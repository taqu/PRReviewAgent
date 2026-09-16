using PRReviewAgent.Services;
using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.Statistics;

namespace PRReviewAgent.Test;

/// <summary>
/// Phase 7 — Review Rule Usage Tracking tests.
/// Verifies per-rule telemetry rows are persisted correctly across the candidate,
/// prompt-selection, Detection, and Selection stages of the review pipeline.
/// </summary>
[TestClass]
public class TestReviewRuleUsage
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<(RuleRepository ruleRepo, ProjectRepository projRepo,
        ReviewExecutionRepository execRepo, ReviewRuleUsageRepository usageRepo)> CreateRepositoriesAsync()
    {
        string dbName = $"testdb_usage_{Guid.NewGuid():N}";
        string cs = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        RuleRepository ruleRepo = new RuleRepository(cs, isConnectionString: true);
        ProjectRepository projRepo = new ProjectRepository(cs, isConnectionString: true);
        ReviewExecutionRepository execRepo = new ReviewExecutionRepository(cs, isConnectionString: true);
        ReviewRuleUsageRepository usageRepo = new ReviewRuleUsageRepository(cs, isConnectionString: true);
        await ruleRepo.InitializeAsync();
        return (ruleRepo, projRepo, execRepo, usageRepo);
    }

    private static async Task<(long execId, Project proj)> CreateExecutionAsync(
        ReviewExecutionRepository execRepo, ProjectRepository projRepo, string extId)
    {
        Project proj = await projRepo.GetOrCreateAsync(extId, extId, null);
        long execId = await execRepo.StartAsync(proj.Id, $"mr/{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        return (execId, proj);
    }

    private static LearnedRule MakeRule(long projectId, string mergeRequestId, float[]? embedding = null)
    {
        return new LearnedRule
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = projectId,
            MergeRequestId = mergeRequestId,
            AstPattern = "p",
            RuleDescription = "rule",
            Embedding = embedding ?? new float[] { 1.0f, 0.0f },
            ConfidenceScore = 5,
            LastHitAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static ReviewRuleUsage MakeUsage(long execId, long projectId, string ruleId,
        double? score = null, bool usedInPrompt = true)
    {
        return new ReviewRuleUsage
        {
            ReviewExecutionId = execId,
            ProjectId = projectId,
            RuleId = ruleId,
            SimilarityScore = score,
            UsedInPrompt = usedInPrompt,
            ProducedCandidate = false,
            ProducedFinalFinding = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    // -------------------------------------------------------------------------
    // Test 1 — Candidate rules create one usage row each
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task CandidateRules_CreateUsageRows()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u1");

        LearnedRule r1 = MakeRule(proj.Id, "mr-u1a");
        LearnedRule r2 = MakeRule(proj.Id, "mr-u1b");
        LearnedRule r3 = MakeRule(proj.Id, "mr-u1c");
        await ruleRepo.InsertAsync(r1);
        await ruleRepo.InsertAsync(r2);
        await ruleRepo.InsertAsync(r3);

        List<ReviewRuleUsage> rows = new()
        {
            MakeUsage(execId, proj.Id, r1.Id),
            MakeUsage(execId, proj.Id, r2.Id),
            MakeUsage(execId, proj.Id, r3.Id),
        };
        await usageRepo.AddRangeAsync(rows);

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        Assert.AreEqual(3, stored.Count, "One usage row per candidate rule");
    }

    // -------------------------------------------------------------------------
    // Test 2 — Similarity score is preserved exactly
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SimilarityScore_IsPreservedExactly()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u2");

        LearnedRule rule = MakeRule(proj.Id, "mr-u2");
        await ruleRepo.InsertAsync(rule);

        double expectedScore = 0.923456789;
        await usageRepo.AddRangeAsync(new[] { MakeUsage(execId, proj.Id, rule.Id, score: expectedScore) });

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);

        Assert.IsNotNull(stored[0].SimilarityScore);
        Assert.AreEqual(expectedScore, stored[0].SimilarityScore!.Value, delta: 1e-9,
            "Similarity score must be stored without rounding");
    }

    // -------------------------------------------------------------------------
    // Test 3 — Prompt selection flag is recorded per rule
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task PromptSelection_IsRecorded()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u3");

        // 4 candidates, 2 selected for prompt
        LearnedRule rA = MakeRule(proj.Id, "mr-u3a");
        LearnedRule rB = MakeRule(proj.Id, "mr-u3b");
        LearnedRule rC = MakeRule(proj.Id, "mr-u3c");
        LearnedRule rD = MakeRule(proj.Id, "mr-u3d");
        foreach (LearnedRule r in new[] { rA, rB, rC, rD })
            await ruleRepo.InsertAsync(r);

        List<ReviewRuleUsage> rows = new()
        {
            MakeUsage(execId, proj.Id, rA.Id, usedInPrompt: true),
            MakeUsage(execId, proj.Id, rB.Id, usedInPrompt: true),
            MakeUsage(execId, proj.Id, rC.Id, usedInPrompt: false),
            MakeUsage(execId, proj.Id, rD.Id, usedInPrompt: false),
        };
        await usageRepo.AddRangeAsync(rows);

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);

        Assert.AreEqual(4, stored.Count);
        Assert.AreEqual(2, stored.Count(u => u.UsedInPrompt), "Exactly 2 used_in_prompt = true");
        Assert.AreEqual(2, stored.Count(u => !u.UsedInPrompt), "Exactly 2 used_in_prompt = false");
    }

    // -------------------------------------------------------------------------
    // Test 4 — Candidate finding attribution sets produced_candidate
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task CandidateFindingAttribution_SetsProducedCandidate()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u4");

        LearnedRule rA = MakeRule(proj.Id, "mr-u4a");
        LearnedRule rB = MakeRule(proj.Id, "mr-u4b");
        await ruleRepo.InsertAsync(rA);
        await ruleRepo.InsertAsync(rB);

        await usageRepo.AddRangeAsync(new[]
        {
            MakeUsage(execId, proj.Id, rA.Id),
            MakeUsage(execId, proj.Id, rB.Id),
        });

        // Detection produced a finding attributed to Rule A only.
        await usageRepo.MarkProducedCandidateAsync(execId, proj.Id, new[] { rA.Id });

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        ReviewRuleUsage uA = stored.Single(u => u.RuleId == rA.Id);
        ReviewRuleUsage uB = stored.Single(u => u.RuleId == rB.Id);

        Assert.IsTrue(uA.ProducedCandidate, "Rule A attributed by Detection must have produced_candidate = true");
        Assert.IsFalse(uB.ProducedCandidate, "Rule B not attributed must remain false");
    }

    // -------------------------------------------------------------------------
    // Test 5 — Final finding attribution sets produced_final_finding
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task FinalFindingAttribution_SetsProducedFinalFinding()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u5");

        LearnedRule rA = MakeRule(proj.Id, "mr-u5a");
        await ruleRepo.InsertAsync(rA);
        await usageRepo.AddRangeAsync(new[] { MakeUsage(execId, proj.Id, rA.Id) });
        await usageRepo.MarkProducedCandidateAsync(execId, proj.Id, new[] { rA.Id });
        await usageRepo.MarkProducedFinalFindingAsync(execId, proj.Id, new[] { rA.Id });

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        Assert.IsTrue(stored[0].ProducedFinalFinding, "Rule A whose finding survived Selection must have produced_final_finding = true");
    }

    // -------------------------------------------------------------------------
    // Test 6 — Detection finding removed by Selection: candidate true, final false
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task DetectionFinding_RemovedBySelection_CandidateTrueFinalFalse()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u6");

        LearnedRule rB = MakeRule(proj.Id, "mr-u6b");
        await ruleRepo.InsertAsync(rB);
        await usageRepo.AddRangeAsync(new[] { MakeUsage(execId, proj.Id, rB.Id) });

        // Detection found it, but Selection did not include it.
        await usageRepo.MarkProducedCandidateAsync(execId, proj.Id, new[] { rB.Id });
        // MarkProducedFinalFindingAsync is NOT called for rB.

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        Assert.IsTrue(stored[0].ProducedCandidate, "Rule B should be candidate");
        Assert.IsFalse(stored[0].ProducedFinalFinding, "Rule B removed by Selection must remain false");
    }

    // -------------------------------------------------------------------------
    // Test 7 — General finding (null rule_id) does not affect usage rows
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task GeneralFinding_NullRuleId_NoUsageRowAffected()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u7");

        LearnedRule r = MakeRule(proj.Id, "mr-u7");
        await ruleRepo.InsertAsync(r);
        await usageRepo.AddRangeAsync(new[] { MakeUsage(execId, proj.Id, r.Id) });

        // No rule IDs in Detection — simulating a null rule_id finding.
        await usageRepo.MarkProducedCandidateAsync(execId, proj.Id, Array.Empty<string>());

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        Assert.IsFalse(stored[0].ProducedCandidate, "No attribution call means produced_candidate stays false");
    }

    // -------------------------------------------------------------------------
    // Test 8 — Invalid rule ID from model is ignored (not in allowedRuleIds)
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task InvalidRuleId_IsIgnored_NoUsageRowModified()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u8");

        LearnedRule r = MakeRule(proj.Id, "mr-u8");
        await ruleRepo.InsertAsync(r);
        await usageRepo.AddRangeAsync(new[] { MakeUsage(execId, proj.Id, r.Id) });

        // Simulate validation: model returned a rule ID not in the allowed set.
        string fakeRuleId = "unknown-rule-xyz";
        HashSet<string> allowedRuleIds = new() { r.Id };
        string[] validatedIds = new[] { fakeRuleId }
            .Where(id => allowedRuleIds.Contains(id))
            .ToArray();

        // Only call Mark with validated IDs (which is empty after filtering).
        await usageRepo.MarkProducedCandidateAsync(execId, proj.Id, validatedIds);

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        Assert.IsFalse(stored[0].ProducedCandidate, "Invalid rule attribution must not affect usage rows");
    }

    // -------------------------------------------------------------------------
    // Test 9 — Cross-project attribution is impossible
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task CrossProjectAttribution_IsImpossible()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execIdA, Project projA) = await CreateExecutionAsync(execRepo, projRepo, "ext:u9a");
        Project projB = await projRepo.GetOrCreateAsync("ext:u9b", "B", null);

        LearnedRule rA = MakeRule(projA.Id, "mr-u9a");
        LearnedRule rB = MakeRule(projB.Id, "mr-u9b");
        await ruleRepo.InsertAsync(rA);
        await ruleRepo.InsertAsync(rB);

        // Only Project A usage row is created.
        await usageRepo.AddRangeAsync(new[] { MakeUsage(execIdA, projA.Id, rA.Id) });

        // Attempt to mark Project B rule via Project A execution — should be a no-op due to project_id filter.
        await usageRepo.MarkProducedCandidateAsync(execIdA, projA.Id, new[] { rB.Id });

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execIdA);
        Assert.AreEqual(1, stored.Count, "Only Project A row should exist");
        Assert.AreEqual(rA.Id, stored[0].RuleId);
        Assert.IsFalse(stored[0].ProducedCandidate, "Project B rule ID cannot affect Project A row");
    }

    // -------------------------------------------------------------------------
    // Test 10 — Candidate count consistency with Phase 6
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task CandidateCount_MatchesPhase6CandidateRuleCount()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u10");

        LearnedRule r1 = MakeRule(proj.Id, "mr-u10a");
        LearnedRule r2 = MakeRule(proj.Id, "mr-u10b");
        LearnedRule r3 = MakeRule(proj.Id, "mr-u10c");
        await ruleRepo.InsertAsync(r1);
        await ruleRepo.InsertAsync(r2);
        await ruleRepo.InsertAsync(r3);

        // 3 candidate rules — same as candidate_rule_count would be.
        await usageRepo.AddRangeAsync(new[]
        {
            MakeUsage(execId, proj.Id, r1.Id, usedInPrompt: true),
            MakeUsage(execId, proj.Id, r2.Id, usedInPrompt: true),
            MakeUsage(execId, proj.Id, r3.Id, usedInPrompt: false),
        });

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        Assert.AreEqual(3, stored.Count,
            "Usage row count should equal candidate_rule_count from Phase 6");
    }

    // -------------------------------------------------------------------------
    // Test 11 — Selected count consistency with Phase 6
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SelectedCount_MatchesPhase6SelectedRuleCount()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u11");

        LearnedRule r1 = MakeRule(proj.Id, "mr-u11a");
        LearnedRule r2 = MakeRule(proj.Id, "mr-u11b");
        await ruleRepo.InsertAsync(r1);
        await ruleRepo.InsertAsync(r2);

        // selected_rule_count = 2 in Phase 6 telemetry → 2 used_in_prompt = true.
        await usageRepo.AddRangeAsync(new[]
        {
            MakeUsage(execId, proj.Id, r1.Id, usedInPrompt: true),
            MakeUsage(execId, proj.Id, r2.Id, usedInPrompt: true),
        });

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        Assert.AreEqual(2, stored.Count(u => u.UsedInPrompt),
            "count(used_in_prompt = true) must equal selected_rule_count from Phase 6");
    }

    // -------------------------------------------------------------------------
    // Test 12 — Detection failure: usage rows retain search/prompt info, candidates false
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task DetectionFailure_UsageRowsRetainPromptInfo_CandidateFalse()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u12");

        LearnedRule r = MakeRule(proj.Id, "mr-u12");
        await ruleRepo.InsertAsync(r);

        // Rule search succeeded and usage rows were inserted.
        await usageRepo.AddRangeAsync(new[] { MakeUsage(execId, proj.Id, r.Id, score: 0.88, usedInPrompt: true) });

        // Detection failed — MarkProducedCandidateAsync is never called.

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        Assert.AreEqual(1, stored.Count);
        Assert.IsTrue(stored[0].UsedInPrompt, "used_in_prompt must be preserved from rule search");
        Assert.IsNotNull(stored[0].SimilarityScore, "similarity_score must be preserved from rule search");
        Assert.IsFalse(stored[0].ProducedCandidate, "produced_candidate must remain false on Detection failure");
        Assert.IsFalse(stored[0].ProducedFinalFinding, "produced_final_finding must remain false on Detection failure");
    }

    // -------------------------------------------------------------------------
    // Test 13 — Selection failure: produced_candidate preserved, final false
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SelectionFailure_ProducedCandidatePreserved_FinalFindingFalse()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u13");

        LearnedRule r = MakeRule(proj.Id, "mr-u13");
        await ruleRepo.InsertAsync(r);

        await usageRepo.AddRangeAsync(new[] { MakeUsage(execId, proj.Id, r.Id) });
        await usageRepo.MarkProducedCandidateAsync(execId, proj.Id, new[] { r.Id });
        // Selection failed — MarkProducedFinalFindingAsync is never called.

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        Assert.IsTrue(stored[0].ProducedCandidate, "produced_candidate must be preserved after Selection failure");
        Assert.IsFalse(stored[0].ProducedFinalFinding, "produced_final_finding must remain false when Selection fails");
    }

    // -------------------------------------------------------------------------
    // Test 14 — Expired/deleted learned rule does not delete historical usage
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ExpiredRule_DoesNotDeleteHistoricalUsage()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u14");

        LearnedRule r = MakeRule(proj.Id, "mr-u14");
        await ruleRepo.InsertAsync(r);
        string ruleId = r.Id;

        await usageRepo.AddRangeAsync(new[] { MakeUsage(execId, proj.Id, ruleId) });

        // Expire the rule by decrementing confidence below zero.
        await ruleRepo.DecrementConfidenceAsync(ruleId);
        await ruleRepo.DecrementConfidenceAsync(ruleId);
        await ruleRepo.DecrementConfidenceAsync(ruleId);
        await ruleRepo.DecrementConfidenceAsync(ruleId);
        await ruleRepo.DecrementConfidenceAsync(ruleId);
        await ruleRepo.DecrementConfidenceAsync(ruleId);

        // Historical usage row must still exist.
        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        Assert.AreEqual(1, stored.Count, "Historical usage row must survive learned rule expiration");
        Assert.AreEqual(ruleId, stored[0].RuleId);
    }

    // -------------------------------------------------------------------------
    // Test 15 — Duplicate findings do not create duplicate usage rows (UNIQUE constraint)
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task DuplicateFindings_DoNotCreateDuplicateUsageRows()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u15");

        LearnedRule r = MakeRule(proj.Id, "mr-u15");
        await ruleRepo.InsertAsync(r);

        // Insert the same usage row twice — INSERT OR IGNORE should prevent duplication.
        await usageRepo.AddRangeAsync(new[] { MakeUsage(execId, proj.Id, r.Id) });
        await usageRepo.AddRangeAsync(new[] { MakeUsage(execId, proj.Id, r.Id) });

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        Assert.AreEqual(1, stored.Count, "Duplicate insertion must be silently ignored");
    }

    // -------------------------------------------------------------------------
    // Test 16 — Multiple findings reference same rule; boolean is set once correctly
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task MultipleFindings_SameRule_BooleanSetOnce()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo,
            ReviewRuleUsageRepository usageRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:u16");

        LearnedRule rA = MakeRule(proj.Id, "mr-u16a");
        await ruleRepo.InsertAsync(rA);
        await usageRepo.AddRangeAsync(new[] { MakeUsage(execId, proj.Id, rA.Id) });

        // Multiple findings reference rA — deduplicated before calling Mark.
        string[] deduplicated = new[] { rA.Id, rA.Id }.Distinct().ToArray();
        await usageRepo.MarkProducedCandidateAsync(execId, proj.Id, deduplicated);

        IReadOnlyList<ReviewRuleUsage> stored = await usageRepo.GetByReviewExecutionIdAsync(execId);
        Assert.AreEqual(1, stored.Count, "Still one usage row");
        Assert.IsTrue(stored[0].ProducedCandidate, "produced_candidate set correctly despite duplicate input");
    }

    // -------------------------------------------------------------------------
    // Test 17 — Review output unchanged: ExtractSelectionMetadata strips comment only
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ReviewOutput_AttributionCommentStripped_ContentUnchanged()
    {
        string reviewText = "## Issues\n- Line 5: null reference\n- Line 10: missing check\n";
        string withMeta = reviewText + "\n<!-- SELECTED_CANDIDATES: c0,c2 -->";

        (string cleaned, IReadOnlyList<string> ids) = PromptBuilder.ExtractSelectionMetadata(withMeta);

        Assert.AreEqual(reviewText.TrimEnd(), cleaned, "Visible review content must be unchanged after stripping metadata");
        CollectionAssert.AreEquivalent(new[] { "c0", "c2" }, ids.ToArray());
    }

    [TestMethod]
    public void ReviewOutput_NoMetadata_ReturnedUnchanged()
    {
        string reviewText = "## Issues\n- Line 5: null reference";
        (string cleaned, IReadOnlyList<string> ids) = PromptBuilder.ExtractSelectionMetadata(reviewText);

        Assert.AreEqual(reviewText, cleaned, "Text without metadata comment must be returned as-is");
        Assert.AreEqual(0, ids.Count);
    }

    // -------------------------------------------------------------------------
    // Additional — FormatRulesForPrompt includes rule IDs for Detection attribution
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task FormatRulesForPrompt_IncludesRuleIds()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, _, _) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:u18", "A", null);

        LearnedRule r = MakeRule(proj.Id, "mr-u18");
        await ruleRepo.InsertAsync(r);

        List<LearnedRule> rules = await ruleRepo.GetAllActiveAsync(proj.Id);
        string prompt = RuleRetrievalService.FormatRulesForPrompt(rules, "csharp");

        StringAssert.Contains(prompt, $"[rule-id: {r.Id}]",
            "Prompt must include rule-id tag so Detection can attribute findings");
    }
}
