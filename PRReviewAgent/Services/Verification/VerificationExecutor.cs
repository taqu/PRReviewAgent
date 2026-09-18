using Microsoft.Extensions.Logging;
using PRReviewAgent.Prompt;
using PRReviewAgent.Prompt.Turn1;
using PRReviewAgent.Services.Statistics;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace PRReviewAgent.Services.Verification
{
    public static class VerificationExecutor
    {
        public static async Task<(IReadOnlyList<VerifiedIssue> Results, VerificationExecutorMetrics Metrics)> ExecuteAsync(
            ReviewRequest reviewRequest,
            IReadOnlyList<VerificationBatch> batches,
            Func<string, CancellationToken, Task<(VerifiedResponse?, int?, int?)>> llmCall,
            ReviewBudgetConfig budget,
            IReviewTurnRecorder? turnRecorder,
            long? executionId,
            string model,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            // Build order map: candidate_id -> slot index
            var orderMap = new Dictionary<string, int>(StringComparer.Ordinal);
            int slotIndex = 0;
            foreach (VerificationBatch batch in batches)
            {
                foreach (VerificationBatchItem item in batch.Items)
                {
                    string id = item.Candidate.candidate_id ?? string.Empty;
                    if (!orderMap.ContainsKey(id))
                        orderMap[id] = slotIndex++;
                }
            }

            VerifiedIssue?[] resultSlots = new VerifiedIssue?[orderMap.Count];

            SemaphoreSlim sem = new SemaphoreSlim(Math.Max(1, budget.MaxConcurrentBatches));

            var metrics = new VerificationExecutorMetrics
            {
                BatchCount = batches.Count,
                TotalCandidates = orderMap.Count,
                ConfiguredMaxConcurrency = budget.MaxConcurrentBatches,
            };

            // Thread-safe accumulators
            long[] batchDurAcc = new long[1];
            int[] successAcc = new int[1];
            int[] failAcc = new int[1];
            int[] retryAcc = new int[1];
            int[] splitAcc = new int[1];
            int[] singleAcc = new int[1];
            int[] verifiedAcc = new int[1];

            Stopwatch sw = Stopwatch.StartNew();

            Task[] batchTasks = batches
                .Select(batch => ExecuteBatchWithRetryAsync(
                    batch, reviewRequest, llmCall, budget,
                    turnRecorder, executionId, model, logger, cancellationToken,
                    successAcc, failAcc, retryAcc, splitAcc, singleAcc, verifiedAcc, batchDurAcc,
                    orderMap, resultSlots, sem))
                .ToArray();

            await Task.WhenAll(batchTasks);

            sw.Stop();

            metrics.BatchSuccessCount = Volatile.Read(ref successAcc[0]);
            metrics.BatchFailureCount = Volatile.Read(ref failAcc[0]);
            metrics.BatchRetryCount = Volatile.Read(ref retryAcc[0]);
            metrics.BatchSplitCount = Volatile.Read(ref splitAcc[0]);
            metrics.SingleCandidateFallbackCount = Volatile.Read(ref singleAcc[0]);
            metrics.VerifiedCount = Volatile.Read(ref verifiedAcc[0]);
            metrics.WallClockMs = sw.ElapsedMilliseconds;
            metrics.TotalBatchDurationMs = Volatile.Read(ref batchDurAcc[0]);

            // Collect results in original order, skipping nulls
            var results = new List<VerifiedIssue>(orderMap.Count);
            foreach (VerifiedIssue? vi in resultSlots)
            {
                if (vi.HasValue)
                    results.Add(vi.Value);
            }

            return (results, metrics);
        }

        private static async Task ExecuteBatchWithRetryAsync(
            VerificationBatch batch,
            ReviewRequest reviewRequest,
            Func<string, CancellationToken, Task<(VerifiedResponse?, int?, int?)>> llmCall,
            ReviewBudgetConfig budget,
            IReviewTurnRecorder? turnRecorder,
            long? executionId,
            string model,
            ILogger logger,
            CancellationToken cancellationToken,
            int[] successAcc,
            int[] failAcc,
            int[] retryAcc,
            int[] splitAcc,
            int[] singleAcc,
            int[] verifiedAcc,
            long[] batchDurAcc,
            Dictionary<string, int> orderMap,
            VerifiedIssue?[] resultSlots,
            SemaphoreSlim sem)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            await sem.WaitAsync(cancellationToken);

            bool semReleased = false;
            Stopwatch batchSw = Stopwatch.StartNew();

            long? turnId = null;
            if (turnRecorder != null && executionId.HasValue)
            {
                try
                {
                    turnId = await turnRecorder.StartAsync(
                        executionId.Value, ReviewTurnType.Verification,
                        model, DateTimeOffset.UtcNow, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to start Verification turn record for batch {BatchId}", batch.BatchId);
                }
            }

            try
            {
                string prompt = PromptBuilder.BuildTurn2Batch(reviewRequest, batch, new StringBuilder());
                (VerifiedResponse? verResp, int? inputTok, int? outputTok) = await llmCall(prompt, cancellationToken);

                // Validate response
                string? validationError = ValidateResponse(verResp, batch);
                if (validationError != null)
                {
                    batchSw.Stop();
                    Interlocked.Increment(ref failAcc[0]);
                    Interlocked.Add(ref batchDurAcc[0], batchSw.ElapsedMilliseconds);

                    if (turnRecorder != null && turnId.HasValue)
                    {
                        try { await turnRecorder.CompleteFailureAsync(turnId.Value, inputTok, outputTok, "ValidationError", DateTimeOffset.UtcNow, batchSw.ElapsedMilliseconds, cancellationToken); }
                        catch (Exception rex) { logger.LogError(rex, "Failed to complete Verification turn record"); }
                    }

                    if (!semReleased) { sem.Release(); semReleased = true; }

                    // Retry by splitting
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        if (batch.Items.Count > 1)
                        {
                            logger.LogWarning(
                                "Splitting batch {BatchId} ({Count} candidates) for retry: {Reason}",
                                batch.BatchId, batch.Items.Count, validationError);
                            Interlocked.Increment(ref splitAcc[0]);
                            await SplitAndRetryAsync(batch, reviewRequest, llmCall, budget,
                                null, executionId, model, logger, cancellationToken,
                                successAcc, failAcc, retryAcc, splitAcc, singleAcc, verifiedAcc, batchDurAcc,
                                orderMap, resultSlots, sem);
                        }
                        else
                        {
                            string candidateId = batch.Items[0].Candidate.candidate_id ?? "?";
                            logger.LogWarning(
                                "Single-candidate verification failed for {CandidateId}: {Reason}",
                                candidateId, validationError);
                            Interlocked.Increment(ref singleAcc[0]);
                        }
                    }
                    return;
                }

                // Success — write results into slots
                int validCount = 0;
                foreach (VerifiedIssue issue in verResp!.issues)
                {
                    if (orderMap.TryGetValue(issue.candidate_id ?? string.Empty, out int slot))
                    {
                        resultSlots[slot] = issue;
                        if (issue.valid) validCount++;
                    }
                }

                batchSw.Stop();
                Interlocked.Increment(ref successAcc[0]);
                Interlocked.Add(ref batchDurAcc[0], batchSw.ElapsedMilliseconds);
                Interlocked.Add(ref verifiedAcc[0], validCount);

                if (turnRecorder != null && turnId.HasValue)
                {
                    try { await turnRecorder.CompleteSuccessAsync(turnId.Value, inputTok, outputTok, validCount, DateTimeOffset.UtcNow, batchSw.ElapsedMilliseconds, cancellationToken); }
                    catch (Exception rex) { logger.LogError(rex, "Failed to complete Verification turn record"); }
                }
            }
            catch (OperationCanceledException)
            {
                batchSw.Stop();
                Interlocked.Add(ref batchDurAcc[0], batchSw.ElapsedMilliseconds);
                if (turnRecorder != null && turnId.HasValue)
                {
                    try { await turnRecorder.CompleteFailureAsync(turnId.Value, null, null, "Cancellation", DateTimeOffset.UtcNow, batchSw.ElapsedMilliseconds, CancellationToken.None); }
                    catch { }
                }
                throw;
            }
            catch (Exception ex)
            {
                batchSw.Stop();
                Interlocked.Add(ref batchDurAcc[0], batchSw.ElapsedMilliseconds);
                Interlocked.Increment(ref failAcc[0]);

                if (turnRecorder != null && turnId.HasValue)
                {
                    try { await turnRecorder.CompleteFailureAsync(turnId.Value, null, null, ex.GetType().Name, DateTimeOffset.UtcNow, batchSw.ElapsedMilliseconds, CancellationToken.None); }
                    catch { }
                }

                if (!semReleased) { sem.Release(); semReleased = true; }

                if (!cancellationToken.IsCancellationRequested)
                {
                    if (batch.Items.Count > 1)
                    {
                        logger.LogWarning(ex,
                            "Splitting batch {BatchId} ({Count} candidates) for retry due to exception",
                            batch.BatchId, batch.Items.Count);
                        Interlocked.Increment(ref splitAcc[0]);
                        await SplitAndRetryAsync(batch, reviewRequest, llmCall, budget,
                            null, executionId, model, logger, cancellationToken,
                            successAcc, failAcc, retryAcc, splitAcc, singleAcc, verifiedAcc, batchDurAcc,
                            orderMap, resultSlots, sem);
                    }
                    else
                    {
                        string candidateId = batch.Items[0].Candidate.candidate_id ?? "?";
                        logger.LogWarning(ex,
                            "Single-candidate verification failed for {CandidateId}: {Reason}",
                            candidateId, ex.Message);
                        Interlocked.Increment(ref singleAcc[0]);
                    }
                }
                return;
            }
            finally
            {
                if (!semReleased) sem.Release();
            }
        }

        private static async Task SplitAndRetryAsync(
            VerificationBatch batch,
            ReviewRequest reviewRequest,
            Func<string, CancellationToken, Task<(VerifiedResponse?, int?, int?)>> llmCall,
            ReviewBudgetConfig budget,
            IReviewTurnRecorder? turnRecorder,
            long? executionId,
            string model,
            ILogger logger,
            CancellationToken cancellationToken,
            int[] successAcc,
            int[] failAcc,
            int[] retryAcc,
            int[] splitAcc,
            int[] singleAcc,
            int[] verifiedAcc,
            long[] batchDurAcc,
            Dictionary<string, int> orderMap,
            VerifiedIssue?[] resultSlots,
            SemaphoreSlim sem)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            Interlocked.Increment(ref retryAcc[0]);

            int half = batch.Items.Count / 2;
            var firstHalfItems = batch.Items.Take(half).ToList();
            var secondHalfItems = batch.Items.Skip(half).ToList();

            VerificationBatch firstBatch = new VerificationBatch
            {
                BatchId = batch.BatchId + "-r1",
                Items = firstHalfItems,
                EstimatedChars = firstHalfItems.Sum(i => i.EstimatedChars),
                IsOversized = false,
            };

            VerificationBatch secondBatch = new VerificationBatch
            {
                BatchId = batch.BatchId + "-r2",
                Items = secondHalfItems,
                EstimatedChars = secondHalfItems.Sum(i => i.EstimatedChars),
                IsOversized = false,
            };

            Task t1 = ExecuteBatchWithRetryAsync(firstBatch, reviewRequest, llmCall, budget,
                turnRecorder, executionId, model, logger, cancellationToken,
                successAcc, failAcc, retryAcc, splitAcc, singleAcc, verifiedAcc, batchDurAcc,
                orderMap, resultSlots, sem);

            Task t2 = ExecuteBatchWithRetryAsync(secondBatch, reviewRequest, llmCall, budget,
                turnRecorder, executionId, model, logger, cancellationToken,
                successAcc, failAcc, retryAcc, splitAcc, singleAcc, verifiedAcc, batchDurAcc,
                orderMap, resultSlots, sem);

            await Task.WhenAll(t1, t2);
        }

        private static string? ValidateResponse(VerifiedResponse? verResp, VerificationBatch batch)
        {
            if (verResp == null)
                return "null response";

            // Build expected set of candidate IDs
            var expectedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (VerificationBatchItem item in batch.Items)
                expectedIds.Add(item.Candidate.candidate_id ?? string.Empty);

            // Check for duplicates and unknown IDs
            var seenResultIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (VerifiedIssue issue in verResp.issues)
            {
                string id = issue.candidate_id ?? string.Empty;
                if (!expectedIds.Contains(id))
                    return $"unknown candidate_id '{id}' in response";
                if (!seenResultIds.Add(id))
                    return $"duplicate candidate_id '{id}' in response";
            }

            // Check all expected IDs are present
            foreach (string expected in expectedIds)
            {
                if (!seenResultIds.Contains(expected))
                    return $"missing candidate_id '{expected}' in response";
            }

            return null;
        }
    }
}
