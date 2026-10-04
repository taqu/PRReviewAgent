using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.AutoReview;

namespace PRReviewAgent.Test;

/// <summary>
/// Tests for per-repository per-user language preference feature.
/// All tests use an in-memory SQLite database to avoid file system dependencies.
/// </summary>
[TestClass]
public class TestUserLanguagePreference
{
    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static async Task<(UserLanguagePreferenceRepository langRepo, ProjectRepository projRepo)> CreateRepositoriesAsync()
    {
        // Use a unique named in-memory SQLite database to allow connection sharing
        string dbName = $"testdb_{Guid.NewGuid():N}";
        string cs = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        // RuleRepository.InitializeAsync creates the `projects` table which satisfies the FK constraint.
        RuleRepository ruleRepo = new RuleRepository(cs, isConnectionString: true);
        await ruleRepo.InitializeAsync();
        ProjectRepository projRepo = new ProjectRepository(cs, isConnectionString: true);
        UserLanguagePreferenceRepository langRepo = new UserLanguagePreferenceRepository(cs, isConnectionString: true);
        await langRepo.InitializeAsync();
        return (langRepo, projRepo);
    }

    // -------------------------------------------------------------------------
    // Parser tests
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Parser_ReviewLangEn_ReturnsReviewLanguageType()
    {
        ReviewCommand? cmd = ReviewCommandParser.Parse("/review_lang en");
        Assert.IsNotNull(cmd);
        Assert.AreEqual(ReviewCommandType.ReviewLanguage, cmd.Type);
        Assert.AreEqual("en", cmd.Language);
    }

    [TestMethod]
    public void Parser_ReviewLangJa_ReturnsReviewLanguageWithJa()
    {
        ReviewCommand? cmd = ReviewCommandParser.Parse("/review_lang ja");
        Assert.IsNotNull(cmd);
        Assert.AreEqual(ReviewCommandType.ReviewLanguage, cmd.Type);
        Assert.AreEqual("ja", cmd.Language);
    }

    [TestMethod]
    public void Parser_ReviewWithSlashEn_ReturnsReviewTypeWithEn()
    {
        ReviewCommand? cmd = ReviewCommandParser.Parse("/review /en");
        Assert.IsNotNull(cmd);
        Assert.AreEqual(ReviewCommandType.Review, cmd.Type);
        Assert.AreEqual("en", cmd.Language);
    }

    [TestMethod]
    public void Parser_ReviewWithSlashJa_ReturnsReviewTypeWithJa()
    {
        ReviewCommand? cmd = ReviewCommandParser.Parse("/review /ja");
        Assert.IsNotNull(cmd);
        Assert.AreEqual(ReviewCommandType.Review, cmd.Type);
        Assert.AreEqual("ja", cmd.Language);
    }

    [TestMethod]
    public void Parser_PlainReview_ReturnsNullLanguage()
    {
        ReviewCommand? cmd = ReviewCommandParser.Parse("/review");
        Assert.IsNotNull(cmd);
        Assert.AreEqual(ReviewCommandType.Review, cmd.Type);
        Assert.IsNull(cmd.Language);
    }

    [TestMethod]
    public void Parser_ReviewLangNoToken_ReturnsNullLanguage()
    {
        ReviewCommand? cmd = ReviewCommandParser.Parse("/review_lang");
        Assert.IsNotNull(cmd);
        Assert.AreEqual(ReviewCommandType.ReviewLanguage, cmd.Type);
        Assert.IsNull(cmd.Language);
    }

    [TestMethod]
    public void Parser_ReviewLangNotMatchedByReview()
    {
        // Verifies that /review_lang is NOT misrouted as ReviewCommandType.Review
        ReviewCommand? cmd = ReviewCommandParser.Parse("/review_lang fr");
        Assert.IsNotNull(cmd);
        Assert.AreEqual(ReviewCommandType.ReviewLanguage, cmd.Type);
        Assert.AreEqual("fr", cmd.Language);
    }

    // -------------------------------------------------------------------------
    // Repository tests
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Repository_SetAndGet_ReturnsStoredLanguage()
    {
        (UserLanguagePreferenceRepository langRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project project = await projRepo.GetOrCreateAsync("ext:A", "Project A", null);

        await langRepo.SetAsync(project.Id, "gitlab:42", "ja", CancellationToken.None);
        string? result = await langRepo.GetAsync(project.Id, "gitlab:42", CancellationToken.None);

        Assert.AreEqual("ja", result);
    }

    [TestMethod]
    public async Task Repository_Upsert_UpdatesExistingRecord()
    {
        (UserLanguagePreferenceRepository langRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project project = await projRepo.GetOrCreateAsync("ext:B", "Project B", null);

        await langRepo.SetAsync(project.Id, "gitlab:99", "en", CancellationToken.None);
        await langRepo.SetAsync(project.Id, "gitlab:99", "ja", CancellationToken.None);

        string? result = await langRepo.GetAsync(project.Id, "gitlab:99", CancellationToken.None);
        Assert.AreEqual("ja", result, "Second upsert should overwrite the first");

        // Verify only one record exists
        // (CRUD semantics: a second get should still return exactly the latest value)
        string? result2 = await langRepo.GetAsync(project.Id, "gitlab:99", CancellationToken.None);
        Assert.AreEqual("ja", result2);
    }

    [TestMethod]
    public async Task Repository_Get_ReturnsNullForMissingKey()
    {
        (UserLanguagePreferenceRepository langRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project project = await projRepo.GetOrCreateAsync("ext:C", "Project C", null);

        string? result = await langRepo.GetAsync(project.Id, "gitlab:nonexistent", CancellationToken.None);
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task Repository_SameRepo_DifferentUsers_AreIsolated()
    {
        (UserLanguagePreferenceRepository langRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project project = await projRepo.GetOrCreateAsync("ext:D", "Project D", null);

        await langRepo.SetAsync(project.Id, "gitlab:1", "en", CancellationToken.None);
        await langRepo.SetAsync(project.Id, "gitlab:2", "ja", CancellationToken.None);

        string? lang1 = await langRepo.GetAsync(project.Id, "gitlab:1", CancellationToken.None);
        string? lang2 = await langRepo.GetAsync(project.Id, "gitlab:2", CancellationToken.None);

        Assert.AreEqual("en", lang1, "User 1 should have 'en'");
        Assert.AreEqual("ja", lang2, "User 2 should have 'ja'");
    }

    [TestMethod]
    public async Task Repository_SameUser_DifferentRepos_AreIsolated()
    {
        (UserLanguagePreferenceRepository langRepo, ProjectRepository projRepo) = await CreateRepositoriesAsync();
        Project projectA = await projRepo.GetOrCreateAsync("ext:E1", "Project E1", null);
        Project projectB = await projRepo.GetOrCreateAsync("ext:E2", "Project E2", null);

        await langRepo.SetAsync(projectA.Id, "gitlab:7", "en", CancellationToken.None);
        await langRepo.SetAsync(projectB.Id, "gitlab:7", "ja", CancellationToken.None);

        string? langA = await langRepo.GetAsync(projectA.Id, "gitlab:7", CancellationToken.None);
        string? langB = await langRepo.GetAsync(projectB.Id, "gitlab:7", CancellationToken.None);

        Assert.AreEqual("en", langA, "Project A should have 'en' for user 7");
        Assert.AreEqual("ja", langB, "Project B should have 'ja' for user 7");
    }
}
