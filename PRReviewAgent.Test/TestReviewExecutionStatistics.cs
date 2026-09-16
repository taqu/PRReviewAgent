using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.Statistics;

namespace PRReviewAgent.Test;

[TestClass]
public class TestReviewExecutionStatistics
{
    private static async Task<(ReviewExecutionRepository execRepo, ProjectRepository projRepo)> CreateRepositoriesAsync()
    {
        string dbName = $"testdb_stats_{Guid.NewGuid():N}";
        string cs = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        RuleRepository ruleRepo = new RuleRepository(cs, isConnectionString: true);
        ProjectRepository projRepo = new ProjectRepository(cs, isConnectionString: true);
        ReviewExecutionRepository execRepo = new ReviewExecutionRepository(cs, isConnectionString: true);
        await ruleRepo.InitializeAsync();
        return (execRepo, projRepo);
    }

    // -------------------------------------------------------------------------
    // Test 1 — StartAsync creates a Running record
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task StartAsync_CreatesRunningRecord()
    {
        (ReviewExecutionRepository execRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:s1", "Project S1", null);

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        long id = await execRepo.StartAsync(proj.Id, "gitlab/1/1", startedAt);

        ReviewExecution? exec = await execRepo.GetByIdAsync(id);

        Assert.IsNotNull(exec);
        Assert.AreEqual(ReviewExecutionStatus.Running, exec.Status);
        Assert.AreEqual(proj.Id, exec.ProjectId);
        Assert.AreEqual("gitlab/1/1", exec.MergeRequestId);
        Assert.IsNull(exec.CompletedAt);
        Assert.IsNull(exec.DurationMs);
        Assert.IsNull(exec.ErrorType);
    }

    // -------------------------------------------------------------------------
    // Test 2 — CompleteSuccessAsync sets Succeeded status with all counts
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task CompleteSuccessAsync_SetsSucceededWithCounts()
    {
        (ReviewExecutionRepository execRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:s2", "Project S2", null);

        long id = await execRepo.StartAsync(proj.Id, "gitlab/2/1", DateTimeOffset.UtcNow);
        ReviewExecutionResult result = new ReviewExecutionResult(
            CompletedAt: DateTimeOffset.UtcNow,
            DurationMs: 1234,
            CandidateFindingCount: 10,
            SelectedFindingCount: 6,
            CriticalCount: 2,
            MajorCount: 3,
            MinorCount: 1);
        await execRepo.CompleteSuccessAsync(id, result);

        ReviewExecution? exec = await execRepo.GetByIdAsync(id);

        Assert.IsNotNull(exec);
        Assert.AreEqual(ReviewExecutionStatus.Succeeded, exec.Status);
        Assert.AreEqual(1234L, exec.DurationMs);
        Assert.AreEqual(10, exec.CandidateFindingCount);
        Assert.AreEqual(6, exec.SelectedFindingCount);
        Assert.AreEqual(2, exec.CriticalCount);
        Assert.AreEqual(3, exec.MajorCount);
        Assert.AreEqual(1, exec.MinorCount);
        Assert.IsNull(exec.ErrorType);
    }

    // -------------------------------------------------------------------------
    // Test 3 — CompleteFailureAsync sets Failed status with error_type
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task CompleteFailureAsync_SetsFailedWithErrorType()
    {
        (ReviewExecutionRepository execRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:s3", "Project S3", null);

        long id = await execRepo.StartAsync(proj.Id, "gitlab/3/1", DateTimeOffset.UtcNow);
        await execRepo.CompleteFailureAsync(id, "TimeoutException", DateTimeOffset.UtcNow, 5000L);

        ReviewExecution? exec = await execRepo.GetByIdAsync(id);

        Assert.IsNotNull(exec);
        Assert.AreEqual(ReviewExecutionStatus.Failed, exec.Status);
        Assert.AreEqual("TimeoutException", exec.ErrorType);
        Assert.AreEqual(5000L, exec.DurationMs);
        Assert.IsNotNull(exec.CompletedAt);
        Assert.IsNull(exec.CandidateFindingCount);
        Assert.IsNull(exec.SelectedFindingCount);
    }

    // -------------------------------------------------------------------------
    // Test 4 — GetByIdAsync returns null for missing record
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task GetByIdAsync_ReturnsNullForMissingRecord()
    {
        (ReviewExecutionRepository execRepo, _) = await CreateRepositoriesAsync();

        ReviewExecution? exec = await execRepo.GetByIdAsync(99999);

        Assert.IsNull(exec);
    }

    // -------------------------------------------------------------------------
    // Test 5 — GetByProjectAsync returns only that project's records
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task GetByProjectAsync_ReturnsOnlyProjectRecords()
    {
        (ReviewExecutionRepository execRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:s5a", "Project S5A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:s5b", "Project S5B", null);

        await execRepo.StartAsync(projA.Id, "mr-a-1", DateTimeOffset.UtcNow);
        await execRepo.StartAsync(projA.Id, "mr-a-2", DateTimeOffset.UtcNow);
        await execRepo.StartAsync(projB.Id, "mr-b-1", DateTimeOffset.UtcNow);

        List<ReviewExecution> resultsA = await execRepo.GetByProjectAsync(projA.Id);
        List<ReviewExecution> resultsB = await execRepo.GetByProjectAsync(projB.Id);

        Assert.AreEqual(2, resultsA.Count);
        Assert.IsTrue(resultsA.All(r => r.ProjectId == projA.Id));
        Assert.AreEqual(1, resultsB.Count);
        Assert.AreEqual(projB.Id, resultsB[0].ProjectId);
    }

    // -------------------------------------------------------------------------
    // Test 6 — Projects are isolated: success in A does not affect B
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ProjectsAreIsolated_SuccessInADoesNotAffectB()
    {
        (ReviewExecutionRepository execRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:s6a", "Project S6A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:s6b", "Project S6B", null);

        long idA = await execRepo.StartAsync(projA.Id, "mr-a", DateTimeOffset.UtcNow);
        long idB = await execRepo.StartAsync(projB.Id, "mr-b", DateTimeOffset.UtcNow);

        await execRepo.CompleteSuccessAsync(idA, new ReviewExecutionResult(DateTimeOffset.UtcNow, 100, 5, 3, 1, 2, 0));

        ReviewExecution? execB = await execRepo.GetByIdAsync(idB);
        Assert.IsNotNull(execB);
        Assert.AreEqual(ReviewExecutionStatus.Running, execB.Status, "Project B execution should still be Running");
    }

    // -------------------------------------------------------------------------
    // Test 7 — Duration is stored correctly
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task DurationIsStoredCorrectly()
    {
        (ReviewExecutionRepository execRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:s7", "Project S7", null);

        long id = await execRepo.StartAsync(proj.Id, "mr-dur", DateTimeOffset.UtcNow);
        await execRepo.CompleteSuccessAsync(id, new ReviewExecutionResult(DateTimeOffset.UtcNow, 9876L, 0, 0, 0, 0, 0));

        ReviewExecution? exec = await execRepo.GetByIdAsync(id);
        Assert.AreEqual(9876L, exec!.DurationMs);
    }

    // -------------------------------------------------------------------------
    // Test 8 — StartedAt and CompletedAt are stored and retrieved correctly
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task TimestampsAreStoredAndRetrievedCorrectly()
    {
        (ReviewExecutionRepository execRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:s8", "Project S8", null);

        DateTimeOffset startedAt = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset completedAt = new DateTimeOffset(2025, 6, 1, 12, 0, 5, TimeSpan.Zero);

        long id = await execRepo.StartAsync(proj.Id, "mr-ts", startedAt);
        await execRepo.CompleteSuccessAsync(id, new ReviewExecutionResult(completedAt, 5000L, 0, 0, 0, 0, 0));

        ReviewExecution? exec = await execRepo.GetByIdAsync(id);
        Assert.IsNotNull(exec);
        Assert.AreEqual(startedAt, exec.StartedAt);
        Assert.AreEqual(completedAt, exec.CompletedAt);
    }

    // -------------------------------------------------------------------------
    // Test 9 — GetByProjectAsync limit is respected
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task GetByProjectAsync_LimitIsRespected()
    {
        (ReviewExecutionRepository execRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:s9", "Project S9", null);

        for (int i = 0; i < 5; i++)
            await execRepo.StartAsync(proj.Id, $"mr-{i}", DateTimeOffset.UtcNow);

        List<ReviewExecution> limited = await execRepo.GetByProjectAsync(proj.Id, limit: 3);
        Assert.AreEqual(3, limited.Count);
    }

    // -------------------------------------------------------------------------
    // Test 10 — Multiple completed executions accumulate correctly per project
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task MultipleExecutionsAccumulatePerProject()
    {
        (ReviewExecutionRepository execRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:s10", "Project S10", null);

        long id1 = await execRepo.StartAsync(proj.Id, "mr-1", DateTimeOffset.UtcNow);
        long id2 = await execRepo.StartAsync(proj.Id, "mr-2", DateTimeOffset.UtcNow);
        long id3 = await execRepo.StartAsync(proj.Id, "mr-3", DateTimeOffset.UtcNow);

        await execRepo.CompleteSuccessAsync(id1, new ReviewExecutionResult(DateTimeOffset.UtcNow, 100, 3, 3, 1, 1, 1));
        await execRepo.CompleteSuccessAsync(id2, new ReviewExecutionResult(DateTimeOffset.UtcNow, 200, 5, 4, 2, 2, 0));
        await execRepo.CompleteFailureAsync(id3, "NetworkException", DateTimeOffset.UtcNow, 50);

        List<ReviewExecution> all = await execRepo.GetByProjectAsync(proj.Id);
        Assert.AreEqual(3, all.Count);
        Assert.AreEqual(2, all.Count(e => e.Status == ReviewExecutionStatus.Succeeded));
        Assert.AreEqual(1, all.Count(e => e.Status == ReviewExecutionStatus.Failed));
        Assert.AreEqual("NetworkException", all.First(e => e.Status == ReviewExecutionStatus.Failed).ErrorType);
    }
}
