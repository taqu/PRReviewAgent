namespace PRReviewAgent.Services.AutoImprove
{
    public sealed record RuleExtractionContext
    {
        public required SourceLanguage Language { get; init; }
        public required string FilePath { get; init; }
        public required string Diff { get; init; }
        public string? AstContext { get; init; }
        public string? FileDependencies { get; init; }
    }
}
