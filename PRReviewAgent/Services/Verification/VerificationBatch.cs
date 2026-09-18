namespace PRReviewAgent.Services.Verification
{
    public sealed class VerificationBatch
    {
        public required string BatchId { get; init; }
        public required IReadOnlyList<VerificationBatchItem> Items { get; init; }
        public int EstimatedChars { get; init; }
        public bool IsOversized { get; init; }
    }
}
