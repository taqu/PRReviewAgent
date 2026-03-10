using Microsoft.Agents.AI;
using NGitLab;
using NGitLab.Impl;
using NGitLab.Models;
using PRReviewAgent.Services;
using PRReviewAgent.Services.GitLabWebhook;
using System.Text;
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
            }
            NGitLab.IMergeRequestClient mergeRequestClient = gitLabClient_.GetMergeRequest(payloadComment.project.id);
            #if false
            {
                IMergeRequestCommentClient mergeRequestCommentClient = mergeRequestClient.Comments(payloadComment.merge_request.iid);
                MergeRequestCommentEdit mergeRequestCommentEdit = new MergeRequestCommentEdit();
                string result = System.IO.File.ReadAllText("result.md");
                StringBuilder tempBuilder = new StringBuilder();
                tempBuilder.Append($"{payloadComment.object_attributes.note}\n\n");
                tempBuilder.Append(result);
                mergeRequestCommentEdit.Body = tempBuilder.ToString();
                try
                {
                    mergeRequestCommentClient.Edit(payloadComment.object_attributes.id, mergeRequestCommentEdit);
                }
                catch
                {
                }

            }
            #endif
            GitLabCollectionResponse<NGitLab.Models.Diff> response = mergeRequestClient.GetDiffsAsync(payloadComment.merge_request.iid);
            List<NGitLab.Models.Diff> diffs = new List<NGitLab.Models.Diff>();

            Context context = Context.Instance;
            Microsoft.Agents.AI.AgentResponse helloResponse = await context.Agents.RunExecutorAsync("Hello!", context.CancellationToken);
            context.Agents.GitLabChanges.ClearDiffs();
            foreach (NGitLab.Models.Diff diff in response)
            {
                context.Agents.GitLabChanges.AddDiff(diff, context.Settings.IsTargetExtension);
            }

            foreach (Difference diff in context.Agents.GitLabChanges.Diffs)
            {
                Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunExecutorAsync($"Summarize a next diff briefly in few lines.\n{diff.Change.path}\n----\n{diff.Diff.Difference}", context.CancellationToken);
                diff.Change.summary = agentResponse.Text;
            }
            string changeFilePaths = context.Agents.GitLabChanges.GetChangeFilePaths();
            Assignments? assignments = await context.Agents.RunExecutorAsync<Assignments>($"Group next diffs in the pull request by relevance and assign file paths to each reviewer.\n```json\n{changeFilePaths}```", context.CancellationToken);
            if (null == assignments)
            {
                return;
            }
            List<string> reviews = new List<string>();
            foreach (Assign assign in assignments.assigns)
            {
                string? reviewText = context.Agents.GitLabChanges.GetReviewDiffs(assign.paths, "ja");
                if (string.IsNullOrEmpty(reviewText))
                {
                    continue;
                }
                try
                {
                    Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunExecutorAsync(reviewText, context.CancellationToken);
                    if (string.IsNullOrEmpty(agentResponse.Text))
                    {
                        continue;
                    }
                    reviews.Add(agentResponse.Text);
                }
                catch
                {
                    continue;
                }
            }

            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("Organize the following reviews:\n");
            foreach(string review in reviews)
            {
                stringBuilder.Append(review).Append("\n\n");
            }
            if(0 < stringBuilder.Length)
            {
                Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunExecutorAsync(stringBuilder.ToString(), context.CancellationToken);
                if(!string.IsNullOrEmpty(agentResponse.Text))
                {
                    System.IO.File.WriteAllText("result.md", agentResponse.Text);

                    IMergeRequestCommentClient mergeRequestCommentClient = mergeRequestClient.Comments(payloadComment.merge_request.iid);
                    MergeRequestCommentEdit mergeRequestCommentEdit = new MergeRequestCommentEdit();
                    stringBuilder.Clear();
                    stringBuilder.Append($"{payloadComment.object_attributes.note}\n\n");
                    stringBuilder.Append(agentResponse.Text);
                    mergeRequestCommentEdit.Body = stringBuilder.ToString();
                    try
                    {
                        mergeRequestCommentClient.Edit(payloadComment.object_attributes.id, mergeRequestCommentEdit);
                    }
                    catch
                    {
                    }

                }
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
