using PRReviewAget.Prompt;
using PRReviewAgent.Services;
using System.Text;
using System.Text.Json;

namespace PRReviewAgent.Prompt.Turn1
{
    public static class SemanticSummaryBuilder
    {
        /// <summary>
        /// Builds a combined semantic summary for a file group.
        /// Returns empty string if no useful information is available.
        /// </summary>
        public static string Build(IReadOnlyList<ReviewContext> contexts)
        {
            List<string> changedSymbols = new List<string>();
            List<string> callEdges = new List<string>();
            List<string> fieldAccesses = new List<string>();
            List<string> counterparts = new List<string>();

            HashSet<string> changedSimpleNames = new HashSet<string>(StringComparer.Ordinal);

            // First pass: collect changed functions and their simple names
            List<(FunctionInfo fn, OutputResult result)> changedFunctions = new List<(FunctionInfo fn, OutputResult result)>();

            foreach (ReviewContext context in contexts)
            {
                if (string.IsNullOrEmpty(context.AstJson)) continue;

                OutputResult? output;
                try
                {
                    output = JsonSerializer.Deserialize<OutputResult>(context.AstJson);
                }
                catch
                {
                    continue;
                }
                if (output == null) continue;

                if (output.Functions != null)
                {
                    foreach (FunctionInfo fn in output.Functions)
                    {
                        if (fn.Change != null)
                        {
                            changedFunctions.Add((fn, output));
                            string simpleName = GetSimpleName(fn.QualifiedName);
                            changedSimpleNames.Add(simpleName);
                            string label = fn.Signature ?? fn.QualifiedName;
                            string entry = $"{label} ({fn.Change})";
                            if (changedSymbols.Count < 20)
                                changedSymbols.Add(entry);
                        }
                    }
                }

                if (output.StructuralDependencies?.CounterpartContext?.File is string cpFile
                    && !string.IsNullOrEmpty(cpFile))
                {
                    if (!counterparts.Contains(cpFile))
                        counterparts.Add(cpFile);
                }
            }

            if (changedSimpleNames.Count == 0 && counterparts.Count == 0)
                return string.Empty;

            // Second pass: call graph edges involving changed symbols
            foreach (ReviewContext context in contexts)
            {
                if (string.IsNullOrEmpty(context.AstJson)) continue;

                OutputResult? output;
                try
                {
                    output = JsonSerializer.Deserialize<OutputResult>(context.AstJson);
                }
                catch
                {
                    continue;
                }
                if (output?.CallGraph == null) continue;

                foreach (CallEdge edge in output.CallGraph)
                {
                    string callerSimple = GetSimpleName(StripParens(edge.Caller));
                    string calleeSimple = GetSimpleName(StripParens(edge.Callee));

                    if (changedSimpleNames.Contains(callerSimple) || changedSimpleNames.Contains(calleeSimple))
                    {
                        string edgeStr = $"{edge.Caller} → {edge.Callee}";
                        if (callEdges.Count < 20 && !callEdges.Contains(edgeStr))
                            callEdges.Add(edgeStr);
                    }
                }
            }

            // Collect field accesses from changed functions
            foreach ((FunctionInfo fn, _) in changedFunctions)
            {
                string fnName = fn.Signature ?? fn.QualifiedName;

                if (fn.FieldReads != null)
                {
                    foreach (string field in fn.FieldReads)
                    {
                        if (fieldAccesses.Count < 15)
                            fieldAccesses.Add($"{fnName} reads {field}");
                    }
                }
                if (fn.FieldWrites != null)
                {
                    foreach (string field in fn.FieldWrites)
                    {
                        if (fieldAccesses.Count < 15)
                            fieldAccesses.Add($"{fnName} writes {field}");
                    }
                }
            }

            // Build output
            bool hasAnything = changedSymbols.Count > 0 || callEdges.Count > 0
                || fieldAccesses.Count > 0 || counterparts.Count > 0;

            if (!hasAnything) return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[SEMANTIC SUMMARY]");

            if (changedSymbols.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Changed symbols:");
                foreach (string sym in changedSymbols)
                    sb.AppendLine($"- {sym}");
            }

            if (callEdges.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Call graph (involving changed symbols):");
                foreach (string edge in callEdges)
                    sb.AppendLine($"- {edge}");
            }

            if (fieldAccesses.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Field accesses in changed symbols:");
                foreach (string fa in fieldAccesses)
                    sb.AppendLine($"- {fa}");
            }

            if (counterparts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Counterpart:");
                foreach (string cp in counterparts)
                    sb.AppendLine($"- {cp}");
            }

            return sb.ToString();
        }

        private static string GetSimpleName(string qualifiedName)
        {
            return qualifiedName.Split('.').Last();
        }

        private static string StripParens(string name)
        {
            int paren = name.IndexOf('(');
            return paren >= 0 ? name.Substring(0, paren) : name;
        }
    }
}
