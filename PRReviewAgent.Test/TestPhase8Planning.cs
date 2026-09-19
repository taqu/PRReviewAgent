using PRReviewAgent.Prompt;
using PRReviewAgent.Prompt.Turn1;
using PRReviewAgent.Services.Verification;

namespace PRReviewAgent.Test;

[TestClass]
public class TestPhase8Planning
{
    // -----------------------------------------------------------------------
    // Helper factories
    // -----------------------------------------------------------------------

    private static VerificationBatchItem MakeItem(string candidateId, int chars)
    {
        var candidate = new PRReviewAgent.Prompt.CandidateIssue { candidate_id = candidateId, location = "test.cpp:1", hypothesis = "test", trigger = "test" };
        var ctx = new PRReviewAgent.Prompt.VerificationContext
        {
            CandidateId = candidateId,
            Items = new List<PRReviewAgent.Prompt.SourceContextItem>
            {
                new PRReviewAgent.Prompt.SourceContextItem
                {
                    Kind = PRReviewAgent.Prompt.VerificationContextKind.ChangedScope,
                    Path = "test.cpp",
                    Symbol = "Foo",
                    Source = new string('x', chars)
                }
            }
        };
        return new PRReviewAgent.Services.Verification.VerificationBatchItem { Candidate = candidate, Context = ctx };
    }

    private static ReviewBudgetConfig DefaultBudget() => new ReviewBudgetConfig
    {
        MaxCandidatesPerBatch = 1,
        MaxBatchInputChars = 32_000,
        MaxConcurrentBatches = 1,
    };

    private static AdaptiveVerificationConfig DefaultAdaptiveConfig() => new AdaptiveVerificationConfig
    {
        Policy = VerificationPolicy.Adaptive,
        HardMaxCandidatesPerBatch = 8,
        HardMaxConcurrentBatches = 4,
        SmallCandidateCountThreshold = 3,
        SmallTotalCharsThreshold = 24_000,
        LargeCandidateCharsThreshold = 16_000,
        PreferredSmallBatchSize = 2,
        PreferredMediumBatchSize = 2,
        PreferredConcurrency = 2,
    };

    // -----------------------------------------------------------------------
    // 1. AdaptiveVerificationConfig_Defaults_PolicyIsFixed
    // -----------------------------------------------------------------------

    [TestMethod]
    public void AdaptiveVerificationConfig_Defaults_PolicyIsFixed()
    {
        var config = new AdaptiveVerificationConfig();
        Assert.AreEqual(VerificationPolicy.Fixed, config.Policy);
    }

    // -----------------------------------------------------------------------
    // 2. AdaptiveVerificationConfig_Defaults_HardLimitsAreConservative
    // -----------------------------------------------------------------------

    [TestMethod]
    public void AdaptiveVerificationConfig_Defaults_HardLimitsAreConservative()
    {
        var config = new AdaptiveVerificationConfig();
        Assert.AreEqual(8, config.HardMaxCandidatesPerBatch);
        Assert.AreEqual(4, config.HardMaxConcurrentBatches);
    }

    // -----------------------------------------------------------------------
    // 3. VerificationExecutionMode_HasExpectedValues
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationExecutionMode_HasExpectedValues()
    {
        var values = Enum.GetValues<VerificationExecutionMode>();
        CollectionAssert.Contains(values, VerificationExecutionMode.NoOp);
        CollectionAssert.Contains(values, VerificationExecutionMode.SequentialSingle);
        CollectionAssert.Contains(values, VerificationExecutionMode.SequentialBatch);
        CollectionAssert.Contains(values, VerificationExecutionMode.ParallelBatch);
        Assert.AreEqual(4, values.Length);
    }

    // -----------------------------------------------------------------------
    // 4. ReviewWorkloadProfileBuilder_EmptyItems_ZeroCandidates
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ReviewWorkloadProfileBuilder_EmptyItems_ZeroCandidates()
    {
        var profile = ReviewWorkloadProfileBuilder.Build(new List<VerificationBatchItem>());
        Assert.AreEqual(0, profile.CandidateCount);
        Assert.AreEqual(0, profile.EstimatedVerificationCharsTotal);
        Assert.AreEqual(0, profile.EstimatedVerificationCharsMax);
        Assert.AreEqual(0, profile.EstimatedVerificationCharsAverage);
    }

