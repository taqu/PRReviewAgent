namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class RuleLearningEvent
    {
        public long Id { get; init; }
        public long ProjectId { get; init; }
        public string RuleId { get; init; } = string.Empty;
        public string? MergeRequestId { get; init; }
        public RuleLearningEventType EventType { get; init; }
        public double? ConfidenceBefore { get; init; }
        public double? ConfidenceAfter { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
