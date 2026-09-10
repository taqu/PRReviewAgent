using System.Text.Json;
using PRReviewAget.Prompt;

namespace PRReviewAgent.Services.AutoImprove
{
    public static class RuleContextSelector
    {
        private const int MaxStructures = 8;
        private const int MaxSymbols = 16;
        private const int MaxDependencies = 8;

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = false };

        /// <summary>
        /// Selects compact relevant context from full AST JSON and the expanded diff.
        /// Falls back to empty structural/symbol lists on any failure so the diff-only
        /// extraction path remains available.
        /// </summary>
        public static (IReadOnlyList<StructuralContext> Structures,
                       IReadOnlyList<SymbolContext> Symbols,
                       IReadOnlyList<DependencyContext> Dependencies)
            Select(string? astJson, string expandedDiff, SourceLanguage language)
        {
            IReadOnlyList<DependencyContext> deps = ExtractChangedImports(expandedDiff, language);

            if (string.IsNullOrWhiteSpace(astJson))
                return ([], [], deps);

            try
            {
                OutputResult? result = JsonSerializer.Deserialize<OutputResult>(astJson, JsonOptions);
                if (result == null)
                    return ([], [], deps);

                List<FunctionInfo> changed = result.Functions?
                    .Where(f => !string.IsNullOrEmpty(f.Change))
                    .ToList() ?? [];

                return (BuildStructures(changed), BuildSymbols(changed, result.CallGraph), deps);
            }
            catch
            {
                // Degradation: diff + changed imports only, no full AST fallback
                return ([], [], deps);
            }
        }

        private static IReadOnlyList<StructuralContext> BuildStructures(List<FunctionInfo> changed)
        {
            var list = new List<StructuralContext>();
            foreach (FunctionInfo f in changed)
            {
                if (list.Count >= MaxStructures) break;
                list.Add(new StructuralContext
                {
                    FunctionSignature = f.Signature ?? f.QualifiedName,
                    ContainingType = string.IsNullOrEmpty(f.ContainingType) ? null : f.ContainingType,
                    ChangeKind = f.Change,
                });
            }
            return list;
        }

        private static IReadOnlyList<SymbolContext> BuildSymbols(
            List<FunctionInfo> changed,
            List<CallEdge>? callGraph)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<SymbolContext>();

            void Add(string name, string kind)
            {
                if (list.Count >= MaxSymbols) return;
                if (seen.Add(name))
                    list.Add(new SymbolContext { Name = name, Kind = kind });
            }

            var changedNames = changed
                .Select(f => f.QualifiedName)
                .ToHashSet(StringComparer.Ordinal);

            foreach (FunctionInfo f in changed)
            {
                if (f.FieldReads != null)
                    foreach (string r in f.FieldReads) Add(r, "field_read");
                if (f.FieldWrites != null)
                    foreach (string w in f.FieldWrites) Add(w, "field_write");
                if (f.ObjectCreations != null)
                    foreach (string o in f.ObjectCreations) Add(o, "creates");
            }

            // One-hop call graph: edges where caller is a changed function
            if (callGraph != null)
            {
                foreach (CallEdge edge in callGraph)
                {
                    if (changedNames.Contains(edge.Caller))
                        Add(edge.Callee, "calls");
                }
            }

            return list;
        }

        private static IReadOnlyList<DependencyContext> ExtractChangedImports(
            string expandedDiff, SourceLanguage language)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<DependencyContext>();

            foreach (string line in expandedDiff.Split('\n'))
            {
                if (list.Count >= MaxDependencies) break;
                if (line.Length < 2) continue;

                char prefix = line[0];
                if (prefix != '+' && prefix != '-') continue;

                string content = line[1..].TrimStart();
                (string? kind, string? name) = DetectImport(content, language);
                if (kind == null || name == null) continue;
                if (!seen.Add(name)) continue;

                list.Add(new DependencyContext
                {
                    Name = name,
                    Kind = kind,
                    Change = prefix == '+' ? "added" : "removed",
                });
            }

            return list;
        }

        private static (string? kind, string? name) DetectImport(string content, SourceLanguage language)
        {
            return language switch
            {
                SourceLanguage.C or SourceLanguage.Cpp => DetectCInclude(content),
                SourceLanguage.CSharp => DetectUsing(content),
                SourceLanguage.Python => DetectPythonImport(content),
                SourceLanguage.Rust => DetectRustUse(content),
                _ => (null, null),
            };
        }

        private static (string?, string?) DetectCInclude(string content)
        {
            if (!content.StartsWith("#include")) return (null, null);
            string trimmed = content["#include".Length..].Trim();
            if (trimmed.Length < 3) return (null, null);
            if ((trimmed[0] == '"' && trimmed[^1] == '"') ||
                (trimmed[0] == '<'  && trimmed[^1] == '>'))
                return ("include", trimmed[1..^1]);
            return (null, null);
        }

        private static (string?, string?) DetectUsing(string content)
        {
            // Skip using-statement blocks: "using (" or "using var"
            if (!content.StartsWith("using ") || content.StartsWith("using (") || content.StartsWith("using var"))
                return (null, null);
            string name = content["using ".Length..].TrimEnd(';').Trim();
            return name.Length > 0 ? ("using", name) : (null, null);
        }

        private static (string?, string?) DetectPythonImport(string content)
        {
            if (content.StartsWith("import "))
                return ("import", content["import ".Length..].Trim());
            if (content.StartsWith("from "))
                return ("import", content.Trim());
            return (null, null);
        }

        private static (string?, string?) DetectRustUse(string content)
        {
            if (!content.StartsWith("use ")) return (null, null);
            string name = content["use ".Length..].TrimEnd(';').Trim();
            return name.Length > 0 ? ("use", name) : (null, null);
        }
    }
}
