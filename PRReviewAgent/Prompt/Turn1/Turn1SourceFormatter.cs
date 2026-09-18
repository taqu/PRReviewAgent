using System.Text;

namespace PRReviewAgent.Prompt.Turn1
{
    public static class Turn1SourceFormatter
    {
        public const string ChangeBeginMarker = "/* >>>>> CHANGED BEGIN >>>>> */";
        public const string ChangeEndMarker = "/* <<<<< CHANGED END <<<<< */";

        /// <summary>Returns true if diff represents a new file (--- /dev/null).</summary>
        public static bool IsNewFile(string? diff)
        {
            if (string.IsNullOrEmpty(diff)) return false;
            return diff.Contains("--- /dev/null") || diff.Contains("---\t/dev/null");
        }

        /// <summary>Returns 1-based line numbers in the NEW file that are added/modified.</summary>
        public static HashSet<int> ParseModifiedLines(string? diff)
        {
            HashSet<int> result = new HashSet<int>();
            if (string.IsNullOrEmpty(diff)) return result;

            int lineNum = 0;
            bool inHunk = false;

            foreach (string line in diff.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            {
                if (line.StartsWith("@@ "))
                {
                    int plus = line.IndexOf('+');
                    if (plus == -1) { inHunk = false; continue; }
                    int end = line.IndexOfAny(new[] { ',', ' ' }, plus + 1);
                    string startStr = end > plus + 1
                        ? line.Substring(plus + 1, end - plus - 1)
                        : line.Substring(plus + 1);
                    inHunk = int.TryParse(startStr, out lineNum);
                }
                else if (inHunk)
                {
                    if (line.StartsWith("+")) { result.Add(lineNum++); }
                    else if (line.StartsWith("-")) { /* removed — no new-file number */ }
                    else if (line.StartsWith(" ")) { lineNum++; }
                    else if (line.StartsWith("diff ") || line.StartsWith("index ")
                          || line.StartsWith("--- ") || line.StartsWith("+++ "))
                    {
                        inHunk = false;
                    }
                }
            }
            return result;
        }

        /// <summary>Returns sorted contiguous ranges of changed lines [Start, End] (1-based, inclusive).</summary>
        public static List<(int Start, int End)> ParseChangedRanges(string? diff)
        {
            HashSet<int> lines = ParseModifiedLines(diff);
            if (lines.Count == 0) return new List<(int Start, int End)>();

            List<int> sorted = lines.OrderBy(x => x).ToList();
            List<(int Start, int End)> ranges = new List<(int Start, int End)>();

            int start = sorted[0];
            int end = sorted[0];

            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] == end + 1)
                {
                    end = sorted[i];
                }
                else
                {
                    ranges.Add((start, end));
                    start = sorted[i];
                    end = sorted[i];
                }
            }
            ranges.Add((start, end));
            return ranges;
        }

        /// <summary>
        /// Annotates full source with CHANGED markers around changed regions.
        /// changedRanges must be sorted by Start. If empty, returns source unchanged.
        /// </summary>
        public static string AnnotateSource(string source, List<(int Start, int End)> changedRanges)
        {
            if (changedRanges.Count == 0) return source;

            string[] sourceLines = source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            StringBuilder sb = new StringBuilder();

            int rangeIdx = 0;
            bool inChanged = false;

            for (int i = 0; i < sourceLines.Length; i++)
            {
                int lineNo = i + 1; // 1-based

                // Advance past ranges that are behind us
                while (rangeIdx < changedRanges.Count && changedRanges[rangeIdx].End < lineNo)
                {
                    rangeIdx++;
                }

                bool inRange = rangeIdx < changedRanges.Count
                    && lineNo >= changedRanges[rangeIdx].Start
                    && lineNo <= changedRanges[rangeIdx].End;

                if (inRange && !inChanged)
                {
                    sb.AppendLine(ChangeBeginMarker);
                    inChanged = true;
                }
                else if (!inRange && inChanged)
                {
                    sb.AppendLine(ChangeEndMarker);
                    inChanged = false;
                }

                sb.AppendLine(sourceLines[i]);
            }

            if (inChanged)
            {
                sb.AppendLine(ChangeEndMarker);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats a compact diff summary showing only removed/added lines per hunk (no context lines).
        /// Returns empty string if no hunks with removed or added lines.
        /// </summary>
        public static string FormatDiffSummary(string? diff)
        {
            if (string.IsNullOrEmpty(diff)) return string.Empty;

            const int maxChars = 8000;
            StringBuilder sb = new StringBuilder();

            string[] lines = diff.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            string? currentHeader = null;
            List<string> hunkLines = new List<string>();
            bool inHunk = false;

            void FlushHunk()
            {
                if (currentHeader == null) return;
                bool hasContent = hunkLines.Any(l => l.StartsWith("-") || l.StartsWith("+"));
                if (!hasContent) return;

                string headerLine = $"@@ {currentHeader?.Trim()} @@\n";
                string hunkContent = string.Join("\n", hunkLines) + "\n";

                if (sb.Length + headerLine.Length + hunkContent.Length > maxChars) return;

                sb.Append(headerLine);
                sb.Append(hunkContent);
            }

            foreach (string line in lines)
            {
                if (line.StartsWith("@@ "))
                {
                    FlushHunk();
                    hunkLines.Clear();

                    // Extract context: text after second @@
                    string inner = line;
                    int secondAt = inner.IndexOf("@@", 3);
                    if (secondAt >= 0)
                    {
                        currentHeader = inner.Substring(secondAt + 2).Trim();
                        if (string.IsNullOrEmpty(currentHeader))
                            currentHeader = inner.Substring(3, secondAt - 3).Trim();
                        else
                            currentHeader = inner.Substring(3, secondAt - 3).Trim() + " " + currentHeader;
                    }
                    else
                    {
                        currentHeader = inner.Substring(3).Trim();
                    }
                    inHunk = true;
                }
                else if (inHunk)
                {
                    if (line.StartsWith("-") || line.StartsWith("+"))
                    {
                        hunkLines.Add(line);
                    }
                    else if (line.StartsWith("diff ") || line.StartsWith("index ")
                          || line.StartsWith("--- ") || line.StartsWith("+++ "))
                    {
                        FlushHunk();
                        hunkLines.Clear();
                        currentHeader = null;
                        inHunk = false;
                    }
                    // Skip context lines (starting with ' ')
                }
            }
            FlushHunk();

            return sb.ToString();
        }

        /// <summary>
        /// Formats a complete file section: header + annotated source + diff summary.
        /// </summary>
        public static string FormatFileSection(string path, string source, string? diff, bool isNew)
        {
            string label = isNew ? "NEW FILE" : "FILE";
            StringBuilder sb = new StringBuilder();
            sb.Append($"================================\n{label}: {path}\n================================\n\n");

            if (!string.IsNullOrEmpty(source))
            {
                if (isNew)
                {
                    // For new files, no markers needed — everything is new
                    sb.Append(source);
                    if (!source.EndsWith("\n")) sb.AppendLine();
                }
                else
                {
                    List<(int Start, int End)> ranges = ParseChangedRanges(diff);
                    string annotated = AnnotateSource(source, ranges);
                    sb.Append(annotated);
                    if (!annotated.EndsWith("\n")) sb.AppendLine();
                }
            }

            string diffSummary = FormatDiffSummary(diff);
            if (!string.IsNullOrEmpty(diffSummary))
            {
                sb.Append("\n--------------------------------\nDIFF\n--------------------------------\n\n");
                sb.Append(diffSummary);
            }

            return sb.ToString();
        }
    }
}
