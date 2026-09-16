namespace PRReviewAgent.Services.Statistics
{
    public sealed class RuleStatistics
    {
        public int ActiveRuleCount { get; init; }
        public int CreatedCount { get; init; }
        public int ExpiredCount { get; init; }
        public int ConfidenceIncreaseCount { get; init; }
        public int ConfidenceDecreaseCount { get; init; }
        public RuleSearchStatistics SearchStats { get; init; } = new();
    }
}
