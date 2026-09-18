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

        public static string Build(
            IReadOnlyList<ReviewContext> reviewContexts,
            ReviewBudgetConfig? budget = null)
            => BuildWithMetrics(reviewContexts, budget).Context;

        public static (string Context, Turn1ContextMetrics Metrics) BuildWithMetrics(
            IReadOnlyList<ReviewContext> reviewContexts,
            ReviewBudgetConfig? budget = null)
        {
            budget ??= new ReviewBudgetConfig();
            Turn1ContextMetrics metrics = new Turn1ContextMetrics
            {
                FileCount = reviewContexts.Count,
            };

            string semanticSummary = SemanticSummaryBuilder.Build(reviewContexts, budget.Turn1SemanticSummaryMaxChars);
            metrics.SemanticSummaryChars = semanticSummary.Length;

            IEnumerable<ReviewContext> ordered = reviewContexts
                .OrderBy(c => IsHeaderFile(c.Path) ? 0 : 1)
                .ThenBy(c => c.Path);

            StringBuilder sb = new StringBuilder();

            if (!string.IsNullOrEmpty(semanticSummary))
            {
                sb.Append(semanticSummary);
                sb.AppendLine();
            }

            int remainingBudget = budget.Turn1MaxSourceChars;

            foreach (ReviewContext context in ordered)
            {
                if (remainingBudget <= 0)
                {
                    metrics.Truncated = true;
                    break;
                }

                string path = context.Path;
                string source = context.ChangedFile ?? string.Empty;
                bool isNew = Turn1SourceFormatter.IsNewFile(context.Diff);

                string section;
                if (string.IsNullOrEmpty(source))
                {
                    section = FormatDiffOnlySection(path, context.Diff);
                    metrics.DiffOnlyCount++;
                }
                else if (source.Length <= budget.Turn1FullFileThresholdChars || remainingBudget >= source.Length)
                {
                    section = Turn1SourceFormatter.FormatFileSection(path, source, context.Diff, isNew);
                    metrics.FullFileCount++;
                }
                else
                {
                    section = FormatLargeFileSection(path, source, context.Diff, context.AstJson, isNew);
                    metrics.PartialFileCount++;
                }

                sb.Append(section);
                metrics.EstimatedSourceChars += section.Length;
                remainingBudget -= section.Length;
            }

            return (sb.ToString(), metrics);
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
                                functionRanges.Add((fn.StartLine, fn.EndLine));
                        }
                    }
                }
                catch { }
            }

            if (functionRanges.Count > 0)
            {
                string[] sourceLines = source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                functionRanges = functionRanges.OrderBy(r => r.Start).ToList();

                foreach ((int Start, int End) range in functionRanges)
                {
                    int startIdx = Math.Max(0, range.Start - 1);
                    int endIdx = Math.Min(sourceLines.Length - 1, range.End - 1);
                    if (startIdx > endIdx) continue;

                    string[] excerptLines = sourceLines.Skip(startIdx).Take(endIdx - startIdx + 1).ToArray();
                    string excerpt = string.Join("\n", excerptLines);

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
