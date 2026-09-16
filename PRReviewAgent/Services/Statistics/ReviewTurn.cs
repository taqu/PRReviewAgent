namespace PRReviewAgent.Services.Statistics
{
    public sealed class ReviewTurn
    {
        public long Id { get; init; }
        public long ReviewExecutionId { get; init; }
        public ReviewTurnType TurnType { get; init; }
        public string Model { get; init; } = string.Empty;
        public int? InputTokens { get; init; }
        public int? OutputTokens { get; init; }
        public long? DurationMs { get; init; }
        public int? FindingCount { get; init; }
        public bool Success { get; init; }
        public string? ErrorType { get; init; }
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
    }
}
