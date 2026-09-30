namespace PRReviewAgent.Services.Coverage;

public sealed class ReviewCoverage
{
    public static readonly ReviewCoverage Empty = new(
        Array.Empty<ChangedRegion>(),
        Array.Empty<CandidateMapping>());

    public IReadOnlyList<ChangedRegion> ChangedRegions { get; }
    public IReadOnlySet<string> ReportedRegionIds { get; }
    public IReadOnlySet<string> UnreportedRegionIds { get; }
    public IReadOnlyList<CandidateMapping> CandidateMappings { get; }

    /// <summary>
    /// Combines two coverage results that share the same changed regions.
    /// Used to compute coverage after both Primary and Recovery detection passes.
    /// </summary>
    public static ReviewCoverage Merge(ReviewCoverage primary, ReviewCoverage recovery)
    {
        var combined = primary.CandidateMappings.Concat(recovery.CandidateMappings).ToList();
        return new ReviewCoverage(primary.ChangedRegions, combined);
    }

    public ReviewCoverage(
        IReadOnlyList<ChangedRegion> regions,
        IReadOnlyList<CandidateMapping> mappings)
    {
        ChangedRegions = regions;
        CandidateMappings = mappings;

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (CandidateMapping m in mappings)
            foreach (string rid in m.RegionIds)
                reported.Add(rid);

        ReportedRegionIds = reported;
        UnreportedRegionIds = regions
            .Select(r => r.RegionId)
            .Where(id => !reported.Contains(id))
            .ToHashSet(StringComparer.Ordinal);
    }
}
