using Microsoft.Extensions.Logging;
using PRReviewAget.Prompt;
using PRReviewAgent.Services;
using System.Text.Json;

namespace PRReviewAgent.Prompt
{
    public sealed class VerificationContextResolver
    {
        public const int DefaultMaxDirectCallers = 5;
        public const int DefaultMaxDirectCallees = 5;
        public const int DefaultMaxTotalItems = 16;
        public const int DefaultMaxSourceChars = 32_000;

        private static readonly JsonSerializerOptions JsonOptions =
            new() { PropertyNameCaseInsensitive = false };

        private enum HintKind { Symbol, DirectCallers, DirectCallees }

        // Tracks mutable resolution state within a single Resolve() call.
        private sealed class State
        {
            private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
            private readonly int _maxItems;
            private readonly int _maxChars;

            public List<SourceContextItem> Items { get; } = new();
            public List<string> Unresolved { get; } = new();
            public bool Truncated { get; private set; }
            public int TotalChars { get; private set; }
            public int CallerCount { get; set; }
            public int CalleeCount { get; set; }

            public State(int maxItems, int maxChars)
            {
                _maxItems = maxItems;
                _maxChars = maxChars;
            }

            public bool TryAdd(string path, string symbol, VerificationContextKind kind, string source)
            {
                if (string.IsNullOrEmpty(source)) return false;
                if (Items.Count >= _maxItems) { Truncated = true; return false; }
                if (TotalChars + source.Length > _maxChars) { Truncated = true; return false; }
                if (!_seen.Add($"{path}\0{symbol}")) return false;
                Items.Add(new SourceContextItem { Path = path, Symbol = symbol, Kind = kind, Source = source });
                TotalChars += source.Length;
                return true;
            }
        }

        public VerificationContext Resolve(
            CandidateIssue candidate,
            IReadOnlyList<ReviewContext> reviewContexts,
            PRReviewAgent.Prompt.Turn1.ReviewBudgetConfig? budget = null,
            ILogger? logger = null)
        {
            budget ??= new PRReviewAgent.Prompt.Turn1.ReviewBudgetConfig();
            var state = new State(budget.VerificationMaxContextItems, budget.VerificationMaxContextChars);
            (string? locFile, string? locSymbol) = ParseLocation(candidate.location);

            Dictionary<string, OutputResult?> astCache = BuildAstCache(reviewContexts);
            ReviewContext? primary = FindContext(reviewContexts, locFile);
            OutputResult? primaryAst = GetAst(astCache, primary?.Path);

            // 1. Changed scope (anchor)
            ResolveChangedScope(state, candidate.location, locSymbol, primary, primaryAst);

            // 2. Counterpart: declaration/definition from pair file
            ResolveCounterpart(state, locSymbol, primary, primaryAst);

            // 3. Containing type of the changed symbol
            ResolveContainingType(state, locSymbol, primary, primaryAst);

            // 4. verify_symbols hints
            if (candidate.verify_symbols != null)
            {
                foreach (string hint in candidate.verify_symbols)
                {
                    if (state.Items.Count >= budget.VerificationMaxContextItems) { break; }
                    ResolveHint(state, hint, locSymbol, primary, primaryAst, reviewContexts, astCache, budget);
                }
            }

            logger?.LogInformation(
                "Verification: candidate={Id} items={Count} estimated_chars={Chars} truncated={Truncated} unresolved={Unresolved}",
                candidate.candidate_id ?? "?", state.Items.Count, state.TotalChars, state.Truncated, state.Unresolved.Count);

            return new VerificationContext
            {
                CandidateId = candidate.candidate_id ?? string.Empty,
                Items = SortItems(state.Items),
                UnresolvedTargets = state.Unresolved,
                Truncated = state.Truncated,
            };
        }

        // -------------------------------------------------------------------------
        // Resolution steps
        // -------------------------------------------------------------------------

        private static void ResolveChangedScope(
            State state, string rawLocation, string? locSymbol,
            ReviewContext? primary, OutputResult? ast)
        {
            if (primary == null) return;
            if (locSymbol == null)
            {
                state.Unresolved.Add($"changed scope: {rawLocation}");
                return;
            }

            FunctionInfo? fn = FindFunction(ast, locSymbol);
            if (fn != null)
            {
                string src = ExtractLines(primary.ChangedFile, fn.StartLine, fn.EndLine);
                state.TryAdd(primary.Path, fn.QualifiedName, VerificationContextKind.ChangedScope, src);
                return;
            }

            TypeInfo? ty = FindType(ast, locSymbol);
            if (ty != null)
            {
                string src = ExtractLines(primary.ChangedFile, ty.StartLine, ty.EndLine);
                state.TryAdd(primary.Path, ty.QualifiedName, VerificationContextKind.ChangedScope, src);
                return;
            }

            state.Unresolved.Add($"changed scope: {rawLocation}");
        }

