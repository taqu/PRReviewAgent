namespace PRReviewAgent.Services.Recovery;

public sealed class RecoveryContextResult
{
    public static readonly RecoveryContextResult Empty =
        new(Array.Empty<RecoveryFragment>(), 0, 0, 0);

    public IReadOnlyList<RecoveryFragment> Fragments { get; }
    public int EstimatedTokens { get; }
    public int TargetRegionCount { get; }
    public int ExcludedReportedRegionCount { get; }

    public RecoveryContextResult(
        IReadOnlyList<RecoveryFragment> fragments,
        int estimatedTokens,
        int targetRegionCount,
        int excludedReportedRegionCount)
    {
        Fragments = fragments;
        EstimatedTokens = estimatedTokens;
        TargetRegionCount = targetRegionCount;
        ExcludedReportedRegionCount = excludedReportedRegionCount;
    }
}
