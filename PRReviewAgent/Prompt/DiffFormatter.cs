
using System.Collections.Generic;
using System.Text;

namespace PRReviewAget.Prompt;

/// <summary>
/// Converts a list of <see cref="ExpandedRegion"/> objects into unified diff format.
/// This class is independent of Tree-Sitter and contains no AST or language-specific logic.
/// </summary>
public static class DiffFormatter
{
    /// <summary>
    /// Formats <paramref name="regions"/> as a unified diff string.
    /// </summary>
    /// <param name="regions">
    /// Expanded regions in ascending source order (as produced by
    /// <see cref="DiffExpander.Expand(string,string)"/>).
    /// Overlapping regions must already be merged by the caller.
    /// </param>
    /// <param name="filename">
    /// The source filename to emit in the <c>---</c>/<c>+++</c> header lines.
    /// </param>
    /// <returns>
    /// A unified diff string where every line of each region is emitted with either a
    /// <c>'+'</c> prefix (the line is in <see cref="ExpandedRegion.ChangedLines"/>)
    /// or a <c>' '</c> prefix (context). Deletion lines are not emitted.
    /// Returns an empty string when <paramref name="regions"/> is empty.
    /// </returns>
    public static string Format(IReadOnlyList<ExpandedRegion> regions, string filename)
    {
        if (regions.Count == 0) return "";

        StringBuilder sb = new StringBuilder();
        sb.Append("--- a/").Append(filename).Append('\n');
        sb.Append("+++ b/").Append(filename).Append('\n');

        foreach (ExpandedRegion region in regions)
        {
            HashSet<int> changedSet = new HashSet<int>(region.ChangedLines);

            // Code is built with '\n' separators by ExtractCode; preserve original content.
            string[] regionLines = region.Code.Split('\n');
            int lineCount = regionLines.Length;

            sb.Append("@@ -").Append(region.StartLine).Append(',').Append(lineCount)
              .Append(" +").Append(region.StartLine).Append(',').Append(lineCount)
              .Append(" @@\n");

            for (int i = 0; i < lineCount; i++)
            {
                int lineNum = region.StartLine + i;
                sb.Append(changedSet.Contains(lineNum) ? '+' : ' ')
                  .Append(regionLines[i])
                  .Append('\n');
            }
        }

        return sb.ToString();
    }
}
