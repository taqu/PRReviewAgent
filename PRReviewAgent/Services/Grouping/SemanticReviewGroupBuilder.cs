using PRReviewAget.Prompt;
using PRReviewAgent.Services;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PRReviewAgent.Services.Grouping
{
    public static class SemanticReviewGroupBuilder
    {
        private static readonly string[] StructuralRoots =
            { "include", "src", "source", "lib", "libs", "tests", "test", "unittest", "unittests" };

        private static readonly JsonSerializerOptions JsonOptions =
            new() { PropertyNameCaseInsensitive = false };

        public static IReadOnlyList<FileGroup> Build(
            IReadOnlyList<ReviewContext> reviewContexts,
            GroupingConfig? config = null,
            ILogger? logger = null)
        {
            config ??= new GroupingConfig();
            if (reviewContexts.Count == 0) return Array.Empty<FileGroup>();

            if (config.Mode == GroupingMode.BaseFilename)
                return BuildBaseFilenameGroups(reviewContexts);

            try
            {
                return BuildSemanticGroups(reviewContexts, config, logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Semantic grouping failed, using path-aware fallback");
                return BuildPathAwareGroups(reviewContexts);
            }
        }

        // -------------------------------------------------------------------------
        // Semantic grouping (main algorithm)
        // -------------------------------------------------------------------------

        private static IReadOnlyList<FileGroup> BuildSemanticGroups(
            IReadOnlyList<ReviewContext> reviewContexts,
            GroupingConfig config,
            ILogger? logger)
        {
            // Index by normalized path
            Dictionary<string, ReviewContext> byPath = reviewContexts
                .ToDictionary(c => NormalizePath(c.Path), StringComparer.OrdinalIgnoreCase);

            // Parse AST
            Dictionary<string, OutputResult?> astCache = BuildAstCache(reviewContexts);

            // Changed function names per file
            Dictionary<string, HashSet<string>> changedFns = BuildChangedFunctionNames(reviewContexts, astCache);

            // Collect edges
            List<(string A, string B, string Reason, bool IsStrong)> edges = new();

            // Pair edges (strong)
            foreach (ReviewContext ctx in reviewContexts)
            {
                if (string.IsNullOrEmpty(ctx.PairPath)) continue;
                string normPair = NormalizePath(ctx.PairPath);
                if (byPath.ContainsKey(normPair))
                    edges.Add((NormalizePath(ctx.Path), normPair, "pair", true));
            }

            // CounterpartContext edges (strong) — from AST structural analysis
            foreach (ReviewContext ctx in reviewContexts)
            {
                string normCtx = NormalizePath(ctx.Path);
                if (!astCache.TryGetValue(normCtx, out OutputResult? ast)) continue;
                string? cpFile = ast?.StructuralDependencies?.CounterpartContext?.File;
                if (string.IsNullOrEmpty(cpFile)) continue;
                string normCp = NormalizePath(cpFile);
                if (byPath.ContainsKey(normCp) && normCp != normCtx)
                    edges.Add((normCtx, normCp, "counterpart", true));
            }

            // Changed-symbol call edges (strong) — pairwise
            string[] paths = reviewContexts.Select(c => NormalizePath(c.Path)).ToArray();
            for (int i = 0; i < paths.Length; i++)
            {
                for (int j = i + 1; j < paths.Length; j++)
                {
                    string pA = paths[i], pB = paths[j];

                    if (HasChangedCallRelationship(pA, pB, astCache, changedFns))
                        edges.Add((pA, pB, "changed-symbol call", true));

                    if (HasChangedTypeRelationship(pA, pB, astCache, changedFns))
                        edges.Add((pA, pB, "type relationship", true));
                }
            }

            // Test-target edges (treated as strong when naming + module match)
            foreach (ReviewContext ctx in reviewContexts)
            {
                string norm = NormalizePath(ctx.Path);
                string? prodBase = GetProductionBaseName(ctx.Path);
                if (prodBase == null) continue;

                foreach (ReviewContext other in reviewContexts)
                {
                    if (other == ctx) continue;
                    string normOther = NormalizePath(other.Path);
                    string otherBase = System.IO.Path.GetFileNameWithoutExtension(other.Path);
                    if (string.Equals(otherBase, prodBase, StringComparison.OrdinalIgnoreCase)
                        && SameModuleKey(norm, normOther))
                    {
                        edges.Add((norm, normOther, "test target", true));
                    }
                }
            }

            // Object-creation edges (medium — only accepted if same module)
            for (int i = 0; i < paths.Length; i++)
            {
                for (int j = i + 1; j < paths.Length; j++)
                {
                    string pA = paths[i], pB = paths[j];
                    if (!SameModuleKey(pA, pB)) continue;
                    if (HasObjectCreationRelationship(pA, pB, astCache, changedFns))
                        edges.Add((pA, pB, "object creation (same module)", false));
                }
            }

            // Build Union-Find with accepted edges
            UnionFind uf = new();
            foreach (string p in paths) uf.Init(p);

            var accepted = new List<(string A, string B, string Reason)>();
            // Strong first
            foreach (var (a, b, reason, strong) in edges.Where(e => e.IsStrong))
            {
                uf.Union(a, b);
                accepted.Add((a, b, reason));
            }
            // Medium (object creation already filtered to same-module above)
            foreach (var (a, b, reason, strong) in edges.Where(e => !e.IsStrong))
            {
                uf.Union(a, b);
                accepted.Add((a, b, reason));
            }

            // Build components
            Dictionary<string, List<ReviewContext>> components = new(StringComparer.OrdinalIgnoreCase);
            foreach (ReviewContext ctx in reviewContexts)
            {
                string root = uf.Find(NormalizePath(ctx.Path));
                if (!components.TryGetValue(root, out var list))
                    components[root] = list = new();
                list.Add(ctx);
            }

            // Build per-root edge reasons
            Dictionary<string, List<string>> rootReasons = new(StringComparer.OrdinalIgnoreCase);
            foreach (var (a, b, reason) in accepted)
            {
                string root = uf.Find(a);
                if (!rootReasons.TryGetValue(root, out var rlist))
                    rootReasons[root] = rlist = new();
                rlist.Add($"{System.IO.Path.GetFileName(a)} \u2194 {System.IO.Path.GetFileName(b)}: {reason}");
            }

            // Create groups, applying size limit
            List<FileGroup> groups = new();
            foreach (var (root, members) in components.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                List<string> reasons = rootReasons.TryGetValue(root, out var r) ? r : new();

                if (members.Count <= config.MaxFilesPerGroup)
                {
                    groups.Add(CreateGroup(members, reasons));
                }
                else
                {
                    groups.AddRange(SplitOversizedGroup(members, config.MaxFilesPerGroup));
                }
            }

            // Sort members within each group, then sort groups by topic
            foreach (FileGroup g in groups) SortGroupMembers(g.ReviewContexts);
            groups.Sort((a, b) => string.Compare(a.Topic, b.Topic, StringComparison.OrdinalIgnoreCase));

            logger?.LogInformation(
                "SemanticGrouping: files={Files} groups={Groups} edges={Edges}",
                reviewContexts.Count, groups.Count, accepted.Count);

            return groups;
        }

        // -------------------------------------------------------------------------
        // Edge detection
        // -------------------------------------------------------------------------

        private static bool HasChangedCallRelationship(
            string pA, string pB,
            Dictionary<string, OutputResult?> astCache,
            Dictionary<string, HashSet<string>> changedFns)
        {
            HashSet<string> cfA = changedFns.TryGetValue(pA, out var a) ? a : EmptySet;
            HashSet<string> cfB = changedFns.TryGetValue(pB, out var b) ? b : EmptySet;
            if (cfA.Count == 0 || cfB.Count == 0) return false;

            // A's call graph: changed fn in A calls changed fn in B
            if (astCache.TryGetValue(pA, out OutputResult? astA) && astA?.CallGraph != null)
                foreach (CallEdge e in astA.CallGraph)
                    if (MatchesAny(e.Caller, cfA) && MatchesAny(e.Callee, cfB)) return true;

            // B's call graph: changed fn in B calls changed fn in A
            if (astCache.TryGetValue(pB, out OutputResult? astB) && astB?.CallGraph != null)
                foreach (CallEdge e in astB.CallGraph)
                    if (MatchesAny(e.Caller, cfB) && MatchesAny(e.Callee, cfA)) return true;

            return false;
        }

        private static bool HasChangedTypeRelationship(
            string pA, string pB,
            Dictionary<string, OutputResult?> astCache,
            Dictionary<string, HashSet<string>> changedFns)
        {
            // Changed types = types that contain a changed function
            HashSet<string> ctA = GetChangedTypes(pA, astCache, changedFns);
            HashSet<string> ctB = GetChangedTypes(pB, astCache, changedFns);
            if (ctA.Count == 0 || ctB.Count == 0) return false;

            // Check if A's changed types inherit from B's changed types
            if (astCache.TryGetValue(pA, out OutputResult? astA) && astA?.Types != null)
                foreach (TypeInfo ty in astA.Types)
                {
                    if (!ctA.Contains(ty.QualifiedName)) continue;
                    if (!string.IsNullOrEmpty(ty.BaseClass) && MatchesAny(ty.BaseClass, ctB)) return true;
                    if (ty.Interfaces != null)
                        foreach (string iface in ty.Interfaces)
                            if (MatchesAny(iface, ctB)) return true;
                }

            // Reverse: B's changed types inherit from A's changed types
            if (astCache.TryGetValue(pB, out OutputResult? astB) && astB?.Types != null)
                foreach (TypeInfo ty in astB.Types)
                {
                    if (!ctB.Contains(ty.QualifiedName)) continue;
                    if (!string.IsNullOrEmpty(ty.BaseClass) && MatchesAny(ty.BaseClass, ctA)) return true;
                    if (ty.Interfaces != null)
                        foreach (string iface in ty.Interfaces)
                            if (MatchesAny(iface, ctA)) return true;
                }

            return false;
        }

        private static bool HasObjectCreationRelationship(
            string pA, string pB,
            Dictionary<string, OutputResult?> astCache,
            Dictionary<string, HashSet<string>> changedFns)
        {
            HashSet<string> cfA = changedFns.TryGetValue(pA, out var a) ? a : EmptySet;
            HashSet<string> cfB = changedFns.TryGetValue(pB, out var b) ? b : EmptySet;
            if (cfA.Count == 0 || cfB.Count == 0) return false;

            HashSet<string> ctB = GetChangedTypes(pB, astCache, changedFns);
            HashSet<string> ctA = GetChangedTypes(pA, astCache, changedFns);

            if (astCache.TryGetValue(pA, out OutputResult? astA) && astA?.Functions != null)
                foreach (FunctionInfo fn in astA.Functions)
                {
                    if (!cfA.Contains(fn.QualifiedName) || fn.ObjectCreations == null) continue;
                    foreach (string c in fn.ObjectCreations)
                        if (MatchesAny(c, ctB)) return true;
                }

            if (astCache.TryGetValue(pB, out OutputResult? astB) && astB?.Functions != null)
                foreach (FunctionInfo fn in astB.Functions)
                {
                    if (!cfB.Contains(fn.QualifiedName) || fn.ObjectCreations == null) continue;
                    foreach (string c in fn.ObjectCreations)
                        if (MatchesAny(c, ctA)) return true;
                }

            return false;
        }

        private static HashSet<string> GetChangedTypes(
            string path,
            Dictionary<string, OutputResult?> astCache,
            Dictionary<string, HashSet<string>> changedFns)
        {
            HashSet<string> cf = changedFns.TryGetValue(path, out var v) ? v : EmptySet;
            if (cf.Count == 0) return EmptySet;
            if (!astCache.TryGetValue(path, out OutputResult? ast) || ast?.Functions == null) return EmptySet;

            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (FunctionInfo fn in ast.Functions)
                if (cf.Contains(fn.QualifiedName) && !string.IsNullOrEmpty(fn.ContainingType))
                    result.Add(fn.ContainingType);
            return result;
        }

        private static readonly HashSet<string> EmptySet = new();

        private static bool MatchesAny(string name, HashSet<string> candidates)
        {
            if (candidates.Contains(name)) return true;
            int idx = name.LastIndexOf("::");
            string simple = idx >= 0 ? name[(idx + 2)..] : name;
            foreach (string c in candidates)
            {
                int ci = c.LastIndexOf("::");
                string cs = ci >= 0 ? c[(ci + 2)..] : c;
                if (string.Equals(simple, cs, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // -------------------------------------------------------------------------
        // AST helpers
        // -------------------------------------------------------------------------

        private static Dictionary<string, OutputResult?> BuildAstCache(IReadOnlyList<ReviewContext> contexts)
        {
            var cache = new Dictionary<string, OutputResult?>(StringComparer.OrdinalIgnoreCase);
            foreach (ReviewContext ctx in contexts)
            {
                if (string.IsNullOrWhiteSpace(ctx.AstJson)) continue;
                try { cache[NormalizePath(ctx.Path)] = JsonSerializer.Deserialize<OutputResult>(ctx.AstJson, JsonOptions); }
                catch { cache[NormalizePath(ctx.Path)] = null; }
            }
            return cache;
        }

        private static Dictionary<string, HashSet<string>> BuildChangedFunctionNames(
            IReadOnlyList<ReviewContext> contexts,
            Dictionary<string, OutputResult?> astCache)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (ReviewContext ctx in contexts)
            {
                string norm = NormalizePath(ctx.Path);
                HashSet<string> names = new(StringComparer.Ordinal);
                if (astCache.TryGetValue(norm, out OutputResult? ast) && ast?.Functions != null)
                    foreach (FunctionInfo fn in ast.Functions)
                        if (fn.Change != null) names.Add(fn.QualifiedName);
                result[norm] = names;
            }
            return result;
        }

        // -------------------------------------------------------------------------
        // Path / module helpers
        // -------------------------------------------------------------------------

        internal static string NormalizePath(string path) =>
            path.Replace('\\', '/').Trim('/');

        internal static string ComputeModuleKey(string normalizedPath)
        {
            string[] parts = normalizedPath.Split('/');
            if (parts.Length <= 1) return string.Empty;

            int start = 0;
            if (parts.Length > 1 && Array.Exists(StructuralRoots, r =>
                string.Equals(r, parts[0], StringComparison.OrdinalIgnoreCase)))
                start = 1;

            // directory parts after the structural root, excluding the filename
            string[] dirs = parts[start..(parts.Length - 1)];
            return string.Join("/", dirs);
        }

        internal static bool SameModuleKey(string normA, string normB) =>
            string.Equals(ComputeModuleKey(normA), ComputeModuleKey(normB), StringComparison.OrdinalIgnoreCase);

        private static bool IsHeaderPath(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext is ".h" or ".hpp" or ".hxx";
        }

        private static bool IsTestFile(string path) => GetProductionBaseName(path) != null;

        private static string? GetProductionBaseName(string path)
        {
            string b = System.IO.Path.GetFileNameWithoutExtension(path);
            string lo = b.ToLowerInvariant();
            if (lo.EndsWith("_test")) return b[..^5];
            if (lo.EndsWith("_tests")) return b[..^6];
            if (lo.StartsWith("test_")) return b[5..];
            if (lo.EndsWith(".test")) return b[..^5];
            if (lo.EndsWith("_spec")) return b[..^5];
            return null;
        }

        // -------------------------------------------------------------------------
        // Group creation / topic / sort / split
        // -------------------------------------------------------------------------

        private static FileGroup CreateGroup(List<ReviewContext> members, List<string> reasons)
        {
            FileGroup g = new FileGroup { Topic = GenerateTopic(members) };
            g.GroupingReasons.AddRange(reasons);
            foreach (ReviewContext ctx in members) g.ReviewContexts.Add(ctx);
            return g;
        }

        private static string GenerateTopic(List<ReviewContext> members)
        {
            ReviewContext anchor =
                members.FirstOrDefault(c => IsHeaderPath(c.Path))
                ?? members.Where(c => !IsTestFile(c.Path)).OrderBy(c => c.Path).FirstOrDefault()
                ?? members.OrderBy(c => c.Path).First();

            string module = ComputeModuleKey(NormalizePath(anchor.Path));
            string baseName = System.IO.Path.GetFileNameWithoutExtension(anchor.Path);
            return string.IsNullOrEmpty(module) ? baseName : $"{module}/{baseName}";
        }

        private static void SortGroupMembers(List<ReviewContext> members)
        {
            members.Sort((a, b) =>
            {
                int ao = IsHeaderPath(a.Path) ? 0 : IsTestFile(a.Path) ? 2 : 1;
                int bo = IsHeaderPath(b.Path) ? 0 : IsTestFile(b.Path) ? 2 : 1;
                if (ao != bo) return ao.CompareTo(bo);
                return string.Compare(NormalizePath(a.Path), NormalizePath(b.Path), StringComparison.OrdinalIgnoreCase);
            });
        }

        private static IEnumerable<FileGroup> SplitOversizedGroup(List<ReviewContext> members, int maxFiles)
        {
            // Build pair map
            var pairOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ReviewContext ctx in members)
                if (!string.IsNullOrEmpty(ctx.PairPath))
                    pairOf[NormalizePath(ctx.Path)] = NormalizePath(ctx.PairPath);

            // Build units (indivisible pairs or singles)
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var units = new List<List<ReviewContext>>();

            var sorted = members.OrderBy(c => IsHeaderPath(c.Path) ? 0 : IsTestFile(c.Path) ? 2 : 1)
                                .ThenBy(c => c.Path);

            foreach (ReviewContext ctx in sorted)
            {
                string norm = NormalizePath(ctx.Path);
                if (processed.Contains(norm)) continue;
                processed.Add(norm);

                var unit = new List<ReviewContext> { ctx };
                if (pairOf.TryGetValue(norm, out string? pairNorm))
                {
                    ReviewContext? pairCtx = members.FirstOrDefault(m =>
                        string.Equals(NormalizePath(m.Path), pairNorm, StringComparison.OrdinalIgnoreCase));
                    if (pairCtx != null && !processed.Contains(pairNorm))
                    {
                        unit.Add(pairCtx);
                        processed.Add(pairNorm);
                    }
                }
                units.Add(unit);
            }

            // Greedily assign units to sub-groups
            var subGroups = new List<List<ReviewContext>>();
            var current = new List<ReviewContext>();
            foreach (var unit in units)
            {
                if (current.Count > 0 && current.Count + unit.Count > maxFiles)
                {
                    subGroups.Add(current);
                    current = new();
                }
                current.AddRange(unit);
            }
            if (current.Count > 0) subGroups.Add(current);

            return subGroups.Select(sg => CreateGroup(sg, new()));
        }

        // -------------------------------------------------------------------------
        // Fallbacks
        // -------------------------------------------------------------------------

        private static IReadOnlyList<FileGroup> BuildBaseFilenameGroups(IReadOnlyList<ReviewContext> reviewContexts)
        {
            var groups = new Dictionary<string, FileGroup>(StringComparer.OrdinalIgnoreCase);
            var result = new List<FileGroup>();
            foreach (ReviewContext ctx in reviewContexts)
            {
                string key = System.IO.Path.GetFileNameWithoutExtension(ctx.Path);
                if (!groups.TryGetValue(key, out FileGroup? g))
                {
                    g = new FileGroup { Topic = key };
                    groups[key] = g;
                    result.Add(g);
                }
                g.ReviewContexts.Add(ctx);
            }
            result.Sort((a, b) => string.Compare(a.Topic, b.Topic, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static IReadOnlyList<FileGroup> BuildPathAwareGroups(IReadOnlyList<ReviewContext> reviewContexts)
        {
            var pathToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> changedNorms = new(
                reviewContexts.Select(c => NormalizePath(c.Path)), StringComparer.OrdinalIgnoreCase);

            // Assign path-aware keys
            foreach (ReviewContext ctx in reviewContexts)
            {
                string norm = NormalizePath(ctx.Path);
                string module = ComputeModuleKey(norm);
                string baseName = System.IO.Path.GetFileNameWithoutExtension(ctx.Path);
                pathToKey[norm] = string.IsNullOrEmpty(module) ? baseName : $"{module}/{baseName}";
            }

            // Merge explicit pairs
            foreach (ReviewContext ctx in reviewContexts)
            {
                if (string.IsNullOrEmpty(ctx.PairPath)) continue;
                string normPair = NormalizePath(ctx.PairPath);
                if (!changedNorms.Contains(normPair)) continue;

                string normSelf = NormalizePath(ctx.Path);
                string selfKey = pathToKey.TryGetValue(normSelf, out string? sk) ? sk : normSelf;
                string? pairKey = pathToKey.TryGetValue(normPair, out string? pk) ? pk : null;
                if (pairKey == null) continue;

                string canonical = IsHeaderPath(ctx.Path) ? selfKey : pairKey;
                pathToKey[normSelf] = canonical;
                pathToKey[normPair] = canonical;
            }

            var groups = new Dictionary<string, FileGroup>(StringComparer.OrdinalIgnoreCase);
            var result = new List<FileGroup>();
            foreach (ReviewContext ctx in reviewContexts)
            {
                string norm = NormalizePath(ctx.Path);
                string key = pathToKey.TryGetValue(norm, out string? k) ? k : norm;
                if (!groups.TryGetValue(key, out FileGroup? g))
                {
                    g = new FileGroup { Topic = key };
                    groups[key] = g;
                    result.Add(g);
                }
                g.ReviewContexts.Add(ctx);
            }
            result.Sort((a, b) => string.Compare(a.Topic, b.Topic, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // -------------------------------------------------------------------------
        // UnionFind
        // -------------------------------------------------------------------------

        private sealed class UnionFind
        {
            private readonly Dictionary<string, string> parent_ = new(StringComparer.OrdinalIgnoreCase);

            public void Init(string x) { if (!parent_.ContainsKey(x)) parent_[x] = x; }

            public string Find(string x)
            {
                Init(x);
                while (!string.Equals(parent_[x], x, StringComparison.OrdinalIgnoreCase))
                {
                    string gp = parent_[parent_[x]];
                    parent_[x] = gp;
                    x = gp;
                }
                return x;
            }

            public void Union(string a, string b)
            {
                string ra = Find(a), rb = Find(b);
                if (!string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase))
                    parent_[ra] = rb;
            }
        }
    }
}