        private static void ResolveCounterpart(
            State state, string? locSymbol, ReviewContext? primary, OutputResult? ast)
        {
            if (primary == null || string.IsNullOrEmpty(primary.PairFile)) return;
            CounterpartContext? cp = ast?.StructuralDependencies?.CounterpartContext;
            if (cp == null) return;

            string cpPath = !string.IsNullOrEmpty(primary.PairPath) ? primary.PairPath! : cp.File;
            bool cpIsHeader = IsHeader(cpPath);
            VerificationContextKind cpKind = cpIsHeader
                ? VerificationContextKind.Declaration
                : VerificationContextKind.Definition;

            if (locSymbol != null)
            {
                FunctionInfo? cpFn = FindFunction(cp.Functions, locSymbol);
                if (cpFn != null)
                {
                    string src = ExtractLines(primary.PairFile!, cpFn.StartLine, cpFn.EndLine);
                    state.TryAdd(cpPath, cpFn.QualifiedName, cpKind, src);
                }
            }
        }

        private static void ResolveContainingType(
            State state, string? locSymbol, ReviewContext? primary, OutputResult? ast)
        {
            if (primary == null || locSymbol == null) return;

            FunctionInfo? fn = FindFunction(ast, locSymbol);
            string? containingName = fn?.ContainingType;
            if (containingName == null) return;

            // Try in primary file
            TypeInfo? ty = FindType(ast, containingName);
            if (ty != null)
            {
                string src = ExtractLines(primary.ChangedFile, ty.StartLine, ty.EndLine);
                state.TryAdd(primary.Path, ty.QualifiedName, VerificationContextKind.ContainingType, src);
                return;
            }

            // Try in counterpart
            CounterpartContext? cp = ast?.StructuralDependencies?.CounterpartContext;
            if (cp != null && !string.IsNullOrEmpty(primary.PairFile))
            {
                string cpPath = !string.IsNullOrEmpty(primary.PairPath) ? primary.PairPath! : cp.File;
                TypeInfo? cpTy = FindType(cp.Types, containingName);
                if (cpTy != null)
                {
                    string src = ExtractLines(primary.PairFile!, cpTy.StartLine, cpTy.EndLine);
                    state.TryAdd(cpPath, cpTy.QualifiedName, VerificationContextKind.ContainingType, src);
                }
            }
        }

        private static void ResolveHint(
            State state, string hint, string? locSymbol,
            ReviewContext? primary, OutputResult? primaryAst,
            IReadOnlyList<ReviewContext> contexts,
            Dictionary<string, OutputResult?> astCache,
            PRReviewAgent.Prompt.Turn1.ReviewBudgetConfig budget)
        {
            (HintKind hintKind, string target) = ClassifyHint(hint);

            switch (hintKind)
            {
                case HintKind.DirectCallers:
                {
                    string sym = string.IsNullOrEmpty(target) ? (locSymbol ?? string.Empty) : target;
                    foreach ((ReviewContext ctx, FunctionInfo caller) in FindCallers(sym, contexts, astCache))
                    {
                        if (state.CallerCount >= budget.VerificationMaxDirectCallers) break;
                        if (state.Items.Count >= budget.VerificationMaxContextItems) break;
                        string src = ExtractLines(ctx.ChangedFile, caller.StartLine, caller.EndLine);
                        if (state.TryAdd(ctx.Path, caller.QualifiedName, VerificationContextKind.DirectCaller, src))
                            state.CallerCount++;
                    }
                    break;
                }
                case HintKind.DirectCallees:
                {
                    string sym = string.IsNullOrEmpty(target) ? (locSymbol ?? string.Empty) : target;
                    if (primaryAst?.CallGraph != null && primary != null)
                    {
                        string simple = SimpleName(sym);
                        foreach (CallEdge edge in primaryAst.CallGraph)
                        {
                            if (state.CalleeCount >= budget.VerificationMaxDirectCallees) break;
                            if (state.Items.Count >= budget.VerificationMaxContextItems) break;
                            if (!MatchesSymbol(edge.Caller, sym, simple)) continue;
                            (string? path, string? src) = FindSymbolSource(
                                edge.Callee, primary, primaryAst, contexts, astCache);
                            if (path != null && src != null)
                            {
                                if (state.TryAdd(path, edge.Callee, VerificationContextKind.DirectCallee, src))
                                    state.CalleeCount++;
                            }
                        }
                    }
                    break;
                }
                case HintKind.Symbol:
                {
                    ResolveSymbolHint(state, hint, target, primary, primaryAst, contexts, astCache);
                    break;
                }
            }
        }

