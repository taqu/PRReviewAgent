using PRReviewAgent.Prompt;
using PRReviewAgent.Prompt.Turn1;
using PRReviewAgent.Services;
using PRReviewAgent.Services.Verification;
using System.Text;

namespace PRReviewAgent.Test;

[TestClass]
public class TestPhase7Batching
{
    // -----------------------------------------------------------------------
    // Helper factories
    // -----------------------------------------------------------------------

    private static CandidateIssue MakeCandidate(string id, string location = "file.cpp: Foo::Bar") =>
        new CandidateIssue
        {
            candidate_id = id,
            location = location,
            hypothesis = "test hypothesis",
            trigger = "test trigger",
            category = "correctness",
            verify_symbols = Array.Empty<string>(),
        };

    private static VerificationBatchItem MakeBatchItem(string candidateId, int sourceLength = 100)
    {
        string source = new string('x', sourceLength);
        CandidateIssue candidate = MakeCandidate(candidateId);
        VerificationContext ctx = new VerificationContext
        {
            CandidateId = candidateId,
            Items = new List<SourceContextItem>
            {
                new SourceContextItem
                {
                    Path = "file.cpp",
                    Symbol = "Foo::Bar",
                    Kind = VerificationContextKind.ChangedScope,
                    Source = source,
                }
            },
        };
        return new VerificationBatchItem { Candidate = candidate, Context = ctx };
    }

    private static ReviewRequest MakeReviewRequest() =>
        new ReviewRequest
        {
            ReviewRulesTurn1 = "Turn1 rules",
            ReviewRulesTurn2 = "Turn2 rules",
            ReviewRulesTurn3 = "Turn3 rules",
        };

    // -----------------------------------------------------------------------
    // 1. ReviewBudgetConfig_BatchDefaults
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ReviewBudgetConfig_BatchDefaults()
    {
        ReviewBudgetConfig cfg = new ReviewBudgetConfig();
        Assert.AreEqual(1, cfg.MaxCandidatesPerBatch);
        Assert.AreEqual(32_000, cfg.MaxBatchInputChars);
        Assert.AreEqual(1, cfg.MaxConcurrentBatches);
    }

    // -----------------------------------------------------------------------
    // 2. VerificationBatchBuilder_EmptyInput_ReturnsNoBatches
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationBatchBuilder_EmptyInput_ReturnsNoBatches()
    {
        var budget = new ReviewBudgetConfig { MaxCandidatesPerBatch = 2 };
        IReadOnlyList<VerificationBatch> batches = VerificationBatchBuilder.BuildFromResolved(
            new List<VerificationBatchItem>(), budget, "g0");
        Assert.AreEqual(0, batches.Count);
    }

    // -----------------------------------------------------------------------
    // 3. VerificationBatchBuilder_SingleItem_OneBatch
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationBatchBuilder_SingleItem_OneBatch()
    {
        var budget = new ReviewBudgetConfig { MaxCandidatesPerBatch = 2, MaxBatchInputChars = 32_000 };
        var items = new List<VerificationBatchItem> { MakeBatchItem("c0") };

        IReadOnlyList<VerificationBatch> batches = VerificationBatchBuilder.BuildFromResolved(items, budget, "g0");

        Assert.AreEqual(1, batches.Count);
        Assert.AreEqual(1, batches[0].Items.Count);
        Assert.AreEqual("c0", batches[0].Items[0].Candidate.candidate_id);
        Assert.IsFalse(batches[0].IsOversized);
    }

    // -----------------------------------------------------------------------
    // 4. VerificationBatchBuilder_TwoItems_SameBatch_WhenLimitIsTwo
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationBatchBuilder_TwoItems_SameBatch_WhenLimitIsTwo()
    {
        var budget = new ReviewBudgetConfig { MaxCandidatesPerBatch = 2, MaxBatchInputChars = 32_000 };
        var items = new List<VerificationBatchItem>
        {
            MakeBatchItem("c0"),
            MakeBatchItem("c1"),
        };

        IReadOnlyList<VerificationBatch> batches = VerificationBatchBuilder.BuildFromResolved(items, budget, "g0");

        Assert.AreEqual(1, batches.Count);
        Assert.AreEqual(2, batches[0].Items.Count);
    }

