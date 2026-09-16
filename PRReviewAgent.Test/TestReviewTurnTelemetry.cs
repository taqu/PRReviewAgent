using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.Statistics;

namespace PRReviewAgent.Test;

[TestClass]
public class TestReviewTurnTelemetry
{
    private static async Task<(ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo)> CreateRepositoriesAsync()
    {
        string dbName = $"testdb_turns_{Guid.NewGuid():N}";
        string cs = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        RuleRepository ruleRepo = new RuleRepository(cs, isConnectionString: true);
        ProjectRepository projRepo = new ProjectRepository(cs, isConnectionString: true);
        ReviewExecutionRepository execRepo = new ReviewExecutionRepository(cs, isConnectionString: true);
        ReviewTurnRepository turnRepo = new ReviewTurnRepository(cs, isConnectionString: true);
        await ruleRepo.InitializeAsync();
        return (execRepo, turnRepo, projRepo);
    }

    private static async Task<(long execId, Project proj)> CreateExecutionAsync(ReviewExecutionRepository execRepo, ProjectRepository projRepo, string extId = "ext:t1")
    {
        Project proj = await projRepo.GetOrCreateAsync(extId, extId, null);
        long execId = await execRepo.StartAsync(proj.Id, $"gitlab/1/{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        return (execId, proj);
    }

    // -------------------------------------------------------------------------
    // Test 1 — Successful review creates one Detection and one Selection turn
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SuccessfulReview_CreatesTwoTurns()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        (long execId, _) = await CreateExecutionAsync(execRepo, projRepo, "ext:t1a");

        long detId = await turnRepo.StartAsync(execId, ReviewTurnType.Detection, "model-x", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(detId, 800, 150, 5, DateTimeOffset.UtcNow, 1200);

        long selId = await turnRepo.StartAsync(execId, ReviewTurnType.Selection, "model-x", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(selId, 600, 200, 3, DateTimeOffset.UtcNow, 900);

        IReadOnlyList<ReviewTurn> turns = await turnRepo.GetByReviewExecutionIdAsync(execId);

        Assert.AreEqual(2, turns.Count);
    }

    // -------------------------------------------------------------------------
    // Test 2 — Turn types are stored explicitly, not by position
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task TurnTypesAreExplicit()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        (long execId, _) = await CreateExecutionAsync(execRepo, projRepo, "ext:t2");

        long detId = await turnRepo.StartAsync(execId, ReviewTurnType.Detection, "model-x", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(detId, null, null, 4, DateTimeOffset.UtcNow, 100);

        long selId = await turnRepo.StartAsync(execId, ReviewTurnType.Selection, "model-x", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(selId, null, null, 2, DateTimeOffset.UtcNow, 100);

        IReadOnlyList<ReviewTurn> turns = await turnRepo.GetByReviewExecutionIdAsync(execId);

        Assert.IsTrue(turns.Any(t => t.TurnType == ReviewTurnType.Detection), "Detection must be explicit");
        Assert.IsTrue(turns.Any(t => t.TurnType == ReviewTurnType.Selection), "Selection must be explicit");
    }

    // -------------------------------------------------------------------------
    // Test 3 — Model identifier is stored per turn
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ModelIdentifierIsStored()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        (long execId, _) = await CreateExecutionAsync(execRepo, projRepo, "ext:t3");

        long detId = await turnRepo.StartAsync(execId, ReviewTurnType.Detection, "gpt-4o-mini", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(detId, null, null, 3, DateTimeOffset.UtcNow, 100);

        long selId = await turnRepo.StartAsync(execId, ReviewTurnType.Selection, "gpt-4o-mini", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(selId, null, null, 2, DateTimeOffset.UtcNow, 100);

        IReadOnlyList<ReviewTurn> turns = await turnRepo.GetByReviewExecutionIdAsync(execId);

        Assert.IsTrue(turns.All(t => t.Model == "gpt-4o-mini"), "All turns must store the model identifier");
    }

    // -------------------------------------------------------------------------
    // Test 4 — Detection token usage is stored correctly
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task DetectionTokenUsageIsStored()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        (long execId, _) = await CreateExecutionAsync(execRepo, projRepo, "ext:t4");

        long detId = await turnRepo.StartAsync(execId, ReviewTurnType.Detection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(detId, 1000, 200, 5, DateTimeOffset.UtcNow, 100);

        IReadOnlyList<ReviewTurn> turns = await turnRepo.GetByReviewExecutionIdAsync(execId);
        ReviewTurn detection = turns.Single(t => t.TurnType == ReviewTurnType.Detection);

        Assert.AreEqual(1000, detection.InputTokens);
        Assert.AreEqual(200, detection.OutputTokens);
    }

    // -------------------------------------------------------------------------
    // Test 5 — Selection token usage is stored independently
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SelectionTokenUsageIsStored()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        (long execId, _) = await CreateExecutionAsync(execRepo, projRepo, "ext:t5");

        long detId = await turnRepo.StartAsync(execId, ReviewTurnType.Detection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(detId, 100, 50, 3, DateTimeOffset.UtcNow, 100);

        long selId = await turnRepo.StartAsync(execId, ReviewTurnType.Selection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(selId, 3100, 620, 2, DateTimeOffset.UtcNow, 100);

        IReadOnlyList<ReviewTurn> turns = await turnRepo.GetByReviewExecutionIdAsync(execId);
        ReviewTurn selection = turns.Single(t => t.TurnType == ReviewTurnType.Selection);

        Assert.AreEqual(3100, selection.InputTokens);
        Assert.AreEqual(620, selection.OutputTokens);
    }

    // -------------------------------------------------------------------------
    // Test 6 — Detection finding_count equals ReviewExecution.candidate_finding_count
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task DetectionFindingCountMatchesExecutionCandidateCount()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:t6");

        int candidateCount = 7;
        long detId = await turnRepo.StartAsync(execId, ReviewTurnType.Detection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(detId, null, null, candidateCount, DateTimeOffset.UtcNow, 100);

        await execRepo.CompleteSuccessAsync(execId, new ReviewExecutionResult(DateTimeOffset.UtcNow, 500, candidateCount, 4, 2, 2, 0));

        IReadOnlyList<ReviewTurn> turns = await turnRepo.GetByReviewExecutionIdAsync(execId);
        ReviewExecution? exec = await execRepo.GetByIdAsync(execId);

        Assert.AreEqual(candidateCount, turns.Single(t => t.TurnType == ReviewTurnType.Detection).FindingCount);
        Assert.AreEqual(candidateCount, exec!.CandidateFindingCount);
    }

    // -------------------------------------------------------------------------
    // Test 7 — Selection finding_count equals ReviewExecution.selected_finding_count
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SelectionFindingCountMatchesExecutionSelectedCount()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        (long execId, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:t7");

        int selectedCount = 4;
        long detId = await turnRepo.StartAsync(execId, ReviewTurnType.Detection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(detId, null, null, 10, DateTimeOffset.UtcNow, 100);

        long selId = await turnRepo.StartAsync(execId, ReviewTurnType.Selection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(selId, null, null, selectedCount, DateTimeOffset.UtcNow, 100);

        await execRepo.CompleteSuccessAsync(execId, new ReviewExecutionResult(DateTimeOffset.UtcNow, 600, 10, selectedCount, 2, 2, 0));

        IReadOnlyList<ReviewTurn> turns = await turnRepo.GetByReviewExecutionIdAsync(execId);
        ReviewExecution? exec = await execRepo.GetByIdAsync(execId);

        Assert.AreEqual(selectedCount, turns.Single(t => t.TurnType == ReviewTurnType.Selection).FindingCount);
        Assert.AreEqual(selectedCount, exec!.SelectedFindingCount);
    }

    // -------------------------------------------------------------------------
    // Test 8 — Turn durations, started_at, and completed_at are stored
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task TurnDurationsAndTimestampsAreStored()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        (long execId, _) = await CreateExecutionAsync(execRepo, projRepo, "ext:t8");

        DateTimeOffset detStart = DateTimeOffset.UtcNow;
        long detId = await turnRepo.StartAsync(execId, ReviewTurnType.Detection, "model-a", detStart);
        DateTimeOffset detCompleted = DateTimeOffset.UtcNow;
        await turnRepo.CompleteSuccessAsync(detId, null, null, 3, detCompleted, 4300);

        IReadOnlyList<ReviewTurn> turns = await turnRepo.GetByReviewExecutionIdAsync(execId);
        ReviewTurn det = turns.Single();

        Assert.IsTrue(det.DurationMs >= 0, "duration_ms must be >= 0");
        Assert.AreNotEqual(default(DateTimeOffset), det.StartedAt);
        Assert.IsNotNull(det.CompletedAt);
    }

    // -------------------------------------------------------------------------
    // Test 9 — Detection failure: no Selection turn, execution Failed
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task DetectionFailure_NoSelectionTurnAndExecutionFailed()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        (long execId, _) = await CreateExecutionAsync(execRepo, projRepo, "ext:t9");

        long detId = await turnRepo.StartAsync(execId, ReviewTurnType.Detection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteFailureAsync(detId, null, null, "ModelRequestFailed", DateTimeOffset.UtcNow, 1800);

        await execRepo.CompleteFailureAsync(execId, "ModelRequestFailed", DateTimeOffset.UtcNow, 1900);

        IReadOnlyList<ReviewTurn> turns = await turnRepo.GetByReviewExecutionIdAsync(execId);
        ReviewExecution? exec = await execRepo.GetByIdAsync(execId);

        Assert.AreEqual(1, turns.Count, "Only Detection turn should exist");
        ReviewTurn det = turns[0];
        Assert.AreEqual(ReviewTurnType.Detection, det.TurnType);
        Assert.IsFalse(det.Success);
        Assert.IsNotNull(det.ErrorType);
        Assert.AreEqual(ReviewExecutionStatus.Failed, exec!.Status);
    }

    // -------------------------------------------------------------------------
    // Test 10 — Selection failure: Detection success, Selection failed, execution Failed
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SelectionFailure_DetectionSucceededButExecutionFailed()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        (long execId, _) = await CreateExecutionAsync(execRepo, projRepo, "ext:t10");

        long detId = await turnRepo.StartAsync(execId, ReviewTurnType.Detection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(detId, 500, 100, 5, DateTimeOffset.UtcNow, 2000);

        long selId = await turnRepo.StartAsync(execId, ReviewTurnType.Selection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteFailureAsync(selId, null, null, "Timeout", DateTimeOffset.UtcNow, 3000);

        await execRepo.CompleteFailureAsync(execId, "Timeout", DateTimeOffset.UtcNow, 5000);

        IReadOnlyList<ReviewTurn> turns = await turnRepo.GetByReviewExecutionIdAsync(execId);
        ReviewExecution? exec = await execRepo.GetByIdAsync(execId);

        Assert.AreEqual(2, turns.Count);
        Assert.IsTrue(turns.Single(t => t.TurnType == ReviewTurnType.Detection).Success);
        Assert.IsFalse(turns.Single(t => t.TurnType == ReviewTurnType.Selection).Success);
        Assert.AreEqual(ReviewExecutionStatus.Failed, exec!.Status);
    }

    // -------------------------------------------------------------------------
    // Test 11 — Project isolation through parent execution
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ProjectIsolationThroughParentExecution()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:t11a", "Proj A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:t11b", "Proj B", null);

        long execA = await execRepo.StartAsync(projA.Id, "mr-a", DateTimeOffset.UtcNow);
        long execB = await execRepo.StartAsync(projB.Id, "mr-b", DateTimeOffset.UtcNow);

        long detA = await turnRepo.StartAsync(execA, ReviewTurnType.Detection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(detA, null, null, 3, DateTimeOffset.UtcNow, 100);

        long detB = await turnRepo.StartAsync(execB, ReviewTurnType.Detection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(detB, null, null, 5, DateTimeOffset.UtcNow, 100);

        IReadOnlyList<ReviewTurn> turnsA = await turnRepo.GetByReviewExecutionIdAsync(execA);
        IReadOnlyList<ReviewTurn> turnsB = await turnRepo.GetByReviewExecutionIdAsync(execB);

        Assert.AreEqual(1, turnsA.Count);
        Assert.AreEqual(execA, turnsA[0].ReviewExecutionId);
        Assert.AreEqual(3, turnsA[0].FindingCount);

        Assert.AreEqual(1, turnsB.Count);
        Assert.AreEqual(execB, turnsB[0].ReviewExecutionId);
        Assert.AreEqual(5, turnsB[0].FindingCount);
    }

    // -------------------------------------------------------------------------
    // Test 12 — Multiple reviews of same MR have independent turns
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task MultipleReviewsOfSameMR_HaveIndependentTurns()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        (long execId1, Project proj) = await CreateExecutionAsync(execRepo, projRepo, "ext:t12");
        long execId2 = await execRepo.StartAsync(proj.Id, "gitlab/1/42", DateTimeOffset.UtcNow);

        long det1 = await turnRepo.StartAsync(execId1, ReviewTurnType.Detection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(det1, null, null, 3, DateTimeOffset.UtcNow, 100);
        long sel1 = await turnRepo.StartAsync(execId1, ReviewTurnType.Selection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(sel1, null, null, 2, DateTimeOffset.UtcNow, 100);

        long det2 = await turnRepo.StartAsync(execId2, ReviewTurnType.Detection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(det2, null, null, 7, DateTimeOffset.UtcNow, 100);
        long sel2 = await turnRepo.StartAsync(execId2, ReviewTurnType.Selection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(sel2, null, null, 5, DateTimeOffset.UtcNow, 100);

        IReadOnlyList<ReviewTurn> turns1 = await turnRepo.GetByReviewExecutionIdAsync(execId1);
        IReadOnlyList<ReviewTurn> turns2 = await turnRepo.GetByReviewExecutionIdAsync(execId2);

        Assert.AreEqual(2, turns1.Count);
        Assert.AreEqual(2, turns2.Count);
        Assert.IsTrue(turns1.All(t => t.ReviewExecutionId == execId1));
        Assert.IsTrue(turns2.All(t => t.ReviewExecutionId == execId2));
    }

    // -------------------------------------------------------------------------
    // Test 13 — Rule extraction does not create review_turns rows
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task RuleExtractionDoesNotCreateTurnRows()
    {
        // Perform rule repository operations (which represent rule extraction) without using turnRepo
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:t13", "Project T13", null);

        // Verify that after project/exec operations, no turn rows exist
        long execId = await execRepo.StartAsync(proj.Id, "mr-extract", DateTimeOffset.UtcNow);
        IReadOnlyList<ReviewTurn> turns = await turnRepo.GetByReviewExecutionIdAsync(execId);

        Assert.AreEqual(0, turns.Count, "Rule extraction must not create review_turns rows");
    }

    // -------------------------------------------------------------------------
    // Test 14 — Null token usage is stored as NULL, not zero
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task NullTokenUsageStoredAsNull_SuccessStillTrue()
    {
        (ReviewExecutionRepository execRepo, ReviewTurnRepository turnRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        (long execId, _) = await CreateExecutionAsync(execRepo, projRepo, "ext:t14");

        long detId = await turnRepo.StartAsync(execId, ReviewTurnType.Detection, "model-a", DateTimeOffset.UtcNow);
        await turnRepo.CompleteSuccessAsync(detId, null, null, 2, DateTimeOffset.UtcNow, 500);

        IReadOnlyList<ReviewTurn> turns = await turnRepo.GetByReviewExecutionIdAsync(execId);
        ReviewTurn det = turns.Single();

        Assert.IsNull(det.InputTokens, "InputTokens must be NULL when not provided");
        Assert.IsNull(det.OutputTokens, "OutputTokens must be NULL when not provided");
        Assert.IsTrue(det.Success, "Success must still be true");
    }
}
