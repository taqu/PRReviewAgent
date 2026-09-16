using PRReviewAgent.Pages.Statistics;
using PRReviewAgent.Services.Statistics;

namespace PRReviewAgent.Test;

// ---------------------------------------------------------------------------
// Stub implementation of IStatisticsService for unit tests
// ---------------------------------------------------------------------------

file sealed class StubStatisticsService : IStatisticsService
{
    public OverviewStatistics OverviewResult { get; set; } = new();
    public IReadOnlyList<ReviewTrendPoint> ReviewTrendResult { get; set; } = Array.Empty<ReviewTrendPoint>();
    public IReadOnlyList<TokenTrendPoint> TokenTrendResult { get; set; } = Array.Empty<TokenTrendPoint>();
    public ReviewStatistics ReviewStatisticsResult { get; set; } = new();
    public IReadOnlyList<ModelUsageStatistics> ModelUsageResult { get; set; } = Array.Empty<ModelUsageStatistics>();
    public RuleStatistics RuleStatisticsResult { get; set; } = new();
    public IReadOnlyList<RuleUsageStatistics> RuleUsageResult { get; set; } = Array.Empty<RuleUsageStatistics>();
    public IReadOnlyList<ProjectStatistics> ProjectStatisticsResult { get; set; } = Array.Empty<ProjectStatistics>();

    public Task<OverviewStatistics> GetOverviewAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(OverviewResult);

    public Task<IReadOnlyList<ReviewTrendPoint>> GetReviewTrendAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(ReviewTrendResult);

    public Task<IReadOnlyList<TokenTrendPoint>> GetTokenTrendAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(TokenTrendResult);

    public Task<ReviewStatistics> GetReviewStatisticsAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(ReviewStatisticsResult);

    public Task<IReadOnlyList<ModelUsageStatistics>> GetModelUsageAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(ModelUsageResult);

    public Task<RuleStatistics> GetRuleStatisticsAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(RuleStatisticsResult);

    public Task<IReadOnlyList<RuleUsageStatistics>> GetRuleUsageAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(RuleUsageResult);

    public Task<IReadOnlyList<ProjectStatistics>> GetProjectStatisticsAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(ProjectStatisticsResult);
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public class TestStatisticsDashboard
{
    // -------------------------------------------------------------------------
    // Test 1 — IndexPage_CallsGetOverview
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IndexPage_CallsGetOverview()
    {
        StubStatisticsService stub = new StubStatisticsService
        {
            OverviewResult = new OverviewStatistics
            {
                ReviewCount = 42,
                InputTokens = 100_000,
                OutputTokens = 50_000,
            }
        };

        IndexModel model = new IndexModel(stub);
        await model.OnGetAsync(CancellationToken.None);

        Assert.AreEqual(42, model.Overview.ReviewCount);
        Assert.AreEqual(100_000L, model.Overview.InputTokens);
        Assert.AreEqual(50_000L, model.Overview.OutputTokens);
    }

    // -------------------------------------------------------------------------
    // Test 2 — IndexPage_NullRates_RenderedSafely
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IndexPage_NullRates_RenderedSafely()
    {
        StubStatisticsService stub = new StubStatisticsService
        {
            OverviewResult = new OverviewStatistics
            {
                ReviewCount = 5,
                SelectionRate = null,
                ErrorRate = null,
                AvgReviewDurationMs = null,
            }
        };

        IndexModel model = new IndexModel(stub);
        await model.OnGetAsync(CancellationToken.None);

        // Null nullable values should not throw when accessed
        Assert.IsNull(model.Overview.SelectionRate);
        Assert.IsNull(model.Overview.ErrorRate);
        Assert.IsNull(model.Overview.AvgReviewDurationMs);

        // FormatDuration should return em-dash for null
        string formatted = IndexModel.FormatDuration(null);
        Assert.AreEqual("\u2014", formatted);
    }

    // -------------------------------------------------------------------------
    // Test 3 — ReviewsPage_ShowsDetectionAndSelection
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ReviewsPage_ShowsDetectionAndSelection()
    {
        ReviewTurnStatistics detection = new ReviewTurnStatistics
        {
            TurnType = 1,
            Calls = 10,
            SuccessfulCalls = 9,
            FailedCalls = 1,
            InputTokens = 5_000,
            OutputTokens = 2_000,
            AvgDurationMs = 850.0,
            AvgFindingCount = 3.5,
        };
        ReviewTurnStatistics selection = new ReviewTurnStatistics
        {
            TurnType = 2,
            Calls = 8,
            SuccessfulCalls = 8,
            FailedCalls = 0,
            InputTokens = 3_000,
            OutputTokens = 1_500,
            AvgDurationMs = 420.0,
            AvgFindingCount = 2.0,
        };
        StubStatisticsService stub = new StubStatisticsService
        {
            ReviewStatisticsResult = new ReviewStatistics
            {
                Detection = detection,
                Selection = selection,
                TotalCandidates = 35,
                TotalFinalFindings = 16,
                SelectionRate = 16.0 / 35.0,
                CriticalCount = 4,
                MajorCount = 8,
                MinorCount = 4,
            }
        };

        ReviewsModel model = new ReviewsModel(stub);
        await model.OnGetAsync(CancellationToken.None);

        Assert.IsNotNull(model.Reviews.Detection);
        Assert.IsNotNull(model.Reviews.Selection);
        Assert.AreEqual(1, model.Reviews.Detection.TurnType);
        Assert.AreEqual(2, model.Reviews.Selection.TurnType);
        Assert.AreEqual(10, model.Reviews.Detection.Calls);
        Assert.AreEqual(8, model.Reviews.Selection.Calls);
        Assert.AreEqual(35L, model.Reviews.TotalCandidates);
        Assert.AreEqual(16L, model.Reviews.TotalFinalFindings);
        Assert.AreEqual(4L, model.Reviews.CriticalCount);
        Assert.AreEqual(8L, model.Reviews.MajorCount);
        Assert.AreEqual(4L, model.Reviews.MinorCount);
    }

    // -------------------------------------------------------------------------
    // Test 4 — RulesPage_CallsRuleStatistics
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task RulesPage_CallsRuleStatistics()
    {
        StubStatisticsService stub = new StubStatisticsService
        {
            RuleStatisticsResult = new RuleStatistics
            {
                ActiveRuleCount = 25,
                CreatedCount = 10,
                ExpiredCount = 2,
                ConfidenceIncreaseCount = 30,
                ConfidenceDecreaseCount = 5,
                SearchStats = new RuleSearchStatistics
                {
                    AvgScannedCount = 100.0,
                    AvgCandidateCount = 15.0,
                    AvgSelectedCount = 5.0,
                    AvgSearchDurationMs = 80.0,
                    AvgEmbeddingDurationMs = 40.0,
                }
            }
        };

        RulesModel model = new RulesModel(stub);
        await model.OnGetAsync(CancellationToken.None);

        Assert.AreEqual(25, model.Rules.ActiveRuleCount);
        Assert.AreEqual(10, model.Rules.CreatedCount);
        Assert.AreEqual(2, model.Rules.ExpiredCount);
        Assert.AreEqual(30, model.Rules.ConfidenceIncreaseCount);
        Assert.AreEqual(5, model.Rules.ConfidenceDecreaseCount);
        Assert.AreEqual(100.0, model.Rules.SearchStats.AvgScannedCount);
        Assert.AreEqual(15.0, model.Rules.SearchStats.AvgCandidateCount);
        Assert.AreEqual(5.0, model.Rules.SearchStats.AvgSelectedCount);
    }

    // -------------------------------------------------------------------------
    // Test 5 — ProjectsPage_ShowsAllProjects
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ProjectsPage_ShowsAllProjects()
    {
        IReadOnlyList<ProjectStatistics> projects = new[]
        {
            new ProjectStatistics { ProjectId = 1, ProjectName = "Alpha", ReviewCount = 20 },
            new ProjectStatistics { ProjectId = 2, ProjectName = "Beta", ReviewCount = 15 },
            new ProjectStatistics { ProjectId = 3, ProjectName = "Gamma", ReviewCount = 5 },
        };
        StubStatisticsService stub = new StubStatisticsService
        {
            ProjectStatisticsResult = projects
        };

        ProjectsModel model = new ProjectsModel(stub);
        await model.OnGetAsync(CancellationToken.None);

        Assert.AreEqual(3, model.Projects.Count);
        Assert.AreEqual("Alpha", model.Projects[0].ProjectName);
        Assert.AreEqual("Beta", model.Projects[1].ProjectName);
        Assert.AreEqual("Gamma", model.Projects[2].ProjectName);
    }

    // -------------------------------------------------------------------------
    // Test 6 — EmptyTrends_DoNotThrow
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EmptyTrends_DoNotThrow()
    {
        StubStatisticsService stub = new StubStatisticsService
        {
            ReviewTrendResult = Array.Empty<ReviewTrendPoint>(),
            TokenTrendResult = Array.Empty<TokenTrendPoint>(),
        };

        IndexModel model = new IndexModel(stub);
        await model.OnGetAsync(CancellationToken.None);

        Assert.AreEqual(0, model.ReviewTrend.Count);
        Assert.AreEqual(0, model.TokenTrend.Count);
    }

    // -------------------------------------------------------------------------
    // Test 7 — EmptyRuleUsage_DoNotThrow
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EmptyRuleUsage_DoNotThrow()
    {
        StubStatisticsService stub = new StubStatisticsService
        {
            RuleUsageResult = Array.Empty<RuleUsageStatistics>(),
        };

        RulesModel model = new RulesModel(stub);
        await model.OnGetAsync(CancellationToken.None);

        Assert.AreEqual(0, model.RuleUsage.Count);
    }

    // -------------------------------------------------------------------------
    // Test 8 — ExpiredRuleUsage_FallsBackToRuleId
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ExpiredRuleUsage_FallsBackToRuleId()
    {
        RuleUsageStatistics ruleWithNoDisplay = new RuleUsageStatistics
        {
            ProjectId = 1,
            ProjectName = "TestProject",
            RuleId = "rule-abc-123",
            RuleDisplayText = null,  // expired rule — no description
            CandidateMatches = 5,
            PromptUses = 3,
        };
        StubStatisticsService stub = new StubStatisticsService
        {
            RuleUsageResult = new[] { ruleWithNoDisplay }
        };

        RulesModel model = new RulesModel(stub);
        await model.OnGetAsync(CancellationToken.None);

        Assert.AreEqual(1, model.RuleUsage.Count);
        RuleUsageStatistics r = model.RuleUsage[0];
        Assert.IsNull(r.RuleDisplayText);
        // The Razor view uses: r.RuleDisplayText ?? r.RuleId
        string displayText = r.RuleDisplayText ?? r.RuleId;
        Assert.AreEqual("rule-abc-123", displayText);
    }

    // -------------------------------------------------------------------------
    // Test 9 — MultipleProjects_AllAppear
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task MultipleProjects_AllAppear()
    {
        int projectCount = 7;
        ProjectStatistics[] projects = Enumerable.Range(1, projectCount)
            .Select(i => new ProjectStatistics
            {
                ProjectId = i,
                ProjectName = $"Project {i}",
                ReviewCount = i * 10,
                InputTokens = i * 1_000,
                OutputTokens = i * 500,
                ErrorRate = i == 1 ? null : 0.05,
                SelectionRate = 0.5,
            })
            .ToArray();

        StubStatisticsService stub = new StubStatisticsService
        {
            ProjectStatisticsResult = projects
        };

        ProjectsModel model = new ProjectsModel(stub);
        await model.OnGetAsync(CancellationToken.None);

        Assert.AreEqual(projectCount, model.Projects.Count);
        for (int i = 0; i < projectCount; i++)
        {
            Assert.AreEqual(i + 1, model.Projects[i].ProjectId);
            Assert.AreEqual($"Project {i + 1}", model.Projects[i].ProjectName);
        }
    }

    // -------------------------------------------------------------------------
    // Test 10 — StatisticsService_NoDirectDbDependency
    //           Verifies that page models accept IStatisticsService, not concrete repos.
    // -------------------------------------------------------------------------

    [TestMethod]
    public void StatisticsService_NoDirectDbDependency()
    {
        // All page model constructors accept only IStatisticsService.
        // Verifying at compile-time through constructor calls with the stub.
        StubStatisticsService stub = new StubStatisticsService();

        IndexModel indexModel = new IndexModel(stub);
        ReviewsModel reviewsModel = new ReviewsModel(stub);
        RulesModel rulesModel = new RulesModel(stub);
        ProjectsModel projectsModel = new ProjectsModel(stub);

        Assert.IsNotNull(indexModel);
        Assert.IsNotNull(reviewsModel);
        Assert.IsNotNull(rulesModel);
        Assert.IsNotNull(projectsModel);
    }

    // -------------------------------------------------------------------------
    // Test 11 — FormatDuration covers all ranges
    // -------------------------------------------------------------------------

    [TestMethod]
    public void FormatDuration_CoverAllRanges()
    {
        // null -> em-dash
        Assert.AreEqual("\u2014", IndexModel.FormatDuration(null));

        // < 1000 ms -> "XXX ms"
        string underSecond = IndexModel.FormatDuration(840);
        Assert.IsTrue(underSecond.EndsWith(" ms"), $"Expected ms suffix, got: {underSecond}");

        // >= 1000 ms < 60000 -> "X.X s"
        string seconds = IndexModel.FormatDuration(1800);
        Assert.IsTrue(seconds.EndsWith(" s"), $"Expected s suffix, got: {seconds}");

        // >= 60000 -> "X.X min"
        string minutes = IndexModel.FormatDuration(120_000);
        Assert.IsTrue(minutes.EndsWith(" min"), $"Expected min suffix, got: {minutes}");
    }

    // -------------------------------------------------------------------------
    // Test 12 — ModelUsage_EmptyDoesNotThrow
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ModelUsage_EmptyDoesNotThrow()
    {
        StubStatisticsService stub = new StubStatisticsService
        {
            ModelUsageResult = Array.Empty<ModelUsageStatistics>()
        };

        ReviewsModel model = new ReviewsModel(stub);
        await model.OnGetAsync(CancellationToken.None);

        Assert.AreEqual(0, model.ModelUsage.Count);
    }
}
