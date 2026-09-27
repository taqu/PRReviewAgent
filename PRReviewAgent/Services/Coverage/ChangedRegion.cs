namespace PRReviewAgent.Services.Coverage;

public sealed record ChangedRegion(
    string RegionId,
    string FilePath,
    int StartLine,
    int EndLine,
    IReadOnlyList<int> ChangedLines,
    string? ContainingSymbol);
