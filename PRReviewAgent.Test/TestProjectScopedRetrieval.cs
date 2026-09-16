using PRReviewAgent.Services.AutoImprove;

namespace PRReviewAgent.Test;

/// <summary>
/// Phase 5 — Project-scoped rule retrieval isolation tests.
/// Verifies that learned-rule SQL filtering, vector-search input, top-k selection,
/// and prompt construction are all strictly project-local.
/// </summary>
[TestClass]
public class TestProjectScopedRetrieval
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<(RuleRepository repo, ProjectRepository projRepo)> CreateRepositoriesAsync()
    {
        string dbName = $"testdb_p5_{Guid.NewGuid():N}";
        string cs = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        RuleRepository repo = new RuleRepository(cs, isConnectionString: true);
        ProjectRepository projRepo = new ProjectRepository(cs, isConnectionString: true);
        await repo.InitializeAsync();
        return (repo, projRepo);
    }

    private static LearnedRule MakeRule(
        long projectId,
        string mergeRequestId,
        string description = "test rule",
        float[] embedding = null!,
        int confidence = 5)
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
            Embedding = embedding ?? new float[] { 1.0f, 0.0f },
            ConfidenceScore = confidence,
            LastHitAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
    }

    // -------------------------------------------------------------------------
    // Test 1 — Project A retrieval excludes Project B
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ProjectA_Retrieval_ExcludesProjectB()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:5a", "Project A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:5b", "Project B", null);

        await repo.InsertAsync(MakeRule(projA.Id, "mr-a1", "Rule A1"));
        await repo.InsertAsync(MakeRule(projA.Id, "mr-a2", "Rule A2"));
        await repo.InsertAsync(MakeRule(projB.Id, "mr-b1", "Rule B1"));
        await repo.InsertAsync(MakeRule(projB.Id, "mr-b2", "Rule B2"));

        List<LearnedRule> rules = await repo.GetAllActiveAsync(projA.Id);

        Assert.AreEqual(2, rules.Count);
        Assert.IsTrue(rules.All(r => r.ProjectId == projA.Id), "All returned rules must belong to Project A");
        Assert.IsFalse(rules.Any(r => r.RuleDescription.Contains("B")), "No Project B rules in Project A results");
    }

    // -------------------------------------------------------------------------
    // Test 2 — Project B retrieval excludes Project A
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ProjectB_Retrieval_ExcludesProjectA()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:5c", "Project A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:5d", "Project B", null);

        await repo.InsertAsync(MakeRule(projA.Id, "mr-a", "Rule A"));
        await repo.InsertAsync(MakeRule(projB.Id, "mr-b1", "Rule B1"));
        await repo.InsertAsync(MakeRule(projB.Id, "mr-b2", "Rule B2"));

        List<LearnedRule> rules = await repo.GetAllActiveAsync(projB.Id);

        Assert.AreEqual(2, rules.Count);
        Assert.IsTrue(rules.All(r => r.ProjectId == projB.Id), "All returned rules must belong to Project B");
        Assert.IsFalse(rules.Any(r => r.RuleDescription.Contains("A")), "No Project A rules in Project B results");
    }

    // -------------------------------------------------------------------------
    // Test 3 — Vector search input is project-local (repository boundary)
    // GetAllActiveAsync is the direct input to the vector-comparison loop in
    // RuleRetrievalService.  Verifying its output is project-local guarantees
    // that the vector comparison only sees the correct project's rules.
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task VectorSearchInput_IsProjectLocal()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:5e", "A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:5f", "B", null);

        await repo.InsertAsync(MakeRule(projA.Id, "mr-va", "A rule", new float[] { 0.9f, 0.1f }));
        await repo.InsertAsync(MakeRule(projB.Id, "mr-vb", "B rule", new float[] { 0.8f, 0.2f }));

        List<LearnedRule> vectorInput = await repo.GetAllActiveAsync(projA.Id);

        // Every rule fed into the vector comparison belongs to project A
        Assert.IsTrue(vectorInput.All(r => r.ProjectId == projA.Id),
            "Vector search input must contain only Project A rules");
        Assert.AreEqual(0, vectorInput.Count(r => r.ProjectId == projB.Id),
            "Project B rules must not enter the vector comparison for Project A");
    }

    // -------------------------------------------------------------------------
    // Test 4 — Top-K does not consider other projects
    // Highly similar rules from Project B must not appear in Project A results
    // because B's rules are never loaded when retrieving for A.
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task TopK_DoesNotConsiderOtherProjects()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:5g", "A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:5h", "B", null);

        // Project B has rules with embeddings identical to a hypothetical query
        float[] perfectMatch = new float[] { 1.0f, 0.0f };
        await repo.InsertAsync(MakeRule(projB.Id, "mr-b-topk1", "B perfect 1", perfectMatch));
        await repo.InsertAsync(MakeRule(projB.Id, "mr-b-topk2", "B perfect 2", perfectMatch));
        await repo.InsertAsync(MakeRule(projB.Id, "mr-b-topk3", "B perfect 3", perfectMatch));

        // Project A has weaker rules
        await repo.InsertAsync(MakeRule(projA.Id, "mr-a-topk", "A weaker rule", new float[] { 0.5f, 0.5f }, confidence: 3));

        // Top-K for Project A can only consider Project A's rules
        List<LearnedRule> candidates = await repo.GetAllActiveAsync(projA.Id);

        Assert.AreEqual(1, candidates.Count, "Only Project A's rule is in the candidate set");
        Assert.AreEqual(projA.Id, candidates[0].ProjectId);
        Assert.IsFalse(candidates.Any(r => r.ProjectId == projB.Id), "Project B rules never enter A's top-k candidate set");
    }

    // -------------------------------------------------------------------------
    // Test 5 — Prompt contains only current project rules
    // FormatRulesForPrompt is called with the already project-filtered list.
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Prompt_ContainsOnlyCurrentProjectRules()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:5i", "A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:5j", "B", null);

        await repo.InsertAsync(MakeRule(projA.Id, "mr-pa", "UseNullCoalescing"));
        await repo.InsertAsync(MakeRule(projB.Id, "mr-pb", "AvoidGlobalState"));

        List<LearnedRule> projARules = await repo.GetAllActiveAsync(projA.Id);
        string prompt = RuleRetrievalService.FormatRulesForPrompt(projARules, "csharp");

        StringAssert.Contains(prompt, "UseNullCoalescing");
        Assert.IsFalse(prompt.Contains("AvoidGlobalState"),
            "Prompt for Project A must not contain Project B rule text");
    }

    // -------------------------------------------------------------------------
    // Test 6 — Project with no rules returns empty, no fallback to other projects
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task NoRulesForProject_ReturnsEmpty_NoFallback()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:5k", "A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:5l", "B", null);

        // Only Project B has rules
        await repo.InsertAsync(MakeRule(projB.Id, "mr-b-fallback", "B fallback rule"));

        List<LearnedRule> projARules = await repo.GetAllActiveAsync(projA.Id);

        Assert.AreEqual(0, projARules.Count, "Project A has no rules — result must be empty");
    }

    // -------------------------------------------------------------------------
    // Test 7 — Same rule ID across projects remains isolated
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SameRuleId_AcrossProjects_RemainsIsolated()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:5m", "A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:5n", "B", null);

        string sharedId = "rule-123";

        LearnedRule ruleA = new LearnedRule
        {
            Id = sharedId + "-a",
            ProjectId = projA.Id,
            MergeRequestId = "mr-shared-a",
            AstPattern = "p",
            RuleDescription = "A rule with shared id prefix",
            Embedding = new float[] { 1.0f, 0.0f },
            ConfidenceScore = 5,
            LastHitAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        LearnedRule ruleB = new LearnedRule
        {
            Id = sharedId + "-b",
            ProjectId = projB.Id,
            MergeRequestId = "mr-shared-b",
            AstPattern = "p",
            RuleDescription = "B rule with shared id prefix",
            Embedding = new float[] { 1.0f, 0.0f },
            ConfidenceScore = 5,
            LastHitAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };

        await repo.InsertAsync(ruleA);
        await repo.InsertAsync(ruleB);

        List<LearnedRule> projAResult = await repo.GetAllActiveAsync(projA.Id);

        Assert.AreEqual(1, projAResult.Count);
        Assert.AreEqual(ruleA.Id, projAResult[0].Id);
        Assert.AreEqual(projA.Id, projAResult[0].ProjectId);
    }

    // -------------------------------------------------------------------------
    // Test 8 — Equivalent rule text across projects stays independent
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EquivalentRuleText_AcrossProjects_IsIndependent()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:5o", "A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:5p", "B", null);

        const string sharedDescription = "Use CancellationToken in async methods";

        await repo.InsertAsync(MakeRule(projA.Id, "mr-ctA", sharedDescription));
        await repo.InsertAsync(MakeRule(projB.Id, "mr-ctB", sharedDescription));

        List<LearnedRule> projAResult = await repo.GetAllActiveAsync(projA.Id);
        List<LearnedRule> projBResult = await repo.GetAllActiveAsync(projB.Id);

        Assert.AreEqual(1, projAResult.Count);
        Assert.AreEqual(projA.Id, projAResult[0].ProjectId,
            "Even with identical text, Project A's rule must belong to Project A");

        Assert.AreEqual(1, projBResult.Count);
        Assert.AreEqual(projB.Id, projBResult[0].ProjectId,
            "Project B's copy of the rule belongs to Project B only");
    }

    // -------------------------------------------------------------------------
    // Test 9 — Follow-up UpdateLastHit is project-scoped
    // Verifies that updating last_hit_at for a given MR only touches the
    // correct project's rules, not all projects sharing the same MR id string.
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task FollowUpLastHitUpdate_IsProjectScoped()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:5q", "A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:5r", "B", null);

        // Both projects have a rule with the same MR id (cross-project collision)
        const string sharedMrId = "mr-overlap-99";
        string oldDate = DateTime.UtcNow.AddDays(-30).ToString("O");
        string newDate = DateTime.UtcNow.ToString("O");

        LearnedRule ruleA = MakeRule(projA.Id, sharedMrId, "A rule");
        LearnedRule ruleB = MakeRule(projB.Id, sharedMrId, "B rule");
        ruleA.LastHitAt = DateTime.UtcNow.AddDays(-30);
        ruleB.LastHitAt = DateTime.UtcNow.AddDays(-30);
        await repo.InsertAsync(ruleA);
        await repo.InsertAsync(ruleB);

        // Update only Project A's rules for that MR
        await repo.UpdateLastHitByMergeRequestIdAsync(projA.Id, sharedMrId, newDate);

        List<LearnedRule> projARules = await repo.GetAllActiveAsync(projA.Id);
        List<LearnedRule> projBRules = await repo.GetAllActiveAsync(projB.Id);

        // Project A's rule should have the new timestamp
        Assert.IsTrue(projARules[0].LastHitAt > DateTime.UtcNow.AddDays(-1),
            "Project A's last_hit_at should be updated");

        // Project B's rule must NOT have been updated
        Assert.IsTrue(projBRules[0].LastHitAt < DateTime.UtcNow.AddDays(-1),
            "Project B's last_hit_at must remain unchanged when updating Project A");
    }

    // -------------------------------------------------------------------------
    // Test 10 — Concurrent project reviews produce no cross-project contamination
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ConcurrentProjectReviews_NoContamination()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:5s", "A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:5t", "B", null);

        for (int i = 0; i < 5; i++)
        {
            await repo.InsertAsync(MakeRule(projA.Id, $"mr-cA{i}", $"A rule {i}"));
            await repo.InsertAsync(MakeRule(projB.Id, $"mr-cB{i}", $"B rule {i}"));
        }

        // Retrieve for both projects concurrently
        Task<List<LearnedRule>> taskA = repo.GetAllActiveAsync(projA.Id);
        Task<List<LearnedRule>> taskB = repo.GetAllActiveAsync(projB.Id);
        await Task.WhenAll(taskA, taskB);

        List<LearnedRule> rulesA = taskA.Result;
        List<LearnedRule> rulesB = taskB.Result;

        Assert.IsTrue(rulesA.All(r => r.ProjectId == projA.Id), "Concurrent A result contains only A rules");
        Assert.IsTrue(rulesB.All(r => r.ProjectId == projB.Id), "Concurrent B result contains only B rules");
        Assert.AreEqual(5, rulesA.Count);
        Assert.AreEqual(5, rulesB.Count);
    }

    // -------------------------------------------------------------------------
    // Test 11 — Legacy project is isolated from new projects
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task LegacyProject_IsIsolatedFromNewProjects()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();

        // The legacy project is auto-created by InitializeAsync; retrieve it
        Project legacy = await projRepo.GetOrCreateAsync(ProjectRepository.LegacyExternalProjectId, ProjectRepository.LegacyName, null);
        Project newProj = await projRepo.GetOrCreateAsync("ext:5u", "New Project", null);

        await repo.InsertAsync(MakeRule(legacy.Id, "mr-legacy", "Legacy rule"));
        await repo.InsertAsync(MakeRule(newProj.Id, "mr-new", "New project rule"));

        List<LearnedRule> newProjRules = await repo.GetAllActiveAsync(newProj.Id);

        Assert.AreEqual(1, newProjRules.Count);
        Assert.AreEqual(newProj.Id, newProjRules[0].ProjectId,
            "New project review must not receive legacy project rules");
        Assert.IsFalse(newProjRules.Any(r => r.RuleDescription.Contains("Legacy")),
            "Legacy rule text must not appear in new project results");
    }

    // -------------------------------------------------------------------------
    // Test 12 — Within-project ranking order is preserved
    // Rules with higher cosine similarity to a query must rank above lower ones.
    // We verify this at the SQL retrieval layer by confirming that the returned
    // set is exactly the project's rules; ranking logic in the service is unchanged.
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task WithinProject_RankingBehavior_IsPreserved()
    {
        (RuleRepository repo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:5v", "Rank Project", null);

        // Insert rules with distinct embeddings: cosine similarity against [1,0]
        // rule-high: [1,0] => similarity = 1.0  (highest)
        // rule-mid:  [0.7,0.7] normalized is [0.707,0.707] => sim ≈ 0.707
        // rule-low:  [0,1] => similarity = 0.0  (lowest)
        float[] embHigh = new float[] { 1.0f, 0.0f };
        float[] embMid = new float[] { 0.707f, 0.707f };
        float[] embLow = new float[] { 0.0f, 1.0f };

        await repo.InsertAsync(MakeRule(proj.Id, "mr-rank-h", "High similarity rule", embHigh));
        await repo.InsertAsync(MakeRule(proj.Id, "mr-rank-m", "Mid similarity rule", embMid));
        await repo.InsertAsync(MakeRule(proj.Id, "mr-rank-l", "Low similarity rule", embLow));

        // SQL layer returns all 3 — ranking happens in service layer
        List<LearnedRule> allProjectRules = await repo.GetAllActiveAsync(proj.Id);

        Assert.AreEqual(3, allProjectRules.Count, "All 3 project rules must be available for ranking");
        Assert.IsTrue(allProjectRules.All(r => r.ProjectId == proj.Id), "All rules belong to the same project");

        // Simulate the ranking step that the service performs
        float[] query = new float[] { 1.0f, 0.0f };
        List<(LearnedRule rule, float score)> ranked = allProjectRules
            .Select(r => (r, EmbeddingUtils.CosineSimilarity(query, r.Embedding)))
            .OrderByDescending(x => x.Item2)
            .ToList();

        Assert.AreEqual("High similarity rule", ranked[0].rule.RuleDescription);
        Assert.IsTrue(ranked[0].Item2 > ranked[1].Item2, "Highest similarity rule ranks first");
        Assert.IsTrue(ranked[1].Item2 > ranked[2].Item2, "Mid similarity rule ranks second");
    }
}
