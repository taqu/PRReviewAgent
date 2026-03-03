using NGitLab;
using NGitLab.Impl;
using PRReviewAgent.Services;
using PRReviewAgent.Services.GitLabWebhook;
using static PRReviewAgent.Tools.GitLabChanges;

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
                Settings.Initialize();
                {
                    Context.Initialize();
                }

                {
                    Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Settings.Instance.Config["gitlab"];
                    Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Settings.Instance.Secrets["gitlab"];
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
            PayloadComment? payloadComment = null;
            {
                string json = System.IO.File.ReadAllText("TestData\\payload_comment.json");
                payloadComment = Newtonsoft.Json.JsonConvert.DeserializeObject<PayloadComment>(json);
            }
            NGitLab.IMergeRequestClient mergeRequestClient = gitLabClient_.GetMergeRequest(payloadComment.project.id);
            GitLabCollectionResponse<NGitLab.Models.Diff> response = mergeRequestClient.GetDiffsAsync(payloadComment.merge_request.iid);
            List<NGitLab.Models.Diff> diffs = new List<NGitLab.Models.Diff>();

            Context context = Context.Instance;
            context.Agents.GitLabChanges.ClearDiffs();
            foreach (NGitLab.Models.Diff diff in response)
            {
                context.Agents.GitLabChanges.AddDiff(diff);
            }

            foreach (Difference diff in context.Agents.GitLabChanges.Diffs)
            {
                Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunExecutorAsync($"Summarize a next diff briefly in few lines.\n{diff.Change.path}\n----\n{diff.Diff.Difference}", context.CancellationToken);
                diff.Change.summary = agentResponse.Text;
            }
            {
                string json = context.Agents.GitLabChanges.GetChangeFilePaths();
                Assignments assignments = await context.Agents.RunExecutorAsync<Assignments>($"Group next diffs in the pull request by relevance and assign file paths to each reviewer.\n```json\n{json}```", context.CancellationToken);
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