    // -----------------------------------------------------------------------
    // 5. VerificationBatchBuilder_TwoItems_SeparateBatches_WhenLimitIsOne
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationBatchBuilder_TwoItems_SeparateBatches_WhenLimitIsOne()
    {
        var budget = new ReviewBudgetConfig { MaxCandidatesPerBatch = 1, MaxBatchInputChars = 32_000 };
        var items = new List<VerificationBatchItem>
        {
            MakeBatchItem("c0"),
            MakeBatchItem("c1"),
        };

        IReadOnlyList<VerificationBatch> batches = VerificationBatchBuilder.BuildFromResolved(items, budget, "g0");

        Assert.AreEqual(2, batches.Count);
        Assert.AreEqual("c0", batches[0].Items[0].Candidate.candidate_id);
        Assert.AreEqual("c1", batches[1].Items[0].Candidate.candidate_id);
    }

    // -----------------------------------------------------------------------
    // 6. VerificationBatchBuilder_CharBudget_SplitsCorrectly
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationBatchBuilder_CharBudget_SplitsCorrectly()
    {
        // Char limit = 150, items: 100 chars + 100 chars => second doesn't fit
        var budget = new ReviewBudgetConfig { MaxCandidatesPerBatch = 10, MaxBatchInputChars = 150 };
        var items = new List<VerificationBatchItem>
        {
            MakeBatchItem("c0", sourceLength: 100),
            MakeBatchItem("c1", sourceLength: 100),
        };

        IReadOnlyList<VerificationBatch> batches = VerificationBatchBuilder.BuildFromResolved(items, budget, "g0");

        Assert.AreEqual(2, batches.Count);
        Assert.AreEqual("c0", batches[0].Items[0].Candidate.candidate_id);
        Assert.AreEqual("c1", batches[1].Items[0].Candidate.candidate_id);
    }

    // -----------------------------------------------------------------------
    // 7. VerificationBatchBuilder_NoContextCandidates_AreSkipped
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationBatchBuilder_NoContextCandidates_AreSkipped()
    {
        // Item with empty Items list should be treated as no-context by BuildFromResolved
        // (BuildFromResolved itself doesn't skip — Build() does. Test BuildFromResolved
        //  with zero-item context to confirm empty-source items still get packed.)
        // Instead, we test Build() with empty ReviewContexts — which causes resolver to return empty Items.
        var budget = new ReviewBudgetConfig { MaxCandidatesPerBatch = 2 };
        var candidates = new List<CandidateIssue>
        {
            MakeCandidate("c0"),
        };
        // Empty ReviewContexts means resolver can't find AST → empty Items → skipped
        IReadOnlyList<VerificationBatch> batches = VerificationBatchBuilder.Build(
            candidates,
            new List<ReviewContext>(),
            budget,
            "g0");

        Assert.AreEqual(0, batches.Count);
    }

    // -----------------------------------------------------------------------
    // 8. VerificationBatchBuilder_OversizedSingle_CreatedWithFlag
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationBatchBuilder_OversizedSingle_CreatedWithFlag()
    {
        // Item that exceeds char limit even alone
        var budget = new ReviewBudgetConfig { MaxCandidatesPerBatch = 10, MaxBatchInputChars = 50 };
        var items = new List<VerificationBatchItem>
        {
            MakeBatchItem("c0", sourceLength: 100),
        };

        IReadOnlyList<VerificationBatch> batches = VerificationBatchBuilder.BuildFromResolved(items, budget, "g0");

        Assert.AreEqual(1, batches.Count);
        Assert.IsTrue(batches[0].IsOversized);
        Assert.AreEqual(1, batches[0].Items.Count);
    }

    // -----------------------------------------------------------------------
    // 9. VerificationExecutorMetrics_HasExpectedDefaults
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationExecutorMetrics_HasExpectedDefaults()
    {
        var m = new VerificationExecutorMetrics();
        Assert.AreEqual(0, m.BatchCount);
        Assert.AreEqual(0, m.BatchSuccessCount);
        Assert.AreEqual(0, m.BatchFailureCount);
        Assert.AreEqual(0, m.BatchRetryCount);
        Assert.AreEqual(0, m.BatchSplitCount);
        Assert.AreEqual(0, m.SingleCandidateFallbackCount);
        Assert.AreEqual(0, m.TotalCandidates);
        Assert.AreEqual(0, m.VerifiedCount);
        Assert.AreEqual(0, m.WallClockMs);
        Assert.AreEqual(0, m.TotalBatchDurationMs);
        Assert.AreEqual(0, m.ConfiguredMaxConcurrency);
    }

