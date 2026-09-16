namespace PRReviewAgent.Services.Statistics
{
    public sealed class ReviewTurnStatistics
    {
        public int TurnType { get; init; } // 1=Detection, 2=Selection
        public int Calls { get; init; }
        public int SuccessfulCalls { get; init; }
        public int FailedCalls { get; init; }
        public long InputTokens { get; init; }
        public long OutputTokens { get; init; }
        public double? AvgDurationMs { get; init; }
        public double? AvgFindingCount { get; init; }
    }
}
