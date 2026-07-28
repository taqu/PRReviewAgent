namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class LearnedRule
    {
        public string Id { get; set; } = string.Empty;
        public string MergeRequestId { get; set; } = string.Empty;
        public string AstPattern { get; set; } = string.Empty;
        public string RuleDescription { get; set; } = string.Empty;
        public string? BadPattern { get; set; }
        public string? GoodPattern { get; set; }
        public float[] Embedding { get; set; } = Array.Empty<float>();
        public int ConfidenceScore { get; set; } = 5;
        public DateTime LastHitAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
