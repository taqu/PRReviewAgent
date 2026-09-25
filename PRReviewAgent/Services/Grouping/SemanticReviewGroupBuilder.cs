using System.Text.Json;
using Microsoft.Extensions.Logging;
using PRReviewAget.Prompt;

namespace PRReviewAgent.Services.Grouping
{
    public static class SemanticReviewGroupBuilder
    {
        // Edge weights — higher means stronger relationship.
        private const int WeightSameContainingType   = 100;
        private const int WeightHeaderSourcePair     = 90;
        private const int WeightDirectCallReference  = 70;
        private const int WeightSharedMeaningfulCallee = 40;
        private const int WeightSameBaseFilename     = 30;
        private const int WeightSameModuleDirectory  = 10;

        // Short names of generic utilities that should not drive grouping.
        private static readonly HashSet<string> GenericCalleeBlocklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "move", "forward", "swap", "min", "max", "get", "exchange",
            "make_shared", "make_unique", "make_pair", "make_tuple",
            "ToString", "Equals", "GetHashCode", "CompareTo", "GetType",
            "Log", "LogError", "LogWarning", "LogInformation", "LogDebug", "LogTrace",
            "Assert", "Trace", "Debug", "Info",
            "malloc", "free", "memcpy", "memset", "strlen",
            "push_back", "emplace_back", "size", "begin", "end", "empty",
            "reserve", "resize", "clear", "find", "insert", "erase",
            "at", "data", "front", "back",
        };

        // Structural root directories to strip when computing module keys.
        private static readonly string[] StructuralRoots =
        {
            "src/", "source/", "include/", "lib/", "libs/",
            "tests/", "test/", "spec/", "specs/",
        };

