namespace PRReviewAgent.Services.AutoImprove
{
    public sealed record RuleExtractionContext
    {
        public required SourceLanguage Language { get; init; }
        public required string FilePath { get; init; }
        public required string ExpandedDiff { get; init; }
        public IReadOnlyList<StructuralContext> Structures { get; init; } = [];
        public IReadOnlyList<SymbolContext> Symbols { get; init; } = [];
        public IReadOnlyList<DependencyContext> Dependencies { get; init; } = [];
    }
}