    // -----------------------------------------------------------------------
    // 5. ReviewWorkloadProfileBuilder_SingleItem_ComputesStats
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ReviewWorkloadProfileBuilder_SingleItem_ComputesStats()
    {
        var items = new List<VerificationBatchItem> { MakeItem("c0", 1000) };
        var profile = ReviewWorkloadProfileBuilder.Build(items);
        Assert.AreEqual(1, profile.CandidateCount);
        Assert.AreEqual(1000, profile.EstimatedVerificationCharsTotal);
        Assert.AreEqual(1000, profile.EstimatedVerificationCharsMax);
        Assert.AreEqual(1000, profile.EstimatedVerificationCharsAverage);
    }

    // -----------------------------------------------------------------------
    // 6. ReviewWorkloadProfileBuilder_MultipleItems_ComputesAverageAndMax
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ReviewWorkloadProfileBuilder_MultipleItems_ComputesAverageAndMax()
    {
        var items = new List<VerificationBatchItem>
        {
            MakeItem("c0", 1000),
            MakeItem("c1", 3000),
            MakeItem("c2", 2000),
        };
        var profile = ReviewWorkloadProfileBuilder.Build(items);
        Assert.AreEqual(3, profile.CandidateCount);
        Assert.AreEqual(6000, profile.EstimatedVerificationCharsTotal);
        Assert.AreEqual(3000, profile.EstimatedVerificationCharsMax);
        Assert.AreEqual(2000, profile.EstimatedVerificationCharsAverage);
    }

    // -----------------------------------------------------------------------
    // 7. ReviewWorkloadProfileBuilder_TruncatedContext_CountedCorrectly
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ReviewWorkloadProfileBuilder_TruncatedContext_CountedCorrectly()
    {
        var candidate = new CandidateIssue { candidate_id = "c0", location = "test.cpp:1", hypothesis = "h", trigger = "t" };
        var ctx = new VerificationContext
        {
            CandidateId = "c0",
            Truncated = true,
            Items = new List<SourceContextItem>
            {
                new SourceContextItem { Kind = VerificationContextKind.ChangedScope, Path = "test.cpp", Symbol = "Foo", Source = new string('x', 500) }
            }
        };
        var truncatedItem = new VerificationBatchItem { Candidate = candidate, Context = ctx };
        var items = new List<VerificationBatchItem> { MakeItem("c1", 500), truncatedItem };
        var profile = ReviewWorkloadProfileBuilder.Build(items);
        Assert.AreEqual(1, profile.TruncatedContextCount);
    }

    // -----------------------------------------------------------------------
    // 8. ReviewWorkloadProfileBuilder_LargeContext_CountedCorrectly
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ReviewWorkloadProfileBuilder_LargeContext_CountedCorrectly()
    {
        var items = new List<VerificationBatchItem>
        {
            MakeItem("c0", 500),
            MakeItem("c1", 20_000),  // exceeds default 16_000 threshold
        };
        var profile = ReviewWorkloadProfileBuilder.Build(items);
        Assert.AreEqual(1, profile.LargeCandidateCount);
    }

    // -----------------------------------------------------------------------
    // 9. VerificationExecutionPlanner_ZeroCandidates_ReturnsNoOp
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationExecutionPlanner_ZeroCandidates_ReturnsNoOp()
    {
        var profile = ReviewWorkloadProfileBuilder.Build(new List<VerificationBatchItem>());
        var plan = VerificationExecutionPlanner.Plan(profile, DefaultAdaptiveConfig(), DefaultBudget());
        Assert.AreEqual(VerificationExecutionMode.NoOp, plan.Mode);
    }

    // -----------------------------------------------------------------------
    // 10. VerificationExecutionPlanner_OneCandidate_ReturnsSequentialSingle
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationExecutionPlanner_OneCandidate_ReturnsSequentialSingle()
    {
        var items = new List<VerificationBatchItem> { MakeItem("c0", 1000) };
        var profile = ReviewWorkloadProfileBuilder.Build(items);
        var plan = VerificationExecutionPlanner.Plan(profile, DefaultAdaptiveConfig(), DefaultBudget());
        Assert.AreEqual(VerificationExecutionMode.SequentialSingle, plan.Mode);
        Assert.AreEqual(1, plan.MaxCandidatesPerBatch);
        Assert.AreEqual(1, plan.MaxConcurrentBatches);
    }

