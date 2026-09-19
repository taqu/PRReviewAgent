using Microsoft.Extensions.Logging;
using PRReviewAgent.Prompt.Turn1;

namespace PRReviewAgent.Services.Verification
{
    public static class VerificationExecutionPlanner
    {
        public static VerificationExecutionPlan Plan(
            ReviewWorkloadProfile profile,
            AdaptiveVerificationConfig config,
            ReviewBudgetConfig budget,
            ILogger? logger = null)
        {
            try
            {
                return PlanInternal(profile, config, budget, logger);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "VerificationExecutionPlanner threw; returning conservative fallback.");
                return new VerificationExecutionPlan
                {
                    Mode = VerificationExecutionMode.SequentialSingle,
                    MaxCandidatesPerBatch = 1,
                    MaxBatchInputChars = budget.MaxBatchInputChars,
                    MaxConcurrentBatches = 1,
                    Reason = "Planner error; conservative fallback.",
                };
            }
        }

        private static VerificationExecutionPlan PlanInternal(
            ReviewWorkloadProfile profile,
            AdaptiveVerificationConfig config,
            ReviewBudgetConfig budget,
            ILogger? logger)
        {
            int maxBatchInputChars = budget.MaxBatchInputChars;

            // 0 candidates → NoOp
            if (profile.CandidateCount == 0)
            {
                return new VerificationExecutionPlan
                {
                    Mode = VerificationExecutionMode.NoOp,
                    MaxCandidatesPerBatch = 1,
                    MaxBatchInputChars = maxBatchInputChars,
                    MaxConcurrentBatches = 1,
                    Reason = "No candidates to verify.",
                };
            }

            // 1 candidate → SequentialSingle
            if (profile.CandidateCount == 1)
            {
                return new VerificationExecutionPlan
                {
                    Mode = VerificationExecutionMode.SequentialSingle,
                    MaxCandidatesPerBatch = 1,
                    MaxBatchInputChars = maxBatchInputChars,
                    MaxConcurrentBatches = 1,
                    Reason = "Single candidate; sequential single.",
                };
            }

            // Any candidate > LargeCandidateCharsThreshold → conservative
            if (profile.EstimatedVerificationCharsMax > config.LargeCandidateCharsThreshold)
            {
                return new VerificationExecutionPlan
                {
                    Mode = VerificationExecutionMode.SequentialSingle,
                    MaxCandidatesPerBatch = 1,
                    MaxBatchInputChars = maxBatchInputChars,
                    MaxConcurrentBatches = 1,
                    Reason = $"Large candidate context ({profile.EstimatedVerificationCharsMax} chars); conservative sequential.",
                };
            }

            // avg chars > LargeCandidateCharsThreshold → conservative
            if (profile.EstimatedVerificationCharsAverage > config.LargeCandidateCharsThreshold)
            {
                return new VerificationExecutionPlan
                {
                    Mode = VerificationExecutionMode.SequentialSingle,
                    MaxCandidatesPerBatch = 1,
                    MaxBatchInputChars = maxBatchInputChars,
                    MaxConcurrentBatches = 1,
                    Reason = $"High average candidate context ({profile.EstimatedVerificationCharsAverage} chars); conservative sequential.",
                };
            }

            // Few small candidates → SequentialBatch
            if (profile.CandidateCount <= config.SmallCandidateCountThreshold
                && profile.EstimatedVerificationCharsTotal <= config.SmallTotalCharsThreshold)
            {
                int batchSize = Math.Clamp(config.PreferredSmallBatchSize, 1, config.HardMaxCandidatesPerBatch);
                return new VerificationExecutionPlan
                {
                    Mode = VerificationExecutionMode.SequentialBatch,
                    MaxCandidatesPerBatch = batchSize,
                    MaxBatchInputChars = maxBatchInputChars,
                    MaxConcurrentBatches = 1,
                    Reason = $"Few small candidates ({profile.CandidateCount}, {profile.EstimatedVerificationCharsTotal} chars); sequential batch.",
                };
            }

            // Otherwise → ParallelBatch
            int parallelBatchSize = Math.Clamp(config.PreferredMediumBatchSize, 1, config.HardMaxCandidatesPerBatch);
            int concurrency = Math.Clamp(config.PreferredConcurrency, 1, config.HardMaxConcurrentBatches);
            return new VerificationExecutionPlan
            {
                Mode = VerificationExecutionMode.ParallelBatch,
                MaxCandidatesPerBatch = parallelBatchSize,
                MaxBatchInputChars = maxBatchInputChars,
                MaxConcurrentBatches = concurrency,
                Reason = $"Moderate/large workload ({profile.CandidateCount} candidates, {profile.EstimatedVerificationCharsTotal} chars); parallel batch.",
            };
        }
    }
}
