namespace PRReviewAgent.Prompt.Turn1
{
    public sealed class ReviewBudgetConfig
    {
        // Turn 1
        public int Turn1MaxSourceChars { get; init; } = 128_000;
        public int Turn1FullFileThresholdChars { get; init; } = 32_000;
        public int Turn1SemanticSummaryMaxChars { get; init; } = 8_000;

        // Verification
        public int VerificationMaxContextChars { get; init; } = 32_000;
        public int VerificationMaxContextItems { get; init; } = 16;
        public int VerificationMaxDirectCallers { get; init; } = 5;
        public int VerificationMaxDirectCallees { get; init; } = 5;
        public int VerificationMaxReferencedTypes { get; init; } = 3;

        // Candidates
        public int MaxCandidatesPerGroup { get; init; } = 8;
        public int MaxVerificationCandidatesPerGroup { get; init; } = 8;

        // Batching
        public int MaxCandidatesPerBatch { get; init; } = 1;
        public int MaxBatchInputChars { get; init; } = 32_000;
        public int MaxConcurrentBatches { get; init; } = 1;
    }
}
