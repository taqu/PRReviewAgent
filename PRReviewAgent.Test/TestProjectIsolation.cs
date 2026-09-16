using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using PRReviewAgent.Services.AutoImprove;

namespace PRReviewAgent.Test;

/// <summary>
/// Tests for multi-project isolation of learned rules (Phase 1).
/// All tests use an in-memory SQLite database to avoid file system dependencies.
/// </summary>
[TestClass]
public class TestProjectIsolation
{
    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static async Task<(RuleRepository repo, ProjectRepository projRepo)> CreateRepositoriesAsync()
    {
        // Use a unique named in-memory SQLite database to allow connection sharing
        string dbName = $"testdb_{Guid.NewGuid():N}";
        string cs = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        RuleRepository repo = new RuleRepository(cs, isConnectionString: true);
        ProjectRepository projRepo = new ProjectRepository(cs, isConnectionString: true);
        await repo.InitializeAsync();
        return (repo, projRepo);
    }

    private static LearnedRule MakeRule(long projectId, string mergeRequestId, string description = "test rule")
    {
        return new LearnedRule
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = projectId,
            MergeRequestId = mergeRequestId,
            AstPattern = "method_call",
            RuleDescription = description,
            BadPattern = "bad",
            GoodPattern = "good",
            Embedding = new float[] { 1.0f, 0.0f },
            ConfidenceScore = 5,
            LastHitAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
    }

    // -------------------------------------------------------------------------
    // Test 1 — Rules are stored separately per project
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task RulesAreStoredSeparatelyPerProject()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();

        Project projA = await projRepo.GetOrCreateAsync("ext:A", "Project A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:B", "Project B", null);

        await repo.InsertAsync(MakeRule(projA.Id, "mr-a", "Rule from A"));
        await repo.InsertAsync(MakeRule(projB.Id, "mr-b", "Rule from B"));

        List<LearnedRule> rulesA = await repo.GetAllActiveAsync(projA.Id);
        List<LearnedRule> rulesB = await repo.GetAllActiveAsync(projB.Id);

        Assert.AreEqual(1, rulesA.Count, "Project A should have exactly 1 rule");
        Assert.AreEqual("Rule from A", rulesA[0].RuleDescription);

