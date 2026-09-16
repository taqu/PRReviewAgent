namespace PRReviewAgent.Services.Statistics
{
    public sealed record ReviewExecutionResult(
        DateTimeOffset CompletedAt,
        long DurationMs,
        int CandidateFindingCount,
        int SelectedFindingCount,
        int CriticalCount,
        int MajorCount,
        int MinorCount
    );
}