    // -----------------------------------------------------------------------
    // 10. VerificationBatch_BatchId_SetCorrectly
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationBatch_BatchId_SetCorrectly()
    {
        var budget = new ReviewBudgetConfig { MaxCandidatesPerBatch = 1 };
        var items = new List<VerificationBatchItem>
        {
            MakeBatchItem("c0"),
            MakeBatchItem("c1"),
        };

        IReadOnlyList<VerificationBatch> batches = VerificationBatchBuilder.BuildFromResolved(items, budget, "mygroup");

        Assert.AreEqual("mygroup-batch-0", batches[0].BatchId);
        Assert.AreEqual("mygroup-batch-1", batches[1].BatchId);
    }

    // -----------------------------------------------------------------------
    // 11. PromptBuilder_BuildTurn2Batch_SingleItem_ContainsCandidateId
    // -----------------------------------------------------------------------

    [TestMethod]
    public void PromptBuilder_BuildTurn2Batch_SingleItem_ContainsCandidateId()
    {
        var item = MakeBatchItem("c0");
        var batch = new VerificationBatch
        {
            BatchId = "g0-batch-0",
            Items = new List<VerificationBatchItem> { item },
            EstimatedChars = item.EstimatedChars,
        };

        string prompt = PromptBuilder.BuildTurn2Batch(MakeReviewRequest(), batch, new StringBuilder());

        Assert.IsTrue(prompt.Contains("c0"), "Prompt should contain the candidate_id 'c0'");
    }

    // -----------------------------------------------------------------------
    // 12. PromptBuilder_BuildTurn2Batch_MultipleItems_ContainsAllCandidateIds
    // -----------------------------------------------------------------------

    [TestMethod]
    public void PromptBuilder_BuildTurn2Batch_MultipleItems_ContainsAllCandidateIds()
    {
        var item0 = MakeBatchItem("c0");
        var item1 = MakeBatchItem("c1");
        var batch = new VerificationBatch
        {
            BatchId = "g0-batch-0",
            Items = new List<VerificationBatchItem> { item0, item1 },
            EstimatedChars = item0.EstimatedChars + item1.EstimatedChars,
        };

        string prompt = PromptBuilder.BuildTurn2Batch(MakeReviewRequest(), batch, new StringBuilder());

        Assert.IsTrue(prompt.Contains("c0"), "Prompt should contain candidate_id 'c0'");
        Assert.IsTrue(prompt.Contains("c1"), "Prompt should contain candidate_id 'c1'");
    }

    // -----------------------------------------------------------------------
    // 13. PromptBuilder_BuildTurn2Batch_MultipleItems_ContainsSeparator
    // -----------------------------------------------------------------------

    [TestMethod]
    public void PromptBuilder_BuildTurn2Batch_MultipleItems_ContainsSeparator()
    {
        var item0 = MakeBatchItem("c0");
        var item1 = MakeBatchItem("c1");
        var batch = new VerificationBatch
        {
            BatchId = "g0-batch-0",
            Items = new List<VerificationBatchItem> { item0, item1 },
            EstimatedChars = item0.EstimatedChars + item1.EstimatedChars,
        };

        string prompt = PromptBuilder.BuildTurn2Batch(MakeReviewRequest(), batch, new StringBuilder());

        Assert.IsTrue(prompt.Contains("================================"), "Prompt should contain separator");
    }

