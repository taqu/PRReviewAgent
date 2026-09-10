namespace PRReviewAgent.Services.AutoImprove
{
    public sealed record StructuralContext
    {
        public required string FunctionSignature { get; init; }
        public string? ContainingType { get; init; }
        public string? ChangeKind { get; init; }
    }
}
