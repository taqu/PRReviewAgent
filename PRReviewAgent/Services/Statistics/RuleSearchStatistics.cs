namespace PRReviewAgent.Services.Statistics
{
    public sealed class RuleSearchStatistics
    {
        public double? AvgScannedCount { get; init; }
        public double? AvgCandidateCount { get; init; }
        public double? AvgSelectedCount { get; init; }
        public double? AvgSearchDurationMs { get; init; }
        public double? AvgEmbeddingDurationMs { get; init; }
    }
}
