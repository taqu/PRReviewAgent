namespace PRReviewAgent.Services.Statistics
{
    public sealed class OverviewStatistics
    {
        public int ProjectCount { get; init; }
        public int ReviewCount { get; init; }
        public int MergeRequestCount { get; init; }
        public long InputTokens { get; init; }
        public long OutputTokens { get; init; }
        public double? AvgReviewDurationMs { get; init; }
        public double? ErrorRate { get; init; }
        public long TotalCandidates { get; init; }
        public long TotalFinalFindings { get; init; }
        public double? SelectionRate { get; init; }
    }
}
