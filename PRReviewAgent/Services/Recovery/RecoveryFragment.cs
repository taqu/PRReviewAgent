namespace PRReviewAgent.Services.Recovery;

public sealed record RecoveryFragment(
    string FilePath,
    string? ContainingSymbol,
    int StartLine,
    int EndLine,
    IReadOnlyList<string> TargetRegionIds,
    string DiffText,
    int EstimatedTokens);
