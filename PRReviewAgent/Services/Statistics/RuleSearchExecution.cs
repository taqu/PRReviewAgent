namespace PRReviewAgent.Services.Statistics
{
    public sealed class RuleSearchExecution
    {
        public long Id { get; init; }
        public long ReviewExecutionId { get; init; }
        public long ProjectId { get; init; }
        public int TotalRuleCount { get; init; }
        public int? CandidateRuleCount { get; init; }
        public int? SelectedRuleCount { get; init; }
        public long? EmbeddingDurationMs { get; init; }
        public long? SearchDurationMs { get; init; }
        public long? TotalDurationMs { get; init; }
        public bool Success { get; init; }
        public string? ErrorType { get; init; }
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
    }
}
