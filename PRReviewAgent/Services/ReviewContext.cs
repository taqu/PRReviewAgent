namespace PRReviewAgent.Services
{
    public sealed class ReviewContext
    {
        public string Path { get; init; } = string.Empty;
        public string Filename { get; init; } = string.Empty;

        public string Diff { get; init; } = string.Empty;

        public string ChangedFile { get; set; } = string.Empty;

        public string? PairPath { get; set; } = string.Empty;
        public string? PairDiff { get; set; } = string.Empty;
        public string? PairFile { get; set; } = string.Empty;

        public string? AstJson { get; set; } = string.Empty;
        public string? ExpandedDiff { get; set; } = string.Empty;
    }
}
