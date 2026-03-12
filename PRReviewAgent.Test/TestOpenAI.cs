using Octokit;
using PRReviewAgent.Services;
using PRReviewAgent.Services.GitHubWebhook;
using PRReviewAgent.Services.GitLabWebhook;
using System.Xml.Linq;

namespace PRReviewAgent.Test;

[TestClass]
public class TestOpenAI
{
    private Agents agents_;
    private Octokit.GitHubClient gitHubClient_;
    [TestInitialize]
    public void TestInit()
    {
        try
        {
            Context.Initialize();
            agents_ = new Agents();
            gitHubClient_ = new Octokit.GitHubClient(new Octokit.ProductHeaderValue((string)((Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["github"])["name"]));
            gitHubClient_.Credentials = new Credentials((string)((Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["github"])["personal_access_token"]);
        }
        catch (Exception ex)
        {
            return;
        }
    }

    [TestCleanup]
    public void TestCleanup()
    {
    }


    [TestMethod]
    public async Task TestMethodAsync()
    {
        PayloadIssueComment? issueComment;
        {
            string json = System.IO.File.ReadAllText("TestData\\github_payload.json");
            issueComment = Newtonsoft.Json.JsonConvert.DeserializeObject<PayloadIssueComment>(json);
        }

        GitHubWebhookCommentTask gitHubWebhookCommentTask = new GitHubWebhookCommentTask(issueComment);
        await gitHubWebhookCommentTask.RunAsync(gitHubClient_);
    }
}
