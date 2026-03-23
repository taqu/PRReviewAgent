using NGitLab;
using NGitLab.Models;
using PRReviewAgent.Services.GitLabWebhook;
using System.Text;

namespace PRReviewAgent.Test
{
    [TestClass]
    public sealed class TestMergeRequest
    {
        private NGitLab.GitLabClient gitLabClient_;

        [TestInitialize]
        public void TestInit()
        {
            try
            {
                Context.Initialize();
                {
                    Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["gitlab"];
                    Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["gitlab"];
                    gitLabClient_ = new NGitLab.GitLabClient((string)config["url"], (string)secrets["personal_access_token"]);
                }
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
        public async Task TestCommentPayloadAsync()
        {
            await Context.Instance.WarmUpAsync();
            PayloadComment? payloadComment = null;
            {
                string json = System.IO.File.ReadAllText("TestData\\payload_comment.json");
                payloadComment = Newtonsoft.Json.JsonConvert.DeserializeObject<PayloadComment>(json);
                Services.GitLabWebhookCommentTask gitLabWebhookCommentTask = new Services.GitLabWebhookCommentTask(payloadComment);
                await gitLabWebhookCommentTask.RunAsync(null, Context.Instance.CancellationToken);
            }
        }

        public class Assign
        {
            public int reviewer_number { get; set; }
            public string[] paths { get; set; }
        }
        public record Assignments(
            Assign[] assigns
        );
    }
}
