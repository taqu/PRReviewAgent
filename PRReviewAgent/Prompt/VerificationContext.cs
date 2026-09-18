namespace PRReviewAgent.Prompt
{
    public enum VerificationContextKind
    {
        ChangedScope,
        Declaration,
        Definition,
        ContainingType,
        Field,
        DirectCaller,
        DirectCallee,
        ReferencedType,
        RelatedDeclaration,
        PairFile,
    }

    public sealed class SourceContextItem
    {
        public required string Path { get; init; }
        public required string Symbol { get; init; }
        public required VerificationContextKind Kind { get; init; }
        public required string Source { get; init; }
    }

    public sealed class VerificationContext
    {
        public required string CandidateId { get; init; }
        public List<SourceContextItem> Items { get; init; } = new();
        public List<string> UnresolvedTargets { get; init; } = new();
        public bool Truncated { get; init; }
    }
}
