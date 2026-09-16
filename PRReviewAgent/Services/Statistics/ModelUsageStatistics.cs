namespace PRReviewAgent.Services.Statistics
{
    public sealed class ModelUsageStatistics
    {
        public string Model { get; init; } = string.Empty;
        public int Calls { get; init; }
        public long InputTokens { get; init; }
        public long OutputTokens { get; init; }
        public double? AvgDurationMs { get; init; }
        public int FailureCount { get; init; }
    }
}