        private static void ResolveSymbolHint(
            State state, string originalHint, string target,
            ReviewContext? primary, OutputResult? primaryAst,
            IReadOnlyList<ReviewContext> contexts,
            Dictionary<string, OutputResult?> astCache)
        {
            // Try all contexts for a function match
            foreach (ReviewContext ctx in contexts)
            {
                if (!astCache.TryGetValue(ctx.Path, out OutputResult? ast)) continue;
                FunctionInfo? fn = FindFunction(ast, target);
                if (fn != null)
                {
                    string src = ExtractLines(ctx.ChangedFile, fn.StartLine, fn.EndLine);
                    state.TryAdd(ctx.Path, fn.QualifiedName, VerificationContextKind.RelatedDeclaration, src);
                    return;
                }
            }

            // Try as type (or containing type for field hints like "Foo::resource_")
            string typeName = ExtractTypeName(target);
            foreach (ReviewContext ctx in contexts)
            {
                if (!astCache.TryGetValue(ctx.Path, out OutputResult? ast)) continue;
                TypeInfo? ty = FindType(ast, typeName);
                if (ty != null)
                {
                    string src = ExtractLines(ctx.ChangedFile, ty.StartLine, ty.EndLine);
                    bool isField = target.Contains("::") && !string.Equals(typeName, target, StringComparison.Ordinal);
                    state.TryAdd(ctx.Path, ty.QualifiedName,
                        isField ? VerificationContextKind.Field : VerificationContextKind.ReferencedType, src);
                    return;
                }
            }

            // Try counterpart
            if (primary?.PairFile != null && primaryAst?.StructuralDependencies?.CounterpartContext != null)
            {
                CounterpartContext cp = primaryAst.StructuralDependencies.CounterpartContext;
                string cpPath = !string.IsNullOrEmpty(primary.PairPath) ? primary.PairPath! : cp.File;

                FunctionInfo? cpFn = FindFunction(cp.Functions, target);
                if (cpFn != null)
                {
                    string src = ExtractLines(primary.PairFile!, cpFn.StartLine, cpFn.EndLine);
                    state.TryAdd(cpPath, cpFn.QualifiedName, VerificationContextKind.RelatedDeclaration, src);
                    return;
                }

                TypeInfo? cpTy = FindType(cp.Types, typeName);
                if (cpTy != null)
                {
                    string src = ExtractLines(primary.PairFile!, cpTy.StartLine, cpTy.EndLine);
                    bool isField = target.Contains("::") && !string.Equals(typeName, target, StringComparison.Ordinal);
                    state.TryAdd(cpPath, cpTy.QualifiedName,
                        isField ? VerificationContextKind.Field : VerificationContextKind.ContainingType, src);
                    return;
                }
            }

            state.Unresolved.Add(originalHint);
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
                try { cache[ctx.Path] = JsonSerializer.Deserialize<OutputResult>(ctx.AstJson, JsonOptions); }
                catch { cache[ctx.Path] = null; }
            }
            return cache;
        }

        private static OutputResult? GetAst(Dictionary<string, OutputResult?> cache, string? path)
        {
            if (path == null) return null;
            cache.TryGetValue(path, out OutputResult? ast);
            return ast;
        }

        private static ReviewContext? FindContext(IReadOnlyList<ReviewContext> contexts, string? file)
        {
            if (string.IsNullOrEmpty(file)) return contexts.Count > 0 ? contexts[0] : null;

            foreach (ReviewContext ctx in contexts)
            {
                if (string.Equals(ctx.Path, file, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ctx.Filename, file, StringComparison.OrdinalIgnoreCase))
                    return ctx;
            }

            // Suffix match for partial paths
            string fileName = Path.GetFileName(file);
            foreach (ReviewContext ctx in contexts)
            {
                if (ctx.Path.EndsWith(file, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ctx.Filename, fileName, StringComparison.OrdinalIgnoreCase))
                    return ctx;
            }

            return contexts.Count > 0 ? contexts[0] : null;
        }