        Assert.AreEqual(1, rulesB.Count, "Project B should have exactly 1 rule");
        Assert.AreEqual("Rule from B", rulesB[0].RuleDescription);
    }

    // -------------------------------------------------------------------------
    // Test 2 — Confidence increment is isolated to the target project
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ConfidenceIncrementIsIsolated()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();

        Project projA = await projRepo.GetOrCreateAsync("ext:A2", "Project A2", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:B2", "Project B2", null);

        string sharedMrId = "shared-mr";
        LearnedRule ruleA = MakeRule(projA.Id, sharedMrId);
        LearnedRule ruleB = MakeRule(projB.Id, sharedMrId);

        await repo.InsertAsync(ruleA);
        await repo.InsertAsync(ruleB);

        await repo.IncrementConfidenceByChunkIdAsync(projA.Id, ruleA.Id);

        List<LearnedRule> rulesA = await repo.GetAllActiveAsync(projA.Id);
        List<LearnedRule> rulesB = await repo.GetAllActiveAsync(projB.Id);

        Assert.AreEqual(6, rulesA[0].ConfidenceScore, "Project A confidence should be incremented");
        Assert.AreEqual(5, rulesB[0].ConfidenceScore, "Project B confidence should remain unchanged");
    }

    // -------------------------------------------------------------------------
    // Test 3 — Confidence decrement is isolated to the target project
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ConfidenceDecrementIsIsolated()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();

        Project projA = await projRepo.GetOrCreateAsync("ext:A3", "Project A3", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:B3", "Project B3", null);

        string sharedMrId = "shared-mr-3";
        LearnedRule ruleA = MakeRule(projA.Id, sharedMrId);
        LearnedRule ruleB = MakeRule(projB.Id, sharedMrId);

        await repo.InsertAsync(ruleA);
        await repo.InsertAsync(ruleB);

        await repo.DecrementConfidenceByChunkIdAsync(projA.Id, ruleA.Id);

        List<LearnedRule> rulesA = await repo.GetAllActiveAsync(projA.Id);
        List<LearnedRule> rulesB = await repo.GetAllActiveAsync(projB.Id);

        Assert.AreEqual(4, rulesA[0].ConfidenceScore, "Project A confidence should be decremented");
        Assert.AreEqual(5, rulesB[0].ConfidenceScore, "Project B confidence should remain unchanged");
    }

    // -------------------------------------------------------------------------
    // Test 4 — GetAllActive only returns the requested project's rules
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task GetAllActiveReturnsOnlyProjectRules()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();

        Project projA = await projRepo.GetOrCreateAsync("ext:A4", "Project A4", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:B4", "Project B4", null);

        for (int i = 0; i < 3; i++)
            await repo.InsertAsync(MakeRule(projA.Id, $"mr-a-{i}"));
        for (int i = 0; i < 2; i++)
            await repo.InsertAsync(MakeRule(projB.Id, $"mr-b-{i}"));

        List<LearnedRule> rulesA = await repo.GetAllActiveAsync(projA.Id);
        List<LearnedRule> rulesB = await repo.GetAllActiveAsync(projB.Id);

        Assert.AreEqual(3, rulesA.Count);
        Assert.IsTrue(rulesA.All(r => r.ProjectId == projA.Id), "All returned rules must belong to project A");

        Assert.AreEqual(2, rulesB.Count);
        Assert.IsTrue(rulesB.All(r => r.ProjectId == projB.Id), "All returned rules must belong to project B");
    }

    // -------------------------------------------------------------------------
    // Test 5 — Merge event confidence update is isolated to one project
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task MergeEventConfidenceUpdateIsIsolated()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();

        Project projA = await projRepo.GetOrCreateAsync("ext:A5", "Project A5", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:B5", "Project B5", null);

        LearnedRule ruleA = MakeRule(projA.Id, "mr-a5", "Avoid badCode");
        LearnedRule ruleB = MakeRule(projB.Id, "mr-b5", "Avoid badCode");
        ruleA.BadPattern = "badCode";
        ruleB.BadPattern = "badCode";

        await repo.InsertAsync(ruleA);
        await repo.InsertAsync(ruleB);

        string prKey = "gitlab/100/1";
        await repo.InsertPendingReviewAsync(prKey, ruleA.Id, ruleA.BadPattern);

        RuleLifecycleService lifecycle = new RuleLifecycleService(repo);
        await lifecycle.OnPrMergedAsync(prKey, "// clean code here", projA.Id);

        List<LearnedRule> rulesA = await repo.GetAllActiveAsync(projA.Id);
        List<LearnedRule> rulesB = await repo.GetAllActiveAsync(projB.Id);

        Assert.AreEqual(6, rulesA[0].ConfidenceScore, "Project A rule confidence should increase after clean merge");
        Assert.AreEqual(5, rulesB[0].ConfidenceScore, "Project B rule confidence must remain unchanged");
    }

    // -------------------------------------------------------------------------
    // Test 6 — Equivalent rule IDs can coexist across projects
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EquivalentRuleIdsCanCoexistAcrossProjects()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();

        Project projA = await projRepo.GetOrCreateAsync("ext:A6", "Project A6", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:B6", "Project B6", null);

        string sharedMrId = "rule-123";
        LearnedRule ruleA = MakeRule(projA.Id, sharedMrId, "Rule A");
        LearnedRule ruleB = MakeRule(projB.Id, sharedMrId, "Rule B");
        await repo.InsertAsync(ruleA);
        await repo.InsertAsync(ruleB);

        List<LearnedRule> rulesA = await repo.GetAllActiveAsync(projA.Id);
        List<LearnedRule> rulesB = await repo.GetAllActiveAsync(projB.Id);

        Assert.AreEqual(1, rulesA.Count);
        Assert.AreEqual(1, rulesB.Count);
        Assert.AreEqual("Rule A", rulesA[0].RuleDescription);
        Assert.AreEqual("Rule B", rulesB[0].RuleDescription);
    }

    // -------------------------------------------------------------------------
    // Test 7 — Legacy migration: existing rules without project_id get assigned
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task LegacyMigrationAssignsExistingRulesToLegacyProject()
    {
        // Simulate a pre-Phase-1 database that has learned_rules without project_id
        string dbName = $"testdb_migration_{Guid.NewGuid():N}";
        string cs = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        using SqliteConnection setupConn = new SqliteConnection(cs);
        await setupConn.OpenAsync();

        // Create old-style schema (no project_id, no projects table)
        await using (SqliteCommand cmd = setupConn.CreateCommand())
        {
            cmd.CommandText = @"
            CREATE TABLE learned_rules (
                id TEXT PRIMARY KEY,
                merge_request_id TEXT NOT NULL,
                ast_pattern TEXT NOT NULL,
                rule_description TEXT NOT NULL,
                bad_pattern TEXT,
                good_pattern TEXT,
                embedding BLOB NOT NULL,
                confidence_score INTEGER NOT NULL DEFAULT 5,
                last_hit_at TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE pending_reviews (
                pr_key TEXT NOT NULL,
                rule_id TEXT NOT NULL,
                bad_pattern TEXT,
                PRIMARY KEY (pr_key, rule_id)
            );";
            await cmd.ExecuteNonQueryAsync();
        }

        string now = DateTime.UtcNow.ToString("O");
        await using (SqliteCommand cmd = setupConn.CreateCommand())
        {
            cmd.CommandText = @"
            INSERT INTO learned_rules (id, merge_request_id, ast_pattern, rule_description, bad_pattern, good_pattern, embedding, confidence_score, last_hit_at, created_at)
            VALUES (@id, @mr, @ap, @rd, NULL, NULL, @emb, 5, @now, @now)";
            cmd.Parameters.AddWithValue("@id", "legacy-rule-1");
            cmd.Parameters.AddWithValue("@mr", "old-mr-id");
            cmd.Parameters.AddWithValue("@ap", "some_pattern");
            cmd.Parameters.AddWithValue("@rd", "Old rule");
            cmd.Parameters.AddWithValue("@emb", new byte[8]);
            cmd.Parameters.AddWithValue("@now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        // Run InitializeAsync to trigger migration (must keep setupConn open for shared in-memory DB)
        RuleRepository repo = new RuleRepository(cs, isConnectionString: true);
        ProjectRepository projRepo = new ProjectRepository(cs, isConnectionString: true);
        await repo.InitializeAsync();

        // Verify legacy project was created
        Project legacy = await projRepo.GetOrCreateAsync(
            ProjectRepository.LegacyExternalProjectId,
            ProjectRepository.LegacyName,
            null);

        // Verify the existing rule is now under the legacy project and not lost
        List<LearnedRule> legacyRules = await repo.GetAllActiveAsync(legacy.Id);

        Assert.AreEqual(1, legacyRules.Count, "Migrated rule must not be lost");
        Assert.AreEqual("legacy-rule-1", legacyRules[0].Id);
        Assert.AreEqual(legacy.Id, legacyRules[0].ProjectId, "Migrated rule must reference the legacy project");
    }

    // -------------------------------------------------------------------------
    // Test 8 — Single-project behavior continues to work
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SingleProjectWorkflowContinuesToWork()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();

        Project project = await projRepo.GetOrCreateAsync("ext:single", "My Project", null);

        LearnedRule rule = MakeRule(project.Id, "mr-single", "Use CancellationToken");
        rule.BadPattern = "oldPattern";
        await repo.InsertAsync(rule);

        string prKey = "gitlab/999/42";
        await repo.InsertPendingReviewAsync(prKey, rule.Id, rule.BadPattern);

        RuleLifecycleService lifecycle = new RuleLifecycleService(repo);
        await lifecycle.OnPrMergedAsync(prKey, "// fixed code", project.Id);

        List<LearnedRule> rules = await repo.GetAllActiveAsync(project.Id);
        Assert.AreEqual(1, rules.Count);
        Assert.AreEqual(6, rules[0].ConfidenceScore, "Confidence should have been incremented");

        List<(string RuleId, string? BadPattern)> pending = await repo.GetPendingReviewsAsync(prKey);
        Assert.AreEqual(0, pending.Count, "Pending reviews must be cleared after merge");
    }

    // -------------------------------------------------------------------------
    // Test — GetOrCreateAsync is idempotent
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task GetOrCreateProjectIsIdempotent()
    {
        (_, ProjectRepository projRepo) = await CreateRepositoriesAsync();

        Project first = await projRepo.GetOrCreateAsync("ext:idem", "Idempotent Project", null);
        Project second = await projRepo.GetOrCreateAsync("ext:idem", "Idempotent Project", null);

        Assert.AreEqual(first.Id, second.Id, "Same external ID must return same internal project");
    }
}