    // -----------------------------------------------------------------------
    // 14. VerificationExecutor_EmptyBatches_ReturnsEmpty
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task VerificationExecutor_EmptyBatches_ReturnsEmpty()
    {
        var budget = new ReviewBudgetConfig();
        int callCount = 0;

        Func<string, CancellationToken, Task<(VerifiedResponse?, int?, int?)>> llm =
            (prompt, ct) => { callCount++; return Task.FromResult<(VerifiedResponse?, int?, int?)>((null, null, null)); };

        var (results, metrics) = await VerificationExecutor.ExecuteAsync(
            MakeReviewRequest(),
            new List<VerificationBatch>(),
            llm,
            budget,
            null,
            null,
            "test-model",
            MakeNullLogger(),
            CancellationToken.None);

        Assert.AreEqual(0, results.Count);
        Assert.AreEqual(0, metrics.BatchCount);
        Assert.AreEqual(0, callCount);
    }

    // -----------------------------------------------------------------------
    // 15. VerificationExecutor_SingleBatch_Success_ReturnsVerifiedIssues
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task VerificationExecutor_SingleBatch_Success_ReturnsVerifiedIssues()
    {
        var item = MakeBatchItem("c0");
        var batch = new VerificationBatch
        {
            BatchId = "g0-batch-0",
            Items = new List<VerificationBatchItem> { item },
            EstimatedChars = item.EstimatedChars,
        };

        var budget = new ReviewBudgetConfig();

        Func<string, CancellationToken, Task<(VerifiedResponse?, int?, int?)>> llm =
            (prompt, ct) =>
            {
                var resp = new VerifiedResponse
                {
                    issues = new[]
                    {
                        new VerifiedIssue
                        {
                            candidate_id = "c0",
                            valid = true,
                            evidence = "test evidence",
                            impact = "test impact",
                            suggested_fix = "test fix",
                            confidence = "high",
                        }
                    }
                };
                return Task.FromResult<(VerifiedResponse?, int?, int?)>((resp, 100, 50));
            };

        var (results, metrics) = await VerificationExecutor.ExecuteAsync(
            MakeReviewRequest(),
            new List<VerificationBatch> { batch },
            llm,
            budget,
            null,
            null,
            "test-model",
            MakeNullLogger(),
            CancellationToken.None);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("c0", results[0].candidate_id);
        Assert.IsTrue(results[0].valid);
        Assert.AreEqual(1, metrics.BatchSuccessCount);
        Assert.AreEqual(0, metrics.BatchFailureCount);
    }

    // -----------------------------------------------------------------------
    // 16. VerificationExecutor_SingleBatch_LlmReturnsNull_ReturnsEmpty
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task VerificationExecutor_SingleBatch_LlmReturnsNull_ReturnsEmpty()
    {
        var item = MakeBatchItem("c0");
        var batch = new VerificationBatch
        {
            BatchId = "g0-batch-0",
            Items = new List<VerificationBatchItem> { item },
            EstimatedChars = item.EstimatedChars,
        };

        var budget = new ReviewBudgetConfig();

        Func<string, CancellationToken, Task<(VerifiedResponse?, int?, int?)>> llm =
            (prompt, ct) => Task.FromResult<(VerifiedResponse?, int?, int?)>((null, null, null));

        var (results, metrics) = await VerificationExecutor.ExecuteAsync(
            MakeReviewRequest(),
            new List<VerificationBatch> { batch },
            llm,
            budget,
            null,
            null,
            "test-model",
            MakeNullLogger(),
            CancellationToken.None);

        Assert.AreEqual(0, results.Count);
        Assert.AreEqual(1, metrics.SingleCandidateFallbackCount);
    }

