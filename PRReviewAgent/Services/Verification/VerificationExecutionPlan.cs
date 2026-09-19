namespace PRReviewAgent.Services.Verification
{
    public sealed class VerificationExecutionPlan
    {
        public required VerificationExecutionMode Mode { get; init; }
        public int MaxCandidatesPerBatch { get; init; }
        public int MaxBatchInputChars { get; init; }
        public int MaxConcurrentBatches { get; init; }
        public required string Reason { get; init; }
    }
}
