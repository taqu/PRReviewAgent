namespace PRReviewAgent.Services.Verification
{
    public enum VerificationPolicy { Fixed, Adaptive }

    public sealed class AdaptiveVerificationConfig
    {
        // Policy selection
        public VerificationPolicy Policy { get; init; } = VerificationPolicy.Fixed;

        // Hard limits — always enforced
        public int HardMaxCandidatesPerBatch { get; init; } = 8;
        public int HardMaxConcurrentBatches { get; init; } = 4;

        // Adaptive thresholds
        public int SmallCandidateCountThreshold { get; init; } = 3;
        public int SmallTotalCharsThreshold { get; init; } = 24_000;
        public int LargeCandidateCharsThreshold { get; init; } = 16_000;

        // Adaptive preferred values
        public int PreferredSmallBatchSize { get; init; } = 2;
        public int PreferredMediumBatchSize { get; init; } = 2;
        public int PreferredConcurrency { get; init; } = 2;
    }
}
