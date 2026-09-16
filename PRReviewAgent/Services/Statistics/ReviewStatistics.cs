namespace PRReviewAgent.Services.Statistics
{
    public sealed class ReviewStatistics
    {
        public ReviewTurnStatistics? Detection { get; init; }
        public ReviewTurnStatistics? Selection { get; init; }
        public long TotalCandidates { get; init; }
        public long TotalFinalFindings { get; init; }
        public double? SelectionRate { get; init; }
        public long CriticalCount { get; init; }
        public long MajorCount { get; init; }
        public long MinorCount { get; init; }
    }
}