    // -----------------------------------------------------------------------
    // 17. VerificationExecutor_Batch_MissingCandidate_RetriesHalves
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task VerificationExecutor_Batch_MissingCandidate_RetriesHalves()
    {
        // Batch with 2 candidates; first call returns only one (missing c1) => split & retry
        var item0 = MakeBatchItem("c0");
        var item1 = MakeBatchItem("c1");
        var batch = new VerificationBatch
        {
            BatchId = "g0-batch-0",
            Items = new List<VerificationBatchItem> { item0, item1 },
            EstimatedChars = item0.EstimatedChars + item1.EstimatedChars,
        };

        var budget = new ReviewBudgetConfig { MaxConcurrentBatches = 2 };
        int callCount = 0;

        Func<string, CancellationToken, Task<(VerifiedResponse?, int?, int?)>> llm =
            (prompt, ct) =>
            {
                callCount++;
                // Determine which candidate to return based on which is in prompt
                bool hasC0 = prompt.Contains("c0");
                bool hasC1 = prompt.Contains("c1");

                if (hasC0 && hasC1)
                {
                    // First call — return only c0 (missing c1) to force split
                    var resp = new VerifiedResponse
                    {
                        issues = new[]
                        {
                            new VerifiedIssue { candidate_id = "c0", valid = true, evidence = "e", impact = "i", suggested_fix = "f", confidence = "high" }
                        }
                    };
                    return Task.FromResult<(VerifiedResponse?, int?, int?)>((resp, 10, 5));
                }
                else if (hasC0)
                {
                    var resp = new VerifiedResponse
                    {
                        issues = new[]
                        {
                            new VerifiedIssue { candidate_id = "c0", valid = true, evidence = "e", impact = "i", suggested_fix = "f", confidence = "high" }
                        }
                    };
                    return Task.FromResult<(VerifiedResponse?, int?, int?)>((resp, 10, 5));
                }
                else
                {
                    var resp = new VerifiedResponse
                    {
                        issues = new[]
                        {
                            new VerifiedIssue { candidate_id = "c1", valid = true, evidence = "e", impact = "i", suggested_fix = "f", confidence = "high" }
                        }
                    };
                    return Task.FromResult<(VerifiedResponse?, int?, int?)>((resp, 10, 5));
                }
            };

        var (results, metrics) = await VerificationExecutor.ExecuteAsync(
            MakeReviewRequest(),
            new List<VerificationBatch> { batch },
            llm,
            budget,
            null,
            null,
            "test-model",
            MakeNullLogger(),
            CancellationToken.None);

        // After split retry, both c0 and c1 should be verified
        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results.Any(r => r.candidate_id == "c0"));
        Assert.IsTrue(results.Any(r => r.candidate_id == "c1"));
        Assert.IsTrue(callCount >= 3, $"Expected at least 3 calls (1 batch + 2 halves) but got {callCount}");
    }

    // -----------------------------------------------------------------------
    // 18. VerificationExecutor_Batch_DuplicateCandidate_TreatsAsFailed
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task VerificationExecutor_Batch_DuplicateCandidate_TreatsAsFailed()
    {
        var item0 = MakeBatchItem("c0");
        var item1 = MakeBatchItem("c1");
        var batch = new VerificationBatch
        {
            BatchId = "g0-batch-0",
            Items = new List<VerificationBatchItem> { item0, item1 },
            EstimatedChars = item0.EstimatedChars + item1.EstimatedChars,
        };

        var budget = new ReviewBudgetConfig { MaxConcurrentBatches = 2 };
        int callCount = 0;

        Func<string, CancellationToken, Task<(VerifiedResponse?, int?, int?)>> llm =
            (prompt, ct) =>
            {
                callCount++;
                bool hasC0 = prompt.Contains("[CANDIDATE c0]") || (prompt.Contains("c0") && !prompt.Contains("c1"));
                bool hasC1 = prompt.Contains("[CANDIDATE c1]") || (prompt.Contains("c1") && !prompt.Contains("c0"));

                if (prompt.Contains("c0") && prompt.Contains("c1") && prompt.Contains("independent candidates"))
                {
                    // Batch call: return duplicate c0 to trigger failure + split
                    var resp = new VerifiedResponse
                    {
                        issues = new[]
                        {
                            new VerifiedIssue { candidate_id = "c0", valid = true, evidence = "e", impact = "i", suggested_fix = "f", confidence = "high" },
                            new VerifiedIssue { candidate_id = "c0", valid = false, evidence = "dup", impact = "", suggested_fix = "", confidence = "high" },
                        }
                    };
                    return Task.FromResult<(VerifiedResponse?, int?, int?)>((resp, 10, 5));
                }

                // Single item retries
                string id = prompt.Contains("c0") ? "c0" : "c1";
                var singleResp = new VerifiedResponse
                {
                    issues = new[]
                    {
                        new VerifiedIssue { candidate_id = id, valid = true, evidence = "e", impact = "i", suggested_fix = "f", confidence = "high" }
                    }
                };
                return Task.FromResult<(VerifiedResponse?, int?, int?)>((singleResp, 10, 5));
            };

        var (results, metrics) = await VerificationExecutor.ExecuteAsync(
            MakeReviewRequest(),
            new List<VerificationBatch> { batch },
            llm,
            budget,
            null,
            null,
            "test-model",
            MakeNullLogger(),
            CancellationToken.None);

        // Original batch failed due to duplicates, but splits should succeed
        Assert.IsTrue(metrics.BatchFailureCount >= 1, "Should have at least one batch failure");
        Assert.IsTrue(metrics.BatchSplitCount >= 1, "Should have at least one split");
    }

    // -----------------------------------------------------------------------
    // 19. VerificationExecutor_Batch_UnknownCandidate_IsIgnored
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task VerificationExecutor_Batch_UnknownCandidate_IsIgnored()
    {
        var item = MakeBatchItem("c0");
        var batch = new VerificationBatch
        {
            BatchId = "g0-batch-0",
            Items = new List<VerificationBatchItem> { item },
            EstimatedChars = item.EstimatedChars,
        };

        var budget = new ReviewBudgetConfig();

        Func<string, CancellationToken, Task<(VerifiedResponse?, int?, int?)>> llm =
            (prompt, ct) =>
            {
                // Return an unknown candidate_id
                var resp = new VerifiedResponse
                {
                    issues = new[]
                    {
                        new VerifiedIssue { candidate_id = "unknown_xyz", valid = true, evidence = "e", impact = "i", suggested_fix = "f", confidence = "high" }
                    }
                };
                return Task.FromResult<(VerifiedResponse?, int?, int?)>((resp, 10, 5));
            };

        var (results, metrics) = await VerificationExecutor.ExecuteAsync(
            MakeReviewRequest(),
            new List<VerificationBatch> { batch },
            llm,
            budget,
            null,
            null,
            "test-model",
            MakeNullLogger(),
            CancellationToken.None);

        // Single-item batch with unknown_id in response → validation fails → single fallback
        Assert.AreEqual(0, results.Count);
        Assert.AreEqual(1, metrics.SingleCandidateFallbackCount);
    }

    // -----------------------------------------------------------------------
    // 20. VerificationExecutor_ResultsInOriginalOrder
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task VerificationExecutor_ResultsInOriginalOrder()
    {
        var item0 = MakeBatchItem("c0");
        var item1 = MakeBatchItem("c1");
        var item2 = MakeBatchItem("c2");

        // Each in its own batch (default limit=1)
        var batches = new List<VerificationBatch>
        {
            new VerificationBatch { BatchId = "g-batch-0", Items = new List<VerificationBatchItem> { item0 }, EstimatedChars = item0.EstimatedChars },
            new VerificationBatch { BatchId = "g-batch-1", Items = new List<VerificationBatchItem> { item1 }, EstimatedChars = item1.EstimatedChars },
            new VerificationBatch { BatchId = "g-batch-2", Items = new List<VerificationBatchItem> { item2 }, EstimatedChars = item2.EstimatedChars },
        };

        var budget = new ReviewBudgetConfig { MaxConcurrentBatches = 3 };

        Func<string, CancellationToken, Task<(VerifiedResponse?, int?, int?)>> llm =
            (prompt, ct) =>
            {
                // Identify which candidate based on prompt content
                string id = "c0";
                if (prompt.Contains("c2")) id = "c2";
                else if (prompt.Contains("c1")) id = "c1";

                var resp = new VerifiedResponse
                {
                    issues = new[]
                    {
                        new VerifiedIssue { candidate_id = id, valid = true, evidence = "e", impact = "i", suggested_fix = "f", confidence = "high" }
                    }
                };
                return Task.FromResult<(VerifiedResponse?, int?, int?)>((resp, 10, 5));
            };

        var (results, metrics) = await VerificationExecutor.ExecuteAsync(
            MakeReviewRequest(),
            batches,
            llm,
            budget,
            null,
            null,
            "test-model",
            MakeNullLogger(),
            CancellationToken.None);

        Assert.AreEqual(3, results.Count);
        Assert.AreEqual("c0", results[0].candidate_id);
        Assert.AreEqual("c1", results[1].candidate_id);
        Assert.AreEqual("c2", results[2].candidate_id);
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    private static Microsoft.Extensions.Logging.ILogger MakeNullLogger() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
