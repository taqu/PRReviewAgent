using PRReviewAgent.Prompt;
using PRReviewAgent.Services.Coverage;

namespace PRReviewAgent.Services.Recovery;

public static class CandidateUnionBuilder
{
    /// <summary>
    /// Combines primary and recovery candidates into a single IssuesResponse for Turn 2.
    /// Assigns sequential candidate IDs across the combined list.
    /// Returns the combined response and the count of recovery candidates removed as obvious duplicates.
    /// </summary>
    public static (IssuesResponse Combined, int DuplicateCount) Union(
        IssuesResponse primary,
        IssuesResponse? recovery,
        ReviewCoverage primaryCoverage,
        ReviewCoverage recoveryCoverage)
    {
        if (recovery == null || recovery.issues.Length == 0)
            return (primary, 0);

        (Issue[] deduped, int dups) = Deduplicate(primary, recovery, primaryCoverage, recoveryCoverage);

        var all = new Issue[primary.issues.Length + deduped.Length];
        primary.issues.CopyTo(all, 0);
        deduped.CopyTo(all, primary.issues.Length);

        for (int i = 0; i < all.Length; i++)
            all[i].candidate_id = $"c{i}";

        return (new IssuesResponse { issues = all }, dups);
    }

    /// <summary>
    /// Removes obvious duplicates from recovery candidates.
    /// Conservative: only removes a recovery candidate when its location string exactly matches
    /// a primary candidate's location (after normalization).
    /// Ambiguous cases are kept and passed to the existing Turn 2 deduplication.
    /// </summary>
    internal static (Issue[] Deduped, int DuplicateCount) Deduplicate(
        IssuesResponse primary,
        IssuesResponse recovery,
        ReviewCoverage primaryCoverage,
        ReviewCoverage recoveryCoverage)
    {
        var primaryLocations = new HashSet<string>(
            primary.issues.Select(i => NormalizeLocation(i.location)),
            StringComparer.OrdinalIgnoreCase);

        var kept = new List<Issue>(recovery.issues.Length);
        int dups = 0;

        foreach (Issue issue in recovery.issues)
        {
            // Exact normalized location match → obvious duplicate.
            if (primaryLocations.Contains(NormalizeLocation(issue.location)))
            {
                dups++;
                continue;
            }

            kept.Add(issue);
        }

        return (kept.ToArray(), dups);
    }

    internal static string NormalizeLocation(string location)
        => (location ?? string.Empty).Replace('\\', '/').Trim();
}