        private static FunctionInfo? FindFunction(OutputResult? ast, string symbol) =>
            FindFunction(ast?.Functions, symbol);

        private static FunctionInfo? FindFunction(IEnumerable<FunctionInfo>? fns, string symbol)
        {
            if (fns == null || string.IsNullOrEmpty(symbol)) return null;
            string simple = SimpleName(symbol);

            // 1. Exact qualified name
            foreach (FunctionInfo f in fns)
                if (string.Equals(f.QualifiedName, symbol, StringComparison.Ordinal))
                    return f;

            // 2. Suffix (handles missing namespace prefix)
            foreach (FunctionInfo f in fns)
                if (f.QualifiedName.EndsWith("::" + symbol, StringComparison.Ordinal))
                    return f;

            // 3. Simple name match (last component)
            foreach (FunctionInfo f in fns)
                if (string.Equals(SimpleName(f.QualifiedName), simple, StringComparison.Ordinal))
                    return f;

            return null;
        }

        private static TypeInfo? FindType(OutputResult? ast, string symbol) =>
            FindType(ast?.Types, symbol);

        private static TypeInfo? FindType(IEnumerable<TypeInfo>? types, string symbol)
        {
            if (types == null || string.IsNullOrEmpty(symbol)) return null;
            string simple = SimpleName(symbol);

            foreach (TypeInfo t in types)
                if (string.Equals(t.QualifiedName, symbol, StringComparison.Ordinal))
                    return t;

            foreach (TypeInfo t in types)
                if (t.QualifiedName.EndsWith("::" + symbol, StringComparison.Ordinal))
                    return t;

            foreach (TypeInfo t in types)
                if (string.Equals(SimpleName(t.QualifiedName), simple, StringComparison.Ordinal))
                    return t;

            return null;
        }

        private static IEnumerable<(ReviewContext Ctx, FunctionInfo Fn)> FindCallers(
            string target,
            IReadOnlyList<ReviewContext> contexts,
            Dictionary<string, OutputResult?> astCache)
        {
            string simple = SimpleName(target);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var all = new List<(ReviewContext Ctx, FunctionInfo Fn)>();

            foreach (ReviewContext ctx in contexts)
            {
                if (!astCache.TryGetValue(ctx.Path, out OutputResult? ast) || ast?.CallGraph == null) continue;
                foreach (CallEdge edge in ast.CallGraph)
                {
                    if (!MatchesSymbol(edge.Callee, target, simple)) continue;
                    FunctionInfo? callerFn = FindFunction(ast, edge.Caller);
                    if (callerFn == null) continue;
                    if (!seen.Add(callerFn.QualifiedName)) continue;
                    all.Add((ctx, callerFn));
                }
            }

            // Prioritize changed callers, then same-file callers, then stable order
            return all.OrderBy(x => x.Fn.Change != null ? 0 : 1)
                      .ThenBy(x => x.Ctx.Path, StringComparer.OrdinalIgnoreCase);
        }

        private static (string? Path, string? Source) FindSymbolSource(
            string symbol,
            ReviewContext primary,
            OutputResult? primaryAst,
            IReadOnlyList<ReviewContext> contexts,
            Dictionary<string, OutputResult?> astCache)
        {
            // Try primary file first
            FunctionInfo? fn = FindFunction(primaryAst, symbol);
            if (fn != null)
                return (primary.Path, ExtractLines(primary.ChangedFile, fn.StartLine, fn.EndLine));

            // Try all contexts
            foreach (ReviewContext ctx in contexts)
            {
                if (!astCache.TryGetValue(ctx.Path, out OutputResult? ast)) continue;
                fn = FindFunction(ast, symbol);
                if (fn != null)
                    return (ctx.Path, ExtractLines(ctx.ChangedFile, fn.StartLine, fn.EndLine));
            }

            return (null, null);
        }

        // -------------------------------------------------------------------------
        // Source extraction
        // -------------------------------------------------------------------------

