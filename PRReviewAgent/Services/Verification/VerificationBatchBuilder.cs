using Microsoft.Extensions.Logging;
using PRReviewAgent.Prompt;
using PRReviewAgent.Prompt.Turn1;

namespace PRReviewAgent.Services.Verification
{
    public static class VerificationBatchBuilder
    {
        public static IReadOnlyList<VerificationBatch> Build(
            IReadOnlyList<CandidateIssue> candidates,
            IReadOnlyList<ReviewContext> reviewContexts,
            ReviewBudgetConfig budget,
            string groupId,
            ILogger? logger = null)
        {
            var items = new List<VerificationBatchItem>(candidates.Count);

            foreach (CandidateIssue candidate in candidates)
            {
                VerificationContext ctx = new VerificationContextResolver().Resolve(candidate, reviewContexts, budget, logger);
                if (ctx.Items.Count == 0)
                {
                    logger?.LogWarning(
                        "Skipping candidate {Id}: no context resolved (NoContext)",
                        candidate.candidate_id ?? "?");
                    continue;
                }
                items.Add(new VerificationBatchItem { Candidate = candidate, Context = ctx });
            }

            return BuildFromResolved(items, budget, groupId);
        }

        public static IReadOnlyList<VerificationBatch> BuildFromResolved(
            IReadOnlyList<VerificationBatchItem> items,
            ReviewBudgetConfig budget,
            string groupId)
            => BuildFromResolved(items, budget.MaxCandidatesPerBatch, budget.MaxBatchInputChars, groupId);

        public static IReadOnlyList<VerificationBatch> BuildFromResolved(
            IReadOnlyList<VerificationBatchItem> items,
            int maxCandidatesPerBatch,
            int maxBatchInputChars,
            string groupId)
        {
            var batches = new List<VerificationBatch>();
            int batchIndex = 0;

            var current = new List<VerificationBatchItem>();
            int currentChars = 0;

            void FlushBatch(bool isOversized = false)
            {
                if (current.Count == 0) return;
                batches.Add(new VerificationBatch
                {
                    BatchId = $"{groupId}-batch-{batchIndex}",
                    Items = current.ToList(),
                    EstimatedChars = current.Sum(i => i.EstimatedChars),
                    IsOversized = isOversized,
                });
                batchIndex++;
                current.Clear();
                currentChars = 0;
            }

            foreach (VerificationBatchItem item in items)
            {
                int itemChars = item.EstimatedChars;

                // Single item exceeds char budget — emit as oversized single-item batch
                if (current.Count == 0 && itemChars > maxBatchInputChars)
                {
                    current.Add(item);
                    FlushBatch(isOversized: true);
                    continue;
                }

                // Would exceed candidate or char limit — flush first
                bool exceedsCandidateLimit = current.Count >= maxCandidatesPerBatch;
                bool exceedsCharLimit = current.Count > 0 && (currentChars + itemChars > maxBatchInputChars);

                if (exceedsCandidateLimit || exceedsCharLimit)
                {
                    FlushBatch();
                }

                current.Add(item);
                currentChars += itemChars;
            }

            FlushBatch();

            return batches;
        }
    }
}
