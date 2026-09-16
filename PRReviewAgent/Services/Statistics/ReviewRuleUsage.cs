namespace PRReviewAgent.Services.Statistics
{
    public sealed class ReviewRuleUsage
    {
        public long Id { get; init; }
        public long ReviewExecutionId { get; init; }
        public long ProjectId { get; init; }
        public string RuleId { get; init; } = string.Empty;
        public double? SimilarityScore { get; init; }
        public bool UsedInPrompt { get; init; }
        public bool ProducedCandidate { get; init; }
        public bool ProducedFinalFinding { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
