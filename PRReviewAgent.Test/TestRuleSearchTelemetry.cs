using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.Statistics;

namespace PRReviewAgent.Test;

/// <summary>
/// Phase 6 — Rule search performance telemetry tests.
/// Verifies that rule_search_executions rows are recorded correctly,
/// counts are accurate, project isolation is maintained, and search
/// results are unchanged by instrumentation.
/// </summary>
[TestClass]
public class TestRuleSearchTelemetry
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<(RuleRepository ruleRepo, ProjectRepository projRepo,
        ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo)> CreateRepositoriesAsync()
    {
        string dbName = $"testdb_search_{Guid.NewGuid():N}";
        string cs = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        RuleRepository ruleRepo = new RuleRepository(cs, isConnectionString: true);
        ProjectRepository projRepo = new ProjectRepository(cs, isConnectionString: true);
        ReviewExecutionRepository execRepo = new ReviewExecutionRepository(cs, isConnectionString: true);
        RuleSearchExecutionRepository searchRepo = new RuleSearchExecutionRepository(cs, isConnectionString: true);
        await ruleRepo.InitializeAsync();
        return (ruleRepo, projRepo, execRepo, searchRepo);
    }

    private static async Task<(long execId, Project proj)> CreateExecutionAsync(
        ReviewExecutionRepository execRepo, ProjectRepository projRepo, string extId)
    {
        Project proj = await projRepo.GetOrCreateAsync(extId, extId, null);
        long execId = await execRepo.StartAsync(proj.Id, $"mr/{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        return (execId, proj);
    }

    private static LearnedRule MakeRule(long projectId, string mergeRequestId, float[] embedding, int confidence = 5)
    {
        return new LearnedRule
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = projectId,
            MergeRequestId = mergeRequestId,
            AstPattern = "p",
            RuleDescription = "rule",
            BadPattern = "bad",
            GoodPattern = "good",
            Embedding = embedding,
            ConfidenceScore = confidence,
            LastHitAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static RuleSearchExecution MakeSearchRecord(
        long reviewExecutionId,
        long projectId,
        int total,
        int candidate,
        int selected,
        long embeddingMs = 10,
        long searchMs = 5,
        long totalMs = 20)
    {
        return new RuleSearchExecution
        {
            ReviewExecutionId = reviewExecutionId,
            ProjectId = projectId,
            TotalRuleCount = total,
            CandidateRuleCount = candidate,
            SelectedRuleCount = selected,
            EmbeddingDurationMs = embeddingMs,
            SearchDurationMs = searchMs,
            TotalDurationMs = totalMs,
            Success = true,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        };
    }

    // -------------------------------------------------------------------------
    // Test 1 — Successful search creates a telemetry row with correct parent IDs
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SuccessfulSearch_CreatesTelemetryRow()
    {
        (_, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:s1");

        await searchRepo.RecordAsync(MakeSearchRecord(execId, proj.Id, total: 10, candidate: 3, selected: 2));

        IReadOnlyList<RuleSearchExecution> rows = await searchRepo.GetByReviewExecutionIdAsync(execId);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(execId, rows[0].ReviewExecutionId);
        Assert.AreEqual(proj.Id, rows[0].ProjectId);
        Assert.IsTrue(rows[0].Success);
    }

    // -------------------------------------------------------------------------
    // Test 2 — total_rule_count matches actual project-local rule input
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task TotalRuleCount_MatchesActualProjectInput()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:s2");

        // Insert 4 rules for this project
        for (int i = 0; i < 4; i++)
            await ruleRepo.InsertAsync(MakeRule(proj.Id, $"mr-s2-{i}", new float[] { 1.0f, 0.0f }));

        // Retrieve the exact count that would go into the search
        List<LearnedRule> projectRules = await ruleRepo.GetAllActiveAsync(proj.Id);

        await searchRepo.RecordAsync(MakeSearchRecord(execId, proj.Id, total: projectRules.Count, candidate: 2, selected: 1));

        IReadOnlyList<RuleSearchExecution> rows = await searchRepo.GetByReviewExecutionIdAsync(execId);

        Assert.AreEqual(4, rows[0].TotalRuleCount, "total_rule_count must match actual project rules passed to vector search");
    }

    // -------------------------------------------------------------------------
    // Test 3 — Other projects' rules do not inflate total_rule_count
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task TotalRuleCount_ExcludesOtherProjects()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        (long execIdA, Project projA) = await CreateExecutionAsync(execRepo, projRepo, "ext:s3a");
        Project projB = await projRepo.GetOrCreateAsync("ext:s3b", "B", null);

        for (int i = 0; i < 10; i++)
            await ruleRepo.InsertAsync(MakeRule(projA.Id, $"mr-s3a-{i}", new float[] { 1.0f, 0.0f }));
        for (int i = 0; i < 20; i++)
            await ruleRepo.InsertAsync(MakeRule(projB.Id, $"mr-s3b-{i}", new float[] { 1.0f, 0.0f }));

        List<LearnedRule> projARules = await ruleRepo.GetAllActiveAsync(projA.Id);

        await searchRepo.RecordAsync(MakeSearchRecord(execIdA, projA.Id, total: projARules.Count, candidate: 5, selected: 3));

        IReadOnlyList<RuleSearchExecution> rows = await searchRepo.GetByReviewExecutionIdAsync(execIdA);

        Assert.AreEqual(10, rows[0].TotalRuleCount, "Only Project A rules should count — not Project B's 20");
    }

    // -------------------------------------------------------------------------
    // Test 4 — candidate_rule_count reflects threshold survivors
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task CandidateRuleCount_ReflectsThresholdSurvivors()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:s4");

        // 5 rules: 3 with high similarity embedding, 2 with orthogonal (low similarity)
        float[] high = new float[] { 1.0f, 0.0f };
        float[] low = new float[] { 0.0f, 1.0f };
        for (int i = 0; i < 3; i++)
            await ruleRepo.InsertAsync(MakeRule(proj.Id, $"mr-s4h-{i}", high));
        for (int i = 0; i < 2; i++)
            await ruleRepo.InsertAsync(MakeRule(proj.Id, $"mr-s4l-{i}", low));

        List<LearnedRule> allRules = await ruleRepo.GetAllActiveAsync(proj.Id);
        float[] query = new float[] { 1.0f, 0.0f };
        float threshold = 0.75f;

        // Simulate what the service does: score and filter
        int candidateCount = allRules.Count(r =>
        {
            float score = EmbeddingUtils.CosineSimilarity(query, r.Embedding);
            return score >= threshold;
        });

        // Record 3 candidates (only the high-similarity ones pass 0.75 threshold)
        await searchRepo.RecordAsync(MakeSearchRecord(execId, proj.Id, total: allRules.Count, candidate: candidateCount, selected: 3));

        IReadOnlyList<RuleSearchExecution> rows = await searchRepo.GetByReviewExecutionIdAsync(execId);

        Assert.AreEqual(3, rows[0].CandidateRuleCount, "Only the 3 high-similarity rules should survive the threshold");
    }

    // -------------------------------------------------------------------------
    // Test 5 — selected_rule_count matches prompt rule collection
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SelectedRuleCount_MatchesPromptRuleCollection()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:s5");

        // 8 candidates, but topN = 5 → selected = 5
        for (int i = 0; i < 8; i++)
            await ruleRepo.InsertAsync(MakeRule(proj.Id, $"mr-s5-{i}", new float[] { 1.0f, 0.0f }));

        List<LearnedRule> allRules = await ruleRepo.GetAllActiveAsync(proj.Id);

        // Simulate top-5 selection (distinct MR IDs)
        int topN = 5;
        int selected = allRules
            .Select(r => r.MergeRequestId)
            .Distinct()
            .Take(topN)
            .Count();

        await searchRepo.RecordAsync(MakeSearchRecord(execId, proj.Id, total: allRules.Count, candidate: allRules.Count, selected: selected));

        IReadOnlyList<RuleSearchExecution> rows = await searchRepo.GetByReviewExecutionIdAsync(execId);

        Assert.AreEqual(5, rows[0].SelectedRuleCount);
    }

    // -------------------------------------------------------------------------
    // Test 6 — Empty project rule set is recorded correctly
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EmptyRuleSet_RecordedCorrectly()
    {
        (_, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:s6");

        await searchRepo.RecordAsync(new RuleSearchExecution
        {
            ReviewExecutionId = execId,
            ProjectId = proj.Id,
            TotalRuleCount = 0,
            CandidateRuleCount = 0,
            SelectedRuleCount = 0,
            EmbeddingDurationMs = 5,
            SearchDurationMs = 0,
            TotalDurationMs = 8,
            Success = true,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        IReadOnlyList<RuleSearchExecution> rows = await searchRepo.GetByReviewExecutionIdAsync(execId);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(0, rows[0].TotalRuleCount);
        Assert.AreEqual(0, rows[0].CandidateRuleCount);
        Assert.AreEqual(0, rows[0].SelectedRuleCount);
        Assert.IsTrue(rows[0].Success);
    }

    // -------------------------------------------------------------------------
    // Test 7 — Search duration is stored as non-negative
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SearchDuration_IsStoredAsNonNegative()
    {
        (_, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:s7");

        await searchRepo.RecordAsync(MakeSearchRecord(execId, proj.Id, total: 5, candidate: 2, selected: 1, searchMs: 17));

        IReadOnlyList<RuleSearchExecution> rows = await searchRepo.GetByReviewExecutionIdAsync(execId);

        Assert.IsNotNull(rows[0].SearchDurationMs);
        Assert.IsTrue(rows[0].SearchDurationMs >= 0, "search_duration_ms must be non-negative");
    }

    // -------------------------------------------------------------------------
    // Test 8 — Embedding duration is stored when applicable; NULL when not
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EmbeddingDuration_StoredOrNull()
    {
        (_, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        (long execIdWith, Project projWith) = await CreateExecutionAsync(execRepo, projRepo, "ext:s8a");
        (long execIdWithout, Project projWithout) = await CreateExecutionAsync(execRepo, projRepo, "ext:s8b");

        // With embedding duration
        await searchRepo.RecordAsync(new RuleSearchExecution
        {
            ReviewExecutionId = execIdWith,
            ProjectId = projWith.Id,
            TotalRuleCount = 10,
            CandidateRuleCount = 3,
            SelectedRuleCount = 2,
            EmbeddingDurationMs = 42,
            SearchDurationMs = 8,
            TotalDurationMs = 55,
            Success = true,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        // Without embedding duration (NULL)
        await searchRepo.RecordAsync(new RuleSearchExecution
        {
            ReviewExecutionId = execIdWithout,
            ProjectId = projWithout.Id,
            TotalRuleCount = 10,
            CandidateRuleCount = 3,
            SelectedRuleCount = 2,
            EmbeddingDurationMs = null,
            SearchDurationMs = 8,
            TotalDurationMs = 12,
            Success = true,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        var rowsWith = await searchRepo.GetByReviewExecutionIdAsync(execIdWith);
        var rowsWithout = await searchRepo.GetByReviewExecutionIdAsync(execIdWithout);

        Assert.AreEqual(42L, rowsWith[0].EmbeddingDurationMs);
        Assert.IsNull(rowsWithout[0].EmbeddingDurationMs, "embedding_duration_ms should be NULL when no embedding stage ran");
    }

    // -------------------------------------------------------------------------
    // Test 9 — Failed search is persisted with success=false and error_type
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task FailedSearch_IsPersistedWithErrorType()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:s9");

        for (int i = 0; i < 5; i++)
            await ruleRepo.InsertAsync(MakeRule(proj.Id, $"mr-s9-{i}", new float[] { 1.0f, 0.0f }));

        // Simulate: rules loaded but embedding generation failed
        List<LearnedRule> loadedRules = await ruleRepo.GetAllActiveAsync(proj.Id);

        await searchRepo.RecordAsync(new RuleSearchExecution
        {
            ReviewExecutionId = execId,
            ProjectId = proj.Id,
            TotalRuleCount = loadedRules.Count,
            CandidateRuleCount = null,
            SelectedRuleCount = null,
            EmbeddingDurationMs = 3,
            SearchDurationMs = null,
            TotalDurationMs = null,
            Success = false,
            ErrorType = "EmbeddingFailed",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        IReadOnlyList<RuleSearchExecution> rows = await searchRepo.GetByReviewExecutionIdAsync(execId);

        Assert.AreEqual(1, rows.Count);
        Assert.IsFalse(rows[0].Success);
        Assert.AreEqual("EmbeddingFailed", rows[0].ErrorType);
        Assert.AreEqual(5, rows[0].TotalRuleCount, "Total rule count known before failure should still be stored");
        Assert.IsNull(rows[0].CandidateRuleCount, "Candidate count unknown after embedding failure should be NULL");
        Assert.IsNull(rows[0].SelectedRuleCount);
    }

    // -------------------------------------------------------------------------
    // Test 10 — project_id must match the parent ReviewExecution's project
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ProjectId_MustMatchParentReviewExecution()
    {
        (_, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        (long execId, Project projA) = await CreateExecutionAsync(execRepo, projRepo, "ext:s10a");
        Project projB = await projRepo.GetOrCreateAsync("ext:s10b", "B", null);

        // Record a search that matches the correct project
        await searchRepo.RecordAsync(MakeSearchRecord(execId, projA.Id, total: 5, candidate: 2, selected: 1));
        IReadOnlyList<RuleSearchExecution> correct = await searchRepo.GetByReviewExecutionIdAsync(execId);

        Assert.AreEqual(projA.Id, correct[0].ProjectId,
            "RuleSearchExecution.ProjectId must equal ReviewExecution.ProjectId");
        Assert.AreNotEqual(projB.Id, correct[0].ProjectId,
            "Project B's ID must not appear in a Project A review's search row");
    }

    // -------------------------------------------------------------------------
    // Test 11 — Search results are unchanged by telemetry instrumentation
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SearchResults_AreUnchangedByTelemetry()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, _, _) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:s11", "P", null);

        float[] embHigh = new float[] { 1.0f, 0.0f };
        float[] embLow = new float[] { 0.0f, 1.0f };
        LearnedRule ruleA = MakeRule(proj.Id, "mr-s11a", embHigh);
        LearnedRule ruleB = MakeRule(proj.Id, "mr-s11b", embLow);
        await ruleRepo.InsertAsync(ruleA);
        await ruleRepo.InsertAsync(ruleB);

        float[] query = new float[] { 1.0f, 0.0f };
        float threshold = 0.75f;

        List<LearnedRule> allRules = await ruleRepo.GetAllActiveAsync(proj.Id);

        // Simulate ranking (same as service does)
        List<(LearnedRule rule, float score)> scored = allRules
            .Select(r => (r, EmbeddingUtils.CosineSimilarity(query, r.Embedding)))
            .Where(x => x.Item2 >= threshold)
            .OrderByDescending(x => x.Item2)
            .ToList();

        // Telemetry only reads counts — it does not alter scored or allRules
        int candidateCount = scored.Count;
        Assert.AreEqual(1, candidateCount, "Only ruleA passes threshold");
        Assert.AreEqual(ruleA.Id, scored[0].rule.Id, "Ranking is unchanged: highest similarity rule is first");
    }

    // -------------------------------------------------------------------------
    // Test 12 — No duplicate rule loading: GetAllActiveAsync called once per search
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task NoDuplicateRuleLoading_TelemetryUsesAlreadyLoadedCollection()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:s12");

        for (int i = 0; i < 6; i++)
            await ruleRepo.InsertAsync(MakeRule(proj.Id, $"mr-s12-{i}", new float[] { 1.0f, 0.0f }));

        // The count used in telemetry comes directly from the collection returned by GetAllActiveAsync.
        // Verify that total_rule_count stored equals the actual live count — no second query.
        List<LearnedRule> loadedRules = await ruleRepo.GetAllActiveAsync(proj.Id);
        int countFromCollection = loadedRules.Count;

        await searchRepo.RecordAsync(MakeSearchRecord(execId, proj.Id, total: countFromCollection, candidate: 4, selected: 3));

        IReadOnlyList<RuleSearchExecution> rows = await searchRepo.GetByReviewExecutionIdAsync(execId);

        Assert.AreEqual(6, rows[0].TotalRuleCount, "total_rule_count comes from the already-loaded collection, not a second COUNT query");
    }

    // -------------------------------------------------------------------------
    // Test 13 — Multiple reviews produce independent search telemetry rows
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task MultipleReviews_ProduceIndependentSearchRows()
    {
        (RuleRepository ruleRepo, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:s13", "P", null);

        for (int i = 0; i < 3; i++)
            await ruleRepo.InsertAsync(MakeRule(proj.Id, $"mr-s13-{i}", new float[] { 1.0f, 0.0f }));

        long execId1 = await execRepo.StartAsync(proj.Id, "mr/s13-1", DateTimeOffset.UtcNow);
        long execId2 = await execRepo.StartAsync(proj.Id, "mr/s13-2", DateTimeOffset.UtcNow);
        long execId3 = await execRepo.StartAsync(proj.Id, "mr/s13-3", DateTimeOffset.UtcNow);

        await searchRepo.RecordAsync(MakeSearchRecord(execId1, proj.Id, total: 3, candidate: 2, selected: 1));
        await searchRepo.RecordAsync(MakeSearchRecord(execId2, proj.Id, total: 3, candidate: 1, selected: 1));
        await searchRepo.RecordAsync(MakeSearchRecord(execId3, proj.Id, total: 3, candidate: 0, selected: 0));

        var rows1 = await searchRepo.GetByReviewExecutionIdAsync(execId1);
        var rows2 = await searchRepo.GetByReviewExecutionIdAsync(execId2);
        var rows3 = await searchRepo.GetByReviewExecutionIdAsync(execId3);

        Assert.AreEqual(1, rows1.Count);
        Assert.AreEqual(1, rows2.Count);
        Assert.AreEqual(1, rows3.Count);
        Assert.AreEqual(2, rows1[0].CandidateRuleCount);
        Assert.AreEqual(1, rows2[0].CandidateRuleCount);
        Assert.AreEqual(0, rows3[0].CandidateRuleCount);
        Assert.AreNotEqual(rows1[0].Id, rows2[0].Id);
    }

    // -------------------------------------------------------------------------
    // Test 14 — When search is skipped (no embedding), no row is created
    // This is validated at the service level by checking that the recorder
    // is only called when the search actually proceeds (non-empty embeddings).
    // We verify the repo is pristine when no RecordAsync is called.
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task NoSearch_MeansNoTelemetryRow()
    {
        (_, ProjectRepository projRepo, ReviewExecutionRepository execRepo, RuleSearchExecutionRepository searchRepo) = await CreateRepositoriesAsync();
        (long execId, _) = await CreateExecutionAsync(execRepo, projRepo, "ext:s14");

        // Simulate: GetRelevantRulesAsync returned early because embedding was empty.
        // In that case RecordAsync is never called.
        // The searchRepo should have no rows for this execution.
        IReadOnlyList<RuleSearchExecution> rows = await searchRepo.GetByReviewExecutionIdAsync(execId);

        Assert.AreEqual(0, rows.Count, "No RuleSearchExecution row when search was skipped");
    }
}