        // C and C++ file extensions eligible for semantic multi-file grouping.
        private static readonly HashSet<string> CppExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".c", ".cc", ".cpp", ".cxx", ".c++",
            ".h", ".hh", ".hpp", ".hxx", ".h++",
        };

        private const string StrategyCppSemantic = "strategy: cpp-semantic";
        private const string StrategyPerFile      = "strategy: per-file";

        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        // -----------------------------------------------------------------------
        // Public entry point
        // -----------------------------------------------------------------------

        public static List<FileGroup> Build(
            IReadOnlyList<ReviewContext> contexts,
            GroupingConfig config,
            ILogger logger)
        {
            if (contexts.Count == 0)
                return new List<FileGroup>();

            // Partition into C/C++ files (eligible for semantic grouping) and everything else.
            var cppContexts   = contexts.Where(c => IsCppFile(c.Path)).ToList();
            var otherContexts = contexts.Where(c => !IsCppFile(c.Path))
                                        .OrderBy(c => c.Path, StringComparer.Ordinal)
                                        .ToList();

            logger.LogInformation(
                "Grouping: {CppCount} C/C++ file(s), {OtherCount} per-file group(s).",
                cppContexts.Count, otherContexts.Count);

            var allGroups = new List<FileGroup>();

            // C/C++ files — semantic or base-filename grouping.
            if (cppContexts.Count > 0)
            {
                List<FileGroup> cppGroups = config.Mode == GroupingMode.BaseFilename
                    ? BuildByBaseFilename(cppContexts)
                    : BuildSemantic(cppContexts, config, logger);
                foreach (FileGroup fg in cppGroups)
                {
                    if (!fg.GroupingReasons.Contains(StrategyCppSemantic))
                        fg.GroupingReasons.Insert(0, StrategyCppSemantic);
                }
                allGroups.AddRange(cppGroups);
            }

            // Non-C/C++ files — one group per file.
            foreach (ReviewContext ctx in otherContexts)
                allGroups.Add(CreatePerFileGroup(ctx));

            // Final deterministic sort by the first file path in each group.
            allGroups.Sort((a, b) =>
                StringComparer.Ordinal.Compare(
                    a.ReviewContexts[0].Path,
                    b.ReviewContexts[0].Path));

            return allGroups;
        }

        internal static bool IsCppFile(string path)
            => CppExtensions.Contains(Path.GetExtension(path));

        private static FileGroup CreatePerFileGroup(ReviewContext ctx)
        {
            var fg = new FileGroup { Topic = Path.GetFileNameWithoutExtension(ctx.Path) };
            fg.GroupingReasons.Add(StrategyPerFile);
            fg.ReviewContexts.Add(ctx);
            return fg;
        }

        // -----------------------------------------------------------------------
        // BaseFilename fallback
        // -----------------------------------------------------------------------

        internal static List<FileGroup> BuildByBaseFilename(IReadOnlyList<ReviewContext> contexts)
        {
            var groups = new Dictionary<string, FileGroup>(StringComparer.OrdinalIgnoreCase);
            var result = new List<FileGroup>();
            foreach (ReviewContext ctx in contexts)
            {
                string key = Path.GetFileNameWithoutExtension(ctx.Path);
                if (!groups.TryGetValue(key, out FileGroup? g))
                {
                    g = new FileGroup { Topic = key };
                    groups[key] = g;
                    result.Add(g);
                }
                g.ReviewContexts.Add(ctx);
            }
            return result;
        }

        // -----------------------------------------------------------------------
        // Semantic grouping
        // -----------------------------------------------------------------------

        internal static List<FileGroup> BuildSemantic(
            IReadOnlyList<ReviewContext> contexts,
            GroupingConfig config,
            ILogger logger)
        {
            int n = contexts.Count;

            // Parse AST for each context.
            OutputResult?[] asts = new OutputResult?[n];
            for (int i = 0; i < n; i++)
            {
                if (!string.IsNullOrEmpty(contexts[i].AstJson))
                {
                    try { asts[i] = JsonSerializer.Deserialize<OutputResult>(contexts[i].AstJson!, JsonOpts); }
                    catch { /* leave null — graceful fallback */ }
                }
            }

            // Collect changed functions per file.
            List<FunctionInfo>[] changedFunctions = new List<FunctionInfo>[n];
            for (int i = 0; i < n; i++)
            {
                changedFunctions[i] = asts[i]?.Functions?
                    .Where(f => f.Change != null)
                    .ToList() ?? new List<FunctionInfo>();
            }

            // Build all semantic edges.
            List<(int a, int b, int weight, string reason)> edges = BuildEdges(contexts, asts, changedFunctions, n);

            // Sort edges by weight descending, then (a, b) for determinism.
            edges.Sort((x, y) =>
            {
                int w = y.weight.CompareTo(x.weight);
                if (w != 0) return w;
                int a = x.a.CompareTo(y.a);
                if (a != 0) return a;
                return x.b.CompareTo(y.b);
            });

            // Union-Find with group size budget.
            int[] parent = Enumerable.Range(0, n).ToArray();
            int[] rank   = new int[n];
            int[] size   = Enumerable.Repeat(1, n).ToArray();
            // Track reasons per root.
            var rootReasons = new Dictionary<int, List<string>>();

            foreach (var (a, b, weight, reason) in edges)
            {
                int ra = Find(parent, a);
                int rb = Find(parent, b);

                if (ra == rb)
                {
                    RecordReason(rootReasons, ra, reason);
                    continue;
                }

                if (size[ra] + size[rb] > config.MaxFilesPerGroup)
                    continue;

                // Union by rank.
                int newRoot, lostRoot;
                if (rank[ra] < rank[rb])      { parent[ra] = rb; size[rb] += size[ra]; newRoot = rb; lostRoot = ra; }
                else if (rank[ra] > rank[rb]) { parent[rb] = ra; size[ra] += size[rb]; newRoot = ra; lostRoot = rb; }
                else                          { parent[rb] = ra; rank[ra]++;            size[ra] += size[rb]; newRoot = ra; lostRoot = rb; }

                MergeReasons(rootReasons, newRoot, lostRoot, reason);
            }

            // Collect components (file indices per root).
            var components = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(parent, i);
                if (!components.TryGetValue(root, out var list))
                    components[root] = list = new List<int>();
                list.Add(i);
            }

            // Build FileGroups in deterministic path order.
            var ordered = components.Values
                .Select(indices =>
                {
                    var sorted = indices.OrderBy(i => contexts[i].Path, StringComparer.Ordinal).ToList();
                    int root = Find(parent, sorted[0]);
                    rootReasons.TryGetValue(root, out var reasons);
                    return (sorted, reasons ?? new List<string>(), firstPath: contexts[sorted[0]].Path);
                })
                .OrderBy(x => x.firstPath, StringComparer.Ordinal)
                .ToList();

            var fileGroups = new List<FileGroup>();
            foreach (var (indices, reasons, _) in ordered)
            {
                string topic = Path.GetFileNameWithoutExtension(contexts[indices[0]].Path);
                FileGroup fg = new FileGroup { Topic = topic };
                // Strategy tag is inserted first; other reasons follow.
                fg.GroupingReasons.Add(StrategyCppSemantic);
                foreach (string r in reasons.Distinct())
                    fg.GroupingReasons.Add(r);
                foreach (int idx in indices)
                    fg.ReviewContexts.Add(contexts[idx]);
                fileGroups.Add(fg);
            }

            LogGroupDiagnostics(fileGroups, changedFunctions, contexts, logger);
            return fileGroups;
        }

        // -----------------------------------------------------------------------
        // Edge construction
        // -----------------------------------------------------------------------

        private static List<(int a, int b, int weight, string reason)> BuildEdges(
            IReadOnlyList<ReviewContext> contexts,
            OutputResult?[] asts,
            List<FunctionInfo>[] changedFunctions,
            int n)
        {
            var edges = new List<(int, int, int, string)>();

            // Signal 1: Same containing type across files.
            var byContainingType = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
            {
                foreach (FunctionInfo fn in changedFunctions[i])
                {
                    if (string.IsNullOrEmpty(fn.ContainingType)) continue;
                    if (!byContainingType.TryGetValue(fn.ContainingType, out var list))
                        byContainingType[fn.ContainingType] = list = new List<int>();
                    if (!list.Contains(i)) list.Add(i);
                }
            }
            foreach (var (typeName, indices) in byContainingType)
            {
                for (int x = 0; x < indices.Count; x++)
                    for (int y = x + 1; y < indices.Count; y++)
                        AddEdge(edges, indices[x], indices[y], WeightSameContainingType, $"same containing type: {typeName}");
            }

            // Signal 2: Header/source pair (PairPath and CounterpartContext).
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    bool isPair = (!string.IsNullOrEmpty(contexts[i].PairPath) && NormalizePath(contexts[i].PairPath!) == NormalizePath(contexts[j].Path))
                               || (!string.IsNullOrEmpty(contexts[j].PairPath) && NormalizePath(contexts[j].PairPath!) == NormalizePath(contexts[i].Path));

                    if (!isPair)
                    {
                        string? counterpart = asts[i]?.StructuralDependencies?.CounterpartContext?.File;
                        if (!string.IsNullOrEmpty(counterpart) && NormalizePath(counterpart!) == NormalizePath(contexts[j].Path))
                            isPair = true;
                    }
                    if (!isPair)
                    {
                        string? counterpart = asts[j]?.StructuralDependencies?.CounterpartContext?.File;
                        if (!string.IsNullOrEmpty(counterpart) && NormalizePath(counterpart!) == NormalizePath(contexts[i].Path))
                            isPair = true;
                    }

                    if (isPair)
                        AddEdge(edges, i, j, WeightHeaderSourcePair, "header/source pair");
                }
            }

            // Signal 3: Direct changed-symbol call reference.
            // Build map: function base name → set of file indices where it appears as a changed function.
            var changedBaseNames = new HashSet<string>[n];
            for (int i = 0; i < n; i++)
            {
                changedBaseNames[i] = new HashSet<string>(StringComparer.Ordinal);
                foreach (FunctionInfo fn in changedFunctions[i])
                {
                    string bn = GetFunctionBaseName(fn.QualifiedName);
                    if (!string.IsNullOrEmpty(bn))
                        changedBaseNames[i].Add(bn);
                }
            }

            for (int i = 0; i < n; i++)
            {
                var callGraph = asts[i]?.CallGraph;
                if (callGraph == null || callGraph.Count == 0) continue;

                // Callees from changed callers in file i.
                var callees = new HashSet<string>(StringComparer.Ordinal);
                foreach (CallEdge ce in callGraph)
                {
                    if (IsChangedCaller(ce.Caller, changedFunctions[i]))
                        callees.Add(ce.Callee);
                }

                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    foreach (string callee in callees)
                    {
                        string cb = GetCalleeBaseName(callee);
                        if (!string.IsNullOrEmpty(cb) && changedBaseNames[j].Contains(cb))
                        {
                            AddEdge(edges, i, j, WeightDirectCallReference, $"direct call: {cb}");
                            break;
                        }
                    }
                }
            }

            // Signal 4: Shared meaningful callee.
            // callee full name → set of file indices whose changed functions call it.
            var calleeToFiles = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
            {
                var callGraph = asts[i]?.CallGraph;
                if (callGraph == null) continue;
                foreach (CallEdge ce in callGraph)
                {
                    if (!IsChangedCaller(ce.Caller, changedFunctions[i])) continue;
                    string cb = GetCalleeBaseName(ce.Callee);
                    if (string.IsNullOrEmpty(cb) || IsGenericCallee(cb)) continue;
                    if (!calleeToFiles.TryGetValue(ce.Callee, out var fs))
                        calleeToFiles[ce.Callee] = fs = new HashSet<int>();
                    fs.Add(i);
                }
            }

            // Filter callees appearing in more than 40% of files (generic by frequency).
            int frequencyThreshold = Math.Max(2, (int)Math.Ceiling(n * 0.4));
            foreach (var (callee, fileSet) in calleeToFiles)
            {
                if (fileSet.Count > frequencyThreshold) continue;
                var idxList = fileSet.OrderBy(x => x).ToList();
                for (int x = 0; x < idxList.Count; x++)
                    for (int y = x + 1; y < idxList.Count; y++)
                        AddEdge(edges, idxList[x], idxList[y], WeightSharedMeaningfulCallee, $"shared callee: {callee}");
            }

            // Signal 5: Same base filename.
            var byBaseName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
            {
                string key = Path.GetFileNameWithoutExtension(contexts[i].Path);
                if (!byBaseName.TryGetValue(key, out var list))
                    byBaseName[key] = list = new List<int>();
                list.Add(i);
            }
            foreach (var (name, indices) in byBaseName)
            {
                for (int x = 0; x < indices.Count; x++)
                    for (int y = x + 1; y < indices.Count; y++)
                        AddEdge(edges, indices[x], indices[y], WeightSameBaseFilename, $"base filename: {name}");
            }

            // Signal 6: Same module/directory (weak fallback).
            var moduleKeys = new string[n];
            for (int i = 0; i < n; i++)
                moduleKeys[i] = ComputeModuleKey(contexts[i].Path);

            var byModule = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
            {
                string key = moduleKeys[i];
                if (string.IsNullOrEmpty(key)) continue;
                if (!byModule.TryGetValue(key, out var list))
                    byModule[key] = list = new List<int>();
                list.Add(i);
            }
            foreach (var (module, indices) in byModule)
            {
                for (int x = 0; x < indices.Count; x++)
                    for (int y = x + 1; y < indices.Count; y++)
                        AddEdge(edges, indices[x], indices[y], WeightSameModuleDirectory, $"module: {module}");
            }

            return edges;
        }

        // -----------------------------------------------------------------------
        // Union-Find helpers
        // -----------------------------------------------------------------------

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]]; // path compression (halving)
                i = parent[i];
            }
            return i;
        }

        private static void RecordReason(Dictionary<int, List<string>> rootReasons, int root, string reason)
        {
            if (!rootReasons.TryGetValue(root, out var list))
                rootReasons[root] = list = new List<string>();
            if (!list.Contains(reason))
                list.Add(reason);
        }

        private static void MergeReasons(
            Dictionary<int, List<string>> rootReasons,
            int newRoot, int lostRoot, string newReason)
        {
            if (!rootReasons.TryGetValue(newRoot, out var dst))
                rootReasons[newRoot] = dst = new List<string>();
            if (rootReasons.TryGetValue(lostRoot, out var src))
            {
                foreach (string r in src)
                    if (!dst.Contains(r)) dst.Add(r);
                rootReasons.Remove(lostRoot);
            }
            if (!dst.Contains(newReason))
                dst.Add(newReason);
        }

        // -----------------------------------------------------------------------
        // Edge helper
        // -----------------------------------------------------------------------

        private static void AddEdge(
            List<(int a, int b, int weight, string reason)> edges,
            int a, int b, int weight, string reason)
        {
            // Normalize so a < b for deduplication in caller if needed.
            if (a > b) (a, b) = (b, a);
            edges.Add((a, b, weight, reason));
        }

        // -----------------------------------------------------------------------
        // Matching helpers
        // -----------------------------------------------------------------------

        internal static string NormalizePath(string path)
            => path.Replace('\\', '/').TrimStart('/');

        internal static string ComputeModuleKey(string filePath)
        {
            string normalized = NormalizePath(filePath);
            foreach (string root in StructuralRoots)
            {
                if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(root.Length);
                    break;
                }
            }
            int slash = normalized.IndexOf('/');
            if (slash < 0) return string.Empty;
            return normalized.Substring(0, slash);
        }

        // Last segment of a dotted or "::" qualified name (e.g. "Environment.sample" → "sample").
        internal static string GetFunctionBaseName(string qualifiedName)
        {
            if (string.IsNullOrEmpty(qualifiedName)) return string.Empty;
            int dot = qualifiedName.LastIndexOf('.');
            int scope = qualifiedName.LastIndexOf("::");
            int start = Math.Max(dot, scope >= 0 ? scope + 1 : -1);
            return start >= 0 ? qualifiedName.Substring(start + (scope >= 0 && scope == start - 1 ? 0 : 1)) : qualifiedName;
        }

        // Last segment of a call symbol (e.g. "Sampler::Sampler" → "Sampler", "std::move" → "move").
        internal static string GetCalleeBaseName(string callee)
        {
            if (string.IsNullOrEmpty(callee)) return string.Empty;
            // Strip parameters if present.
            int paren = callee.IndexOf('(');
            if (paren >= 0) callee = callee.Substring(0, paren);
            int scope = callee.LastIndexOf("::");
            if (scope >= 0) return callee.Substring(scope + 2);
            int dot = callee.LastIndexOf('.');
            if (dot >= 0) return callee.Substring(dot + 1);
            return callee;
        }

        private static bool IsGenericCallee(string baseName)
            => GenericCalleeBlocklist.Contains(baseName);

        // A caller string (CanonicalId from call graph) is "changed" if it contains
        // the base name of any changed function in this file.
        private static bool IsChangedCaller(string caller, List<FunctionInfo> changedFunctions)
        {
            foreach (FunctionInfo fn in changedFunctions)
            {
                string bn = GetFunctionBaseName(fn.QualifiedName);
                if (!string.IsNullOrEmpty(bn) && caller.Contains(bn, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        // -----------------------------------------------------------------------
        // Diagnostics
        // -----------------------------------------------------------------------

        private static void LogGroupDiagnostics(
            List<FileGroup> groups,
            List<FunctionInfo>[] changedFunctions,
            IReadOnlyList<ReviewContext> contexts,
            ILogger logger)
        {
            // Build reverse map: path → index within the contexts slice passed to BuildSemantic.
            var pathToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < contexts.Count; i++)
                pathToIndex[contexts[i].Path] = i;

            for (int g = 0; g < groups.Count; g++)
            {
                FileGroup fg = groups[g];

                // Extract strategy tag (first reason starting with "strategy:").
                string strategy = fg.GroupingReasons.FirstOrDefault(r => r.StartsWith("strategy:", StringComparison.OrdinalIgnoreCase))
                                  ?? "strategy: unknown";
                logger.LogInformation("Review group {Index}", g);
                logger.LogInformation("  {Strategy}", strategy);
                logger.LogInformation("  files:");
                foreach (ReviewContext rc in fg.ReviewContexts)
                    logger.LogInformation("    {Path}", rc.Path);

                // Collect changed symbols for this group.
                var symbols = new List<string>();
                foreach (ReviewContext rc in fg.ReviewContexts)
                {
                    if (pathToIndex.TryGetValue(rc.Path, out int idx))
                        foreach (FunctionInfo fn in changedFunctions[idx])
                            symbols.Add(fn.QualifiedName);
                }
                if (symbols.Count > 0)
                {
                    logger.LogInformation("  changed symbols:");
                    foreach (string s in symbols)
                        logger.LogInformation("    {Symbol}", s);
                }

                // Log semantic relations (skip the strategy tag itself).
                var relations = fg.GroupingReasons.Where(r => !r.StartsWith("strategy:", StringComparison.OrdinalIgnoreCase)).ToList();
                if (relations.Count > 0)
                {
                    logger.LogInformation("  relations:");
                    foreach (string r in relations)
                        logger.LogInformation("    {Reason}", r);
                }
            }
        }
    }
}