    // -----------------------------------------------------------------------
    // 11. VerificationExecutionPlanner_FewSmallCandidates_ReturnsSequentialBatch
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationExecutionPlanner_FewSmallCandidates_ReturnsSequentialBatch()
    {
        // CandidateCount=3, total=12000, no large contexts
        var profile = new ReviewWorkloadProfile
        {
            GroupCount = 1,
            CandidateCount = 3,
            EstimatedVerificationCharsTotal = 12_000,
            EstimatedVerificationCharsMax = 4_000,
            EstimatedVerificationCharsAverage = 4_000,
            LargeCandidateCount = 0,
            TruncatedContextCount = 0,
        };
        var plan = VerificationExecutionPlanner.Plan(profile, DefaultAdaptiveConfig(), DefaultBudget());
        Assert.AreEqual(VerificationExecutionMode.SequentialBatch, plan.Mode);
        Assert.AreEqual(1, plan.MaxConcurrentBatches);
    }

    // -----------------------------------------------------------------------
    // 12. VerificationExecutionPlanner_ModerateCandidates_ReturnsParallelBatch
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationExecutionPlanner_ModerateCandidates_ReturnsParallelBatch()
    {
        // CandidateCount=5, total=40000, no large contexts
        var profile = new ReviewWorkloadProfile
        {
            GroupCount = 1,
            CandidateCount = 5,
            EstimatedVerificationCharsTotal = 40_000,
            EstimatedVerificationCharsMax = 8_000,
            EstimatedVerificationCharsAverage = 8_000,
            LargeCandidateCount = 0,
            TruncatedContextCount = 0,
        };
        var plan = VerificationExecutionPlanner.Plan(profile, DefaultAdaptiveConfig(), DefaultBudget());
        Assert.AreEqual(VerificationExecutionMode.ParallelBatch, plan.Mode);
    }

    // -----------------------------------------------------------------------
    // 13. VerificationExecutionPlanner_LargeContextCandidate_ReturnsConservative
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationExecutionPlanner_LargeContextCandidate_ReturnsConservative()
    {
        // Any candidate > LargeCandidateCharsThreshold (16000) → conservative
        var items = new List<VerificationBatchItem>
        {
            MakeItem("c0", 500),
            MakeItem("c1", 20_000),
        };
        var profile = ReviewWorkloadProfileBuilder.Build(items);
        var plan = VerificationExecutionPlanner.Plan(profile, DefaultAdaptiveConfig(), DefaultBudget());
        Assert.AreEqual(VerificationExecutionMode.SequentialSingle, plan.Mode);
        Assert.AreEqual(1, plan.MaxCandidatesPerBatch);
        Assert.AreEqual(1, plan.MaxConcurrentBatches);
    }

    // -----------------------------------------------------------------------
    // 14. VerificationExecutionPlanner_HardLimits_ClampBatchSize
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationExecutionPlanner_HardLimits_ClampBatchSize()
    {
        // HardMaxCandidatesPerBatch=2, PreferredMediumBatchSize=4 → clamped to 2
        var config = new AdaptiveVerificationConfig
        {
            Policy = VerificationPolicy.Adaptive,
            HardMaxCandidatesPerBatch = 2,
            HardMaxConcurrentBatches = 4,
            SmallCandidateCountThreshold = 3,
            SmallTotalCharsThreshold = 24_000,
            LargeCandidateCharsThreshold = 16_000,
            PreferredSmallBatchSize = 2,
            PreferredMediumBatchSize = 4,
            PreferredConcurrency = 2,
        };
        // Use moderate profile to get ParallelBatch
        var profile = new ReviewWorkloadProfile
        {
            GroupCount = 1,
            CandidateCount = 5,
            EstimatedVerificationCharsTotal = 40_000,
            EstimatedVerificationCharsMax = 8_000,
            EstimatedVerificationCharsAverage = 8_000,
            LargeCandidateCount = 0,
            TruncatedContextCount = 0,
        };
        var plan = VerificationExecutionPlanner.Plan(profile, config, DefaultBudget());
        Assert.AreEqual(2, plan.MaxCandidatesPerBatch);
    }

