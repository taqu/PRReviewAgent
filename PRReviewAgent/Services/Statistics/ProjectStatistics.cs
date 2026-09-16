namespace PRReviewAgent.Services.Statistics
{
    public sealed class ProjectStatistics
    {
        public long ProjectId { get; init; }
        public string ProjectName { get; init; } = string.Empty;
        public int ReviewCount { get; init; }
        public long InputTokens { get; init; }
        public long OutputTokens { get; init; }
        public double? AvgReviewDurationMs { get; init; }
        public double? SelectionRate { get; init; }
        public double? ErrorRate { get; init; }
        public int ActiveRuleCount { get; init; }
        public double? AvgRuleSearchDurationMs { get; init; }
    }
}
