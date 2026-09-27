using System.Text.Json;
using PRReviewAget.Prompt;

namespace PRReviewAgent.Services.Coverage;

public static class ChangedRegionBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Builds changed regions from the diffs in each ReviewContext.
    /// Each diff hunk (@@) that contains at least one added line becomes one region.
    /// Regions are assigned sequential IDs r0, r1, ... ordered by file path then line.
    /// </summary>
    public static IReadOnlyList<ChangedRegion> Build(IReadOnlyList<ReviewContext> contexts)
    {
        var sorted = contexts
            .Where(c => !string.IsNullOrEmpty(c.Diff))
            .OrderBy(c => c.Path, StringComparer.Ordinal)
            .ToList();

        var regions = new List<ChangedRegion>();
        int idx = 0;

        foreach (ReviewContext ctx in sorted)
        {
            List<FunctionInfo>? functions = ParseFunctions(ctx.AstJson);
            string normalizedPath = NormalizePath(ctx.Path);
            IReadOnlyList<IReadOnlyList<int>> hunks = ParseHunks(ctx.Diff);

            foreach (IReadOnlyList<int> hunk in hunks)
            {
                if (hunk.Count == 0) continue;
                int startLine = hunk.Min();
                int endLine = hunk.Max();
                string? symbol = FindContainingSymbol(startLine, functions);

                regions.Add(new ChangedRegion(
                    RegionId: $"r{idx++}",
                    FilePath: normalizedPath,
                    StartLine: startLine,
                    EndLine: endLine,
                    ChangedLines: hunk,
                    ContainingSymbol: symbol));
            }
        }

        return regions;
    }

    // Returns one list of added-line numbers per diff hunk.
    internal static IReadOnlyList<IReadOnlyList<int>> ParseHunks(string? diffText)
    {
        if (string.IsNullOrEmpty(diffText))
            return Array.Empty<IReadOnlyList<int>>();

        var hunks = new List<IReadOnlyList<int>>();
        List<int>? current = null;
        int lineNum = 0;
        bool inHunk = false;

        foreach (string line in diffText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            if (line.StartsWith("@@ "))
            {
                if (current != null && current.Count > 0)
                    hunks.Add(current);
                current = new List<int>();

                int plus = line.IndexOf('+');
                if (plus < 0) { inHunk = false; continue; }
                int end = line.IndexOfAny(new[] { ',', ' ' }, plus + 1);
                string startStr = end > plus + 1
                    ? line.Substring(plus + 1, end - plus - 1)
                    : line.Substring(plus + 1);
                inHunk = int.TryParse(startStr, out lineNum);
                if (!inHunk) current = null;
            }
            else if (inHunk && current != null)
            {
                if (line.Length > 0 && line[0] == '+')       { current.Add(lineNum++); }
                else if (line.Length > 0 && line[0] == '-')  { /* no new-file increment */ }
                else if (line.Length > 0 && line[0] == ' ')  { lineNum++; }
                else if (line.StartsWith("diff ")  || line.StartsWith("index ")
                      || line.StartsWith("--- ")   || line.StartsWith("+++ "))
                { inHunk = false; }
            }
        }

        if (current != null && current.Count > 0)
            hunks.Add(current);

        return hunks;
    }

    // Finds the most specific (smallest-span) function containing the given line.
    internal static string? FindContainingSymbol(int line, List<FunctionInfo>? functions)
    {
        if (functions == null) return null;
        FunctionInfo? best = null;
        int bestSpan = int.MaxValue;
        foreach (FunctionInfo fn in functions)
        {
            if (fn.StartLine <= line && line <= fn.EndLine)
            {
                int span = fn.EndLine - fn.StartLine;
                if (span < bestSpan) { bestSpan = span; best = fn; }
            }
        }
        return best?.QualifiedName;
    }

    private static List<FunctionInfo>? ParseFunctions(string? astJson)
    {
        if (string.IsNullOrEmpty(astJson)) return null;
        try { return JsonSerializer.Deserialize<OutputResult>(astJson, JsonOpts)?.Functions; }
        catch { return null; }
    }

    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        return path.Replace('\\', '/').TrimStart('/');
    }
}
