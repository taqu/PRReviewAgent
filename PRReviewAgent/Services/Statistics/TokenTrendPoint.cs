namespace PRReviewAgent.Services.Statistics
{
    public sealed class TokenTrendPoint
    {
        public string Date { get; init; } = string.Empty; // "yyyy-MM-dd"
        public long InputTokens { get; init; }
        public long OutputTokens { get; init; }
    }
}