        internal static string ExtractLines(string? content, int startLine, int endLine)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;
            string[] lines = content.Split('\n');
            int start = Math.Max(0, startLine - 1);
            int end = Math.Min(lines.Length, endLine);
            if (start >= end) return string.Empty;
            return string.Join('\n', lines[start..end]);
        }

        // -------------------------------------------------------------------------
        // Hint classification
        // -------------------------------------------------------------------------

        private static (HintKind Kind, string Target) ClassifyHint(string hint)
        {
            string lower = hint.Trim();
            if (lower.StartsWith("direct caller", StringComparison.OrdinalIgnoreCase) ||
                lower.StartsWith("callers of", StringComparison.OrdinalIgnoreCase))
            {
                string target = ExtractAfterOf(hint);
                return (HintKind.DirectCallers, target);
            }
            if (lower.StartsWith("direct callee", StringComparison.OrdinalIgnoreCase) ||
                lower.StartsWith("callees of", StringComparison.OrdinalIgnoreCase))
            {
                string target = ExtractAfterOf(hint);
                return (HintKind.DirectCallees, target);
            }
            return (HintKind.Symbol, hint.Trim());
        }

        private static string ExtractAfterOf(string hint)
        {
            int idx = hint.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? hint[(idx + 4)..].Trim() : string.Empty;
        }

        // -------------------------------------------------------------------------
        // Location parsing
        // -------------------------------------------------------------------------

        internal static (string? File, string? Symbol) ParseLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) return (null, null);

            // Format: "path/file.cpp: Symbol::Name"
            int sep = location.IndexOf(": ", StringComparison.Ordinal);
            if (sep >= 0)
            {
                string file = location[..sep].Trim();
                string symbol = location[(sep + 2)..].Trim();
                return (string.IsNullOrEmpty(file) ? null : file,
                        string.IsNullOrEmpty(symbol) ? null : symbol);
            }

            // No separator — treat as file path if it has an extension, otherwise as symbol
            string trimmed = location.Trim();
            if (trimmed.Contains('.') && !trimmed.Contains("::"))
                return (trimmed, null);

            return (null, trimmed);
        }

        // -------------------------------------------------------------------------
        // String helpers
        // -------------------------------------------------------------------------

        private static string SimpleName(string qualified)
        {
            int idx = qualified.LastIndexOf("::", StringComparison.Ordinal);
            return idx >= 0 ? qualified[(idx + 2)..] : qualified;
        }

        // Returns true if `candidate` matches `target` (exact, suffix, or simple-name)
        private static bool MatchesSymbol(string candidate, string target, string simpleTarget)
        {
            if (string.Equals(candidate, target, StringComparison.Ordinal)) return true;
            if (candidate.EndsWith("::" + target, StringComparison.Ordinal)) return true;
            if (string.Equals(SimpleName(candidate), simpleTarget, StringComparison.Ordinal)) return true;
            return false;
        }

        // For "Foo::resource_" returns "Foo"; for "Foo" returns "Foo"
        private static string ExtractTypeName(string hint)
        {
            int idx = hint.IndexOf("::", StringComparison.Ordinal);
            return idx >= 0 ? hint[..idx].Trim() : hint.Trim();
        }

        private static bool IsHeader(string path) =>
            path.EndsWith(".h", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".hxx", StringComparison.OrdinalIgnoreCase);

        // -------------------------------------------------------------------------
        // Output ordering
        // -------------------------------------------------------------------------

        private static List<SourceContextItem> SortItems(List<SourceContextItem> items)
        {
            items.Sort((a, b) =>
            {
                int k = KindOrder(a.Kind).CompareTo(KindOrder(b.Kind));
                if (k != 0) return k;
                int p = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
                if (p != 0) return p;
                return string.Compare(a.Symbol, b.Symbol, StringComparison.OrdinalIgnoreCase);
            });
            return items;
        }

        private static int KindOrder(VerificationContextKind k) => k switch
        {
            VerificationContextKind.ChangedScope => 0,
            VerificationContextKind.Declaration => 1,
            VerificationContextKind.Definition => 2,
            VerificationContextKind.ContainingType => 3,
            VerificationContextKind.Field => 4,
            VerificationContextKind.DirectCaller => 5,
            VerificationContextKind.DirectCallee => 6,
            VerificationContextKind.ReferencedType => 7,
            VerificationContextKind.RelatedDeclaration => 8,
            VerificationContextKind.PairFile => 9,
            _ => 10,
        };
    }
}
