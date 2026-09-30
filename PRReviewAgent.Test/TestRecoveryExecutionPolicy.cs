using PRReviewAgent.Services.Coverage;
using PRReviewAgent.Services.Recovery;

namespace PRReviewAgent.Test;

[TestClass]
public class TestRecoveryExecutionPolicy
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ReviewCoverage MakeCoverage(int totalRegions, int reportedCount)
    {
        var regions = Enumerable.Range(0, totalRegions)
            .Select(i => new ChangedRegion($"r{i}", "file.cpp", i * 10 + 1, i * 10 + 5, new[] { i * 10 + 1 }, null))
            .ToList();
        var mappings = regions.Take(reportedCount)
            .Select((r, i) => new CandidateMapping($"c{i}", $"file.cpp:{r.StartLine}", new[] { r.RegionId }.ToList()))
            .ToList();
        return new ReviewCoverage(regions, mappings);
    }

    private static RecoveryContextResult MakeContext(int targetRegions) =>
        targetRegions > 0
            ? new RecoveryContextResult(
                new[] { new RecoveryFragment("file.cpp", null, 1, 5, new[] { "r0" }.ToList(), "+int x = 1;", 10) },
                10, targetRegions, 0)
            : RecoveryContextResult.Empty;

    private static RecoveryConfig Auto(int minRegions = 2) =>
        new RecoveryConfig { Mode = RecoveryMode.Auto, MinimumChangedRegionCount = minRegions };

    private static RecoveryConfig Always() =>
        new RecoveryConfig { Mode = RecoveryMode.Always };

    private static RecoveryConfig Never() =>
        new RecoveryConfig { Mode = RecoveryMode.Never };

    // -------------------------------------------------------------------------
    // Case 1 — No remaining regions → skip
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case1_NoRemainingRegions_AutoMode_Skips()
    {
        var coverage = MakeCoverage(totalRegions: 3, reportedCount: 3);
        var decision = RecoveryExecutionPolicy.Evaluate(Auto(), coverage, null);

        Assert.IsFalse(decision.ShouldRun);
        Assert.AreEqual(RecoverySkipReason.NoRemainingRegions, decision.SkipReason);
        Assert.AreEqual("no_remaining_regions", decision.Reason);
    }

    [TestMethod]
    public void Case1_NoRemainingRegions_AlwaysMode_StillSkips()
    {
        var coverage = MakeCoverage(totalRegions: 2, reportedCount: 2);
        var decision = RecoveryExecutionPolicy.Evaluate(Always(), coverage, null);

        Assert.IsFalse(decision.ShouldRun);
        Assert.AreEqual(RecoverySkipReason.NoRemainingRegions, decision.SkipReason);
    }

    // -------------------------------------------------------------------------
    // Case 2 — Remaining work above threshold → run
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case2_AboveThreshold_ValidContext_Runs()
    {
        var coverage = MakeCoverage(totalRegions: 5, reportedCount: 2); // 3 unreported > min 2
        var context = MakeContext(targetRegions: 3);
        var decision = RecoveryExecutionPolicy.Evaluate(Auto(minRegions: 2), coverage, context);

        Assert.IsTrue(decision.ShouldRun);
        Assert.AreEqual("eligible", decision.Reason);
    }

    [TestMethod]
    public void Case2_ExactlyAtThreshold_Runs()
    {
        var coverage = MakeCoverage(totalRegions: 5, reportedCount: 3); // 2 unreported == min 2
        var context = MakeContext(targetRegions: 2);
        var decision = RecoveryExecutionPolicy.Evaluate(Auto(minRegions: 2), coverage, context);

        Assert.IsTrue(decision.ShouldRun);
    }

    // -------------------------------------------------------------------------
    // Case 3 — Remaining work below threshold → skip
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case3_BelowThreshold_Skips()
    {
        var coverage = MakeCoverage(totalRegions: 5, reportedCount: 4); // 1 unreported < min 2
        var context = MakeContext(targetRegions: 1);
        var decision = RecoveryExecutionPolicy.Evaluate(Auto(minRegions: 2), coverage, context);

        Assert.IsFalse(decision.ShouldRun);
        Assert.AreEqual(RecoverySkipReason.BelowMinimumRemainingWork, decision.SkipReason);
        Assert.AreEqual("below_minimum_remaining_work", decision.Reason);
    }

    [TestMethod]
    public void Case3_ZeroMinimum_NeverSkipsDueToThreshold()
    {
        var coverage = MakeCoverage(totalRegions: 5, reportedCount: 4); // 1 unreported, min = 0
        var context = MakeContext(targetRegions: 1);
        var decision = RecoveryExecutionPolicy.Evaluate(Auto(minRegions: 0), coverage, context);

        Assert.IsTrue(decision.ShouldRun, "zero minimum should disable the threshold check");
    }

    // -------------------------------------------------------------------------
    // Case 4 — Force Always → bypasses minimum-work threshold
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case4_AlwaysMode_BypassesMinimumThreshold()
    {
        var coverage = MakeCoverage(totalRegions: 5, reportedCount: 4); // only 1 unreported
        var context = MakeContext(targetRegions: 1);
        var decision = RecoveryExecutionPolicy.Evaluate(Always(), coverage, context);

        Assert.IsTrue(decision.ShouldRun, "Always mode must bypass minimum-work threshold");
        Assert.AreEqual("eligible", decision.Reason);
    }

    // -------------------------------------------------------------------------
    // Case 5 — Force Never → always skips
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case5_NeverMode_SkipsImmediately()
    {
        var coverage = MakeCoverage(totalRegions: 5, reportedCount: 1); // many unreported
        var context = MakeContext(targetRegions: 4);
        var decision = RecoveryExecutionPolicy.Evaluate(Never(), coverage, context);

        Assert.IsFalse(decision.ShouldRun);
        Assert.AreEqual(RecoverySkipReason.Disabled, decision.SkipReason);
        Assert.AreEqual("disabled", decision.Reason);
    }

    [TestMethod]
    public void Case5_NeverMode_SkipsEvenWithNullContext()
    {
        var coverage = MakeCoverage(totalRegions: 5, reportedCount: 0);
        var decision = RecoveryExecutionPolicy.Evaluate(Never(), coverage, null);

        Assert.IsFalse(decision.ShouldRun);
        Assert.AreEqual(RecoverySkipReason.Disabled, decision.SkipReason);
    }

    // -------------------------------------------------------------------------
    // Case 6 — No recoverable context → skip
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case6_NoRecoverableContext_Skips()
    {
        var coverage = MakeCoverage(totalRegions: 5, reportedCount: 2); // 3 unreported
        var emptyContext = MakeContext(targetRegions: 0); // no valid targets
        var decision = RecoveryExecutionPolicy.Evaluate(Auto(), coverage, emptyContext);

        Assert.IsFalse(decision.ShouldRun);
        Assert.AreEqual(RecoverySkipReason.NoRecoverableContext, decision.SkipReason);
        Assert.AreEqual("no_recoverable_context", decision.Reason);
    }

    [TestMethod]
    public void Case6_NullContext_DoesNotTriggerNoRecoverableContext()
    {
        // When context is null (pre-check), NoRecoverableContext must not fire.
        var coverage = MakeCoverage(totalRegions: 5, reportedCount: 2); // 3 unreported > min
        var decision = RecoveryExecutionPolicy.Evaluate(Auto(minRegions: 2), coverage, null);

        Assert.IsTrue(decision.ShouldRun, "pre-check with null context should not skip due to empty context");
    }

    // -------------------------------------------------------------------------
    // Case 7 — Skip preserves coverage (no side effects)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case7_SkipPreservesCoverage()
    {
        var coverage = MakeCoverage(totalRegions: 4, reportedCount: 0); // 4 unreported
        int unreportedBefore = coverage.UnreportedRegionIds.Count;
        Assert.AreEqual(4, unreportedBefore);

        // Policy evaluation must not mutate coverage.
        RecoveryExecutionPolicy.Evaluate(Never(), coverage, null);
        Assert.AreEqual(unreportedBefore, coverage.UnreportedRegionIds.Count,
            "skipping Recovery must not change coverage");
    }

    // -------------------------------------------------------------------------
    // Case 8 — Skip: candidate set is unmodified (Turn 2 receives primary only)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case8_SkipDoesNotMutateCandidates()
    {
        // Structural test: Evaluate() does not touch IssuesResponse.
        // The webhook is responsible for passing combinedCandidates = issuesResponse when skipped.
        // Here we simply verify the decision is a skip and ShouldRun is false.
        var coverage = MakeCoverage(totalRegions: 3, reportedCount: 2); // 1 unreported < min 2
        var context = MakeContext(targetRegions: 1);
        var decision = RecoveryExecutionPolicy.Evaluate(Auto(minRegions: 2), coverage, context);

        Assert.IsFalse(decision.ShouldRun);
    }

    // -------------------------------------------------------------------------
    // Case 9 — Always matches Phase 6 path (eligible when valid context)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case9_AlwaysWithValidContext_IsEligible()
    {
        var coverage = MakeCoverage(totalRegions: 3, reportedCount: 1);
        var context = MakeContext(targetRegions: 2);
        var decision = RecoveryExecutionPolicy.Evaluate(Always(), coverage, context);

        Assert.IsTrue(decision.ShouldRun);
        Assert.AreEqual(RecoverySkipReason.None, decision.SkipReason);
    }

    // -------------------------------------------------------------------------
    // Case 10 — Determinism
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case10_Determinism_SameInputsSameOutput()
    {
        var config = Auto(minRegions: 2);
        var coverage = MakeCoverage(totalRegions: 4, reportedCount: 2);
        var context = MakeContext(targetRegions: 2);

        var d1 = RecoveryExecutionPolicy.Evaluate(config, coverage, context);
        var d2 = RecoveryExecutionPolicy.Evaluate(config, coverage, context);

        Assert.AreEqual(d1.ShouldRun, d2.ShouldRun);
        Assert.AreEqual(d1.Reason, d2.Reason);
        Assert.AreEqual(d1.SkipReason, d2.SkipReason);
    }

    // -------------------------------------------------------------------------
    // RecoveryDecision — reason strings
    // -------------------------------------------------------------------------

    [TestMethod]
    public void DecisionReasonStrings_AreSnakeCase()
    {
        Assert.AreEqual("disabled", RecoveryDecision.Skip(RecoverySkipReason.Disabled).Reason);
        Assert.AreEqual("no_remaining_regions", RecoveryDecision.Skip(RecoverySkipReason.NoRemainingRegions).Reason);
        Assert.AreEqual("no_recoverable_context", RecoveryDecision.Skip(RecoverySkipReason.NoRecoverableContext).Reason);
        Assert.AreEqual("below_minimum_remaining_work", RecoveryDecision.Skip(RecoverySkipReason.BelowMinimumRemainingWork).Reason);
        Assert.AreEqual("eligible", RecoveryDecision.Eligible.Reason);
    }
}
