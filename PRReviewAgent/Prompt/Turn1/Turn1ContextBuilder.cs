using PRReviewAget.Prompt;
using PRReviewAgent.Services;
using System.Text;
using System.Text.Json;

namespace PRReviewAgent.Prompt.Turn1
{
    public static class Turn1ContextBuilder
    {
        public const int DefaultMaxTurn1SourceChars = 128_000;
        public const int DefaultFullFileThreshold = 32_000;

        /// <summary>
        /// Builds the Turn 1 context for a file group.
        /// </summary>
        public static string Build(
            IReadOnlyList<ReviewContext> reviewContexts,
            int maxSourceChars = DefaultMaxTurn1SourceChars,
            int fullFileThreshold = DefaultFullFileThreshold)
        {
            string semanticSummary = SemanticSummaryBuilder.Build(reviewContexts);

            // Order: headers first, then by path
            IEnumerable<ReviewContext> ordered = reviewContexts
                .OrderBy(c => IsHeaderFile(c.Path) ? 0 : 1)
                .ThenBy(c => c.Path);

            StringBuilder sb = new StringBuilder();

            if (!string.IsNullOrEmpty(semanticSummary))
            {
                sb.Append(semanticSummary);
                sb.AppendLine();
            }

            int remainingBudget = maxSourceChars;

            foreach (ReviewContext context in ordered)
            {
                if (remainingBudget <= 0) break;

                string path = context.Path;
                string source = context.ChangedFile ?? string.Empty;
                bool isNew = Turn1SourceFormatter.IsNewFile(context.Diff);

                string section;
                if (string.IsNullOrEmpty(source))
                {
                    section = FormatDiffOnlySection(path, context.Diff);
                }
                else if (source.Length <= fullFileThreshold || remainingBudget >= source.Length)
                {
                    section = Turn1SourceFormatter.FormatFileSection(path, source, context.Diff, isNew);
                }
                else
                {
                    section = FormatLargeFileSection(path, source, context.Diff, context.AstJson, isNew);
                }

                sb.Append(section);
                remainingBudget -= section.Length;
            }

            return sb.ToString();
        }

        private static string FormatDiffOnlySection(string path, string? diff)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"================================\nFILE: {path}\n================================\n\n");
            sb.Append("[Full source unavailable — showing diff only]\n\n");

            string diffSummary = Turn1SourceFormatter.FormatDiffSummary(diff);
            if (!string.IsNullOrEmpty(diffSummary))
            {
                sb.Append("--------------------------------\nDIFF\n--------------------------------\n\n");
                sb.Append(diffSummary);
            }

            return sb.ToString();
        }

        private static string FormatLargeFileSection(string path, string source, string? diff, string? astJson, bool isNew)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"================================\nFILE: {path} [partial — file exceeds size threshold]\n================================\n\n");

            // Parse AST to find changed function scopes
            List<(int Start, int End)> functionRanges = new List<(int Start, int End)>();

            if (!string.IsNullOrEmpty(astJson))
            {
                try
                {
                    OutputResult? output = JsonSerializer.Deserialize<OutputResult>(astJson);
                    if (output?.Functions != null)
                    {
                        foreach (FunctionInfo fn in output.Functions)
                        {
                            if (fn.Change != null && fn.StartLine > 0 && fn.EndLine >= fn.StartLine)
                            {
                                functionRanges.Add((fn.StartLine, fn.EndLine));
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore parse errors; fall back to diff only
                }
            }

            if (functionRanges.Count > 0)
            {
                string[] sourceLines = source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                // Sort ranges by start line
                functionRanges = functionRanges.OrderBy(r => r.Start).ToList();

                foreach ((int Start, int End) range in functionRanges)
                {
                    int startIdx = range.Start - 1; // 0-based
                    int endIdx = range.End - 1;

                    if (startIdx < 0) startIdx = 0;
                    if (endIdx >= sourceLines.Length) endIdx = sourceLines.Length - 1;
                    if (startIdx > endIdx) continue;

                    string[] excerptLines = sourceLines.Skip(startIdx).Take(endIdx - startIdx + 1).ToArray();
                    string excerpt = string.Join("\n", excerptLines);

                    // Create a range list that's relative to the excerpt (shifted to 1-based within excerpt)
                    List<(int Start, int End)> relativeRanges = new List<(int Start, int End)>
                    {
                        (1, excerptLines.Length)
                    };

                    string annotated = Turn1SourceFormatter.AnnotateSource(excerpt, relativeRanges);
                    sb.Append(annotated);
                    if (!annotated.EndsWith("\n")) sb.AppendLine();
                    sb.AppendLine();
                }
            }

            string diffSummary = Turn1SourceFormatter.FormatDiffSummary(diff);
            if (!string.IsNullOrEmpty(diffSummary))
            {
                sb.Append("\n--------------------------------\nDIFF\n--------------------------------\n\n");
                sb.Append(diffSummary);
            }

            return sb.ToString();
        }

        private static bool IsHeaderFile(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext is ".h" or ".hpp";
        }
    }
}
