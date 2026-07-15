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
    public static string Format(IReadOnlyList<ExpandedRegion> regions, string filename)
    {
        if (regions.Count == 0) return "";

        StringBuilder sb = new StringBuilder();
        sb.Append("--- a/").Append(filename).Append('\n');
        sb.Append("+++ b/").Append(filename).Append('\n');

        foreach (ExpandedRegion region in regions)
        {
            sb.Append("@@ -").Append(region.OldStart).Append(',').Append(region.OldCount)
              .Append(" +").Append(region.NewStart).Append(',').Append(region.NewCount)
              .Append(" @@\n");

            foreach (DiffHunkLine line in region.Lines)
                sb.Append(line.Marker).Append(line.Content).Append('\n');
        }

        return sb.ToString();
    }
}
