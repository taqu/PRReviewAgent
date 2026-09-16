namespace PRReviewAgent.Services.Statistics
{
    public sealed class ReviewExecution
    {
        public long Id { get; init; }
        public long ProjectId { get; init; }
        public string MergeRequestId { get; init; } = string.Empty;
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
        public long? DurationMs { get; init; }
        public int? CandidateFindingCount { get; init; }
        public int? SelectedFindingCount { get; init; }
        public int? CriticalCount { get; init; }
        public int? MajorCount { get; init; }
        public int? MinorCount { get; init; }
        public ReviewExecutionStatus Status { get; init; }
        public string? ErrorType { get; init; }
    }
}
