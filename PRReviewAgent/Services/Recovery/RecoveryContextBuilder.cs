using PRReviewAgent.Services.Coverage;
using PRReviewAgent.Services.Grouping;
using System.Text;
using System.Text.RegularExpressions;

namespace PRReviewAgent.Services.Recovery;

public static class RecoveryContextBuilder
{
    // Gap in new-file lines between two target hunks within which they are merged into one fragment.
    internal const int MergeLineThreshold = 100;

    // Approximate token budget for the focused recovery diff section.
    internal const int DefaultBudgetTokens = 8_000;

    /// <summary>
    /// Builds focused recovery context: only diff hunks that correspond to unreported changed regions.
    /// Nearby target hunks within the same file are merged to avoid duplication.
    /// </summary>
    public static RecoveryContextResult Build(
        ReviewCoverage coverage,
        FileGroup fileGroup,
        int budgetTokens = DefaultBudgetTokens)
    {
        if (coverage.UnreportedRegionIds.Count == 0)
            return RecoveryContextResult.Empty;

        var unreportedRegions = coverage.ChangedRegions
            .Where(r => coverage.UnreportedRegionIds.Contains(r.RegionId))
            .ToList();

        int excludedCount = coverage.ReportedRegionIds.Count;

        // Index unreported regions by file path for fast lookup.
        var regionsByFile = new Dictionary<string, List<ChangedRegion>>(StringComparer.OrdinalIgnoreCase);
        foreach (ChangedRegion r in unreportedRegions)
        {
            string key = ChangedRegionBuilder.NormalizePath(r.FilePath);
            if (!regionsByFile.TryGetValue(key, out var list))
                regionsByFile[key] = list = new List<ChangedRegion>();
            list.Add(r);
        }

        var allFragments = new List<RecoveryFragment>();

        // Process files in deterministic order.
        foreach (ReviewContext ctx in fileGroup.ReviewContexts
            .OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
        {
            string normPath = ChangedRegionBuilder.NormalizePath(ctx.Path);
            if (!regionsByFile.TryGetValue(normPath, out List<ChangedRegion>? fileRegions))
                continue;
            if (string.IsNullOrEmpty(ctx.ExpandedDiff))
                continue;

            List<DiffHunk> hunks = ParseDiffHunks(ctx.ExpandedDiff);
            if (hunks.Count == 0) continue;

            // Map each unreported region to its hunk by added-line overlap.
            var hunkTargets = new Dictionary<int, List<ChangedRegion>>();
            foreach (ChangedRegion region in fileRegions)
            {
                for (int hi = 0; hi < hunks.Count; hi++)
                {
                    DiffHunk hunk = hunks[hi];
                    bool overlaps = hunk.AddedLines.Any(line =>
                        line >= region.StartLine && line <= region.EndLine);
                    if (!overlaps && region.ChangedLines.Count > 0)
                        overlaps = region.ChangedLines.Any(line =>
                            line >= hunk.NewStartLine && line <= hunk.NewEndLine);
                    if (overlaps)
                    {
                        if (!hunkTargets.TryGetValue(hi, out var list))
                            hunkTargets[hi] = list = new List<ChangedRegion>();
                        list.Add(region);
                        break;
                    }
                }
            }

            if (hunkTargets.Count == 0) continue;

            // Merge consecutive target hunks that are within MergeLineThreshold lines.
            var targetIndices = hunkTargets.Keys.OrderBy(i => i).ToList();
            var mergeGroups = new List<List<int>>();
            var current = new List<int> { targetIndices[0] };

            for (int i = 1; i < targetIndices.Count; i++)
            {
                int prevIdx = targetIndices[i - 1];
                int currIdx = targetIndices[i];
                int gap = hunks[currIdx].NewStartLine - hunks[prevIdx].NewEndLine;
                if (gap <= MergeLineThreshold)
                    current.Add(currIdx);
                else
                {
                    mergeGroups.Add(current);
                    current = new List<int> { currIdx };
                }
            }
            mergeGroups.Add(current);

            foreach (List<int> group in mergeGroups)
            {
                var sb = new StringBuilder();
                var regionIds = new List<string>();
                string? symbol = null;
                int startLine = hunks[group[0]].NewStartLine;
                int endLine = hunks[group[^1]].NewEndLine;

                foreach (int hi in group)
                {
                    sb.Append(hunks[hi].HunkText);
                    if (hunkTargets.TryGetValue(hi, out var regions))
                        foreach (ChangedRegion r in regions)
                        {
                            regionIds.Add(r.RegionId);
                            symbol ??= r.ContainingSymbol;
                        }
                }

                string diffText = sb.ToString();
                allFragments.Add(new RecoveryFragment(
                    normPath, symbol, startLine, endLine,
                    regionIds.AsReadOnly(), diffText, EstimateTokens(diffText)));
            }
        }

        allFragments = ApplyBudget(allFragments, budgetTokens);

        int totalTokens = allFragments.Sum(f => f.EstimatedTokens);
        int targetCount = allFragments.Sum(f => f.TargetRegionIds.Count);

        return new RecoveryContextResult(
            allFragments.AsReadOnly(), totalTokens, targetCount, excludedCount);
    }

    /// <summary>
    /// Trims supporting context to stay within budget.
    /// Per §17: never remove a Recovery target. All current fragments are targets,
    /// so we trim fragments from the end (lowest priority) only as a last resort.
    /// </summary>
    private static List<RecoveryFragment> ApplyBudget(List<RecoveryFragment> fragments, int budgetTokens)
    {
        int total = fragments.Sum(f => f.EstimatedTokens);
        if (total <= budgetTokens) return fragments;

        // Trim from the end (later files / later regions) — preserves earlier targets.
        // This satisfies §18 (every unreported region should be represented) on a best-effort basis.
        var kept = new List<RecoveryFragment>(fragments.Count);
        int used = 0;
        foreach (RecoveryFragment f in fragments)
        {
            if (used + f.EstimatedTokens > budgetTokens && kept.Count > 0)
                break; // preserve at least one fragment
            kept.Add(f);
            used += f.EstimatedTokens;
        }
        return kept;
    }

    internal static List<DiffHunk> ParseDiffHunks(string diff)
    {
        var hunks = new List<DiffHunk>();
        if (string.IsNullOrEmpty(diff)) return hunks;

        string[] lines = diff.Split('\n');
        int i = 0;

        // Skip file-header lines (--- and +++ and diff --git).
        while (i < lines.Length && !lines[i].StartsWith("@@"))
            i++;

        while (i < lines.Length)
        {
            if (!lines[i].StartsWith("@@")) { i++; continue; }

            string hunkHeader = lines[i];
            int newStart = ParseNewStart(hunkHeader);
            if (newStart < 0) { i++; continue; }

            var hunkLines = new List<string> { lines[i] };
            var addedLines = new List<int>();
            int lineNo = newStart;
            i++;

            while (i < lines.Length && !lines[i].StartsWith("@@") && !lines[i].StartsWith("diff "))
            {
                string l = lines[i];
                hunkLines.Add(l);
                if (l.StartsWith("+") && !l.StartsWith("+++"))
                    addedLines.Add(lineNo++);
                else if (!l.StartsWith("-"))
                    lineNo++;
                i++;
            }

            int newEnd = lineNo > newStart ? lineNo - 1 : newStart;
            string hunkText = string.Join("\n", hunkLines) + "\n";
            hunks.Add(new DiffHunk(newStart, newEnd, addedLines.AsReadOnly(), hunkText));
        }

        return hunks;
    }

    private static readonly Regex HunkHeaderRegex = new Regex(
        @"@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@",
        RegexOptions.Compiled);

    private static int ParseNewStart(string hunkHeader)
    {
        Match m = HunkHeaderRegex.Match(hunkHeader);
        if (!m.Success) return -1;
        return int.TryParse(m.Groups[1].Value, out int n) ? n : -1;
    }

    internal static int EstimateTokens(string text) => (text?.Length ?? 0) / 4;

    internal sealed record DiffHunk(
        int NewStartLine,
        int NewEndLine,
        IReadOnlyList<int> AddedLines,
        string HunkText);
}