    // -----------------------------------------------------------------------
    // 15. VerificationExecutionPlanner_HardLimits_ClampConcurrency
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationExecutionPlanner_HardLimits_ClampConcurrency()
    {
        // HardMaxConcurrentBatches=1, PreferredConcurrency=4 → clamped to 1
        var config = new AdaptiveVerificationConfig
        {
            Policy = VerificationPolicy.Adaptive,
            HardMaxCandidatesPerBatch = 8,
            HardMaxConcurrentBatches = 1,
            SmallCandidateCountThreshold = 3,
            SmallTotalCharsThreshold = 24_000,
            LargeCandidateCharsThreshold = 16_000,
            PreferredSmallBatchSize = 2,
            PreferredMediumBatchSize = 2,
            PreferredConcurrency = 4,
        };
        var profile = new ReviewWorkloadProfile
        {
            GroupCount = 1,
            CandidateCount = 5,
            EstimatedVerificationCharsTotal = 40_000,
            EstimatedVerificationCharsMax = 8_000,
            EstimatedVerificationCharsAverage = 8_000,
            LargeCandidateCount = 0,
            TruncatedContextCount = 0,
        };
        var plan = VerificationExecutionPlanner.Plan(profile, config, DefaultBudget());
        Assert.AreEqual(1, plan.MaxConcurrentBatches);
    }

    // -----------------------------------------------------------------------
    // 16. VerificationExecutionPlanner_Deterministic_SameInputSameOutput
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationExecutionPlanner_Deterministic_SameInputSameOutput()
    {
        var profile = new ReviewWorkloadProfile
        {
            GroupCount = 1,
            CandidateCount = 5,
            EstimatedVerificationCharsTotal = 40_000,
            EstimatedVerificationCharsMax = 8_000,
            EstimatedVerificationCharsAverage = 8_000,
            LargeCandidateCount = 0,
            TruncatedContextCount = 0,
        };
        var config = DefaultAdaptiveConfig();
        var budget = DefaultBudget();
        var plan1 = VerificationExecutionPlanner.Plan(profile, config, budget);
        var plan2 = VerificationExecutionPlanner.Plan(profile, config, budget);
        Assert.AreEqual(plan1.Mode, plan2.Mode);
        Assert.AreEqual(plan1.MaxCandidatesPerBatch, plan2.MaxCandidatesPerBatch);
        Assert.AreEqual(plan1.MaxConcurrentBatches, plan2.MaxConcurrentBatches);
    }

    // -----------------------------------------------------------------------
    // 17. VerificationExecutionPlanner_PlanHasNonEmptyReason
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationExecutionPlanner_PlanHasNonEmptyReason()
    {
        var profile = ReviewWorkloadProfileBuilder.Build(new List<VerificationBatchItem> { MakeItem("c0", 500) });
        var plan = VerificationExecutionPlanner.Plan(profile, DefaultAdaptiveConfig(), DefaultBudget());
        Assert.IsFalse(string.IsNullOrWhiteSpace(plan.Reason));
    }

    // -----------------------------------------------------------------------
    // 18. VerificationBatchBuilder_WithExplicitBatchSize_BuildsCorrectly
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationBatchBuilder_WithExplicitBatchSize_BuildsCorrectly()
    {
        // 5 items, maxCandidates=2 → 3 batches (2, 2, 1)
        var items = new List<VerificationBatchItem>
        {
            MakeItem("c0", 100),
            MakeItem("c1", 100),
            MakeItem("c2", 100),
            MakeItem("c3", 100),
            MakeItem("c4", 100),
        };
        var batches = VerificationBatchBuilder.BuildFromResolved(items, maxCandidatesPerBatch: 2, maxBatchInputChars: 32_000, groupId: "test-group");
        Assert.AreEqual(3, batches.Count);
        Assert.AreEqual(2, batches[0].Items.Count);
        Assert.AreEqual(2, batches[1].Items.Count);
        Assert.AreEqual(1, batches[2].Items.Count);
    }
}
