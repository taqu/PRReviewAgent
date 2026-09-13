namespace PRReviewAgent.Services.AutoImprove
{
    public sealed record SymbolContext
    {
        public required string Name { get; init; }
        public string Kind { get; init; } = string.Empty;
    }
}
