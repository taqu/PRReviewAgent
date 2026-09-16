namespace PRReviewAgent.Services.Statistics
{
    public sealed class ReviewTrendPoint
    {
        public string Date { get; init; } = string.Empty; // "yyyy-MM-dd"
        public int ReviewCount { get; init; }
        public int SuccessCount { get; init; }
        public int FailureCount { get; init; }
    }
}
