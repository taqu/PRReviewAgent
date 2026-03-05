using Microsoft.Extensions.Logging;
using NGitLab;
using NGitLab.Models;
using PRReviewAgent.Services.GitLabWebhook;
using System.Text;
using static PRReviewAgent.Tools.GitLabChanges;

namespace PRReviewAgent.Services
{
    public class  GitLabWebhookCommentPayload
    {
        public string object_kind { get; set; }
    }

    public class GitLabWebhookCommentTask
    {
        public GitLabWebhookCommentTask(PayloadComment payloadComment)
        {
            payloadComment_ = payloadComment;
#if false
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload_, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText("payload_comment.json", json);
#endif
        }

        public class Assign
        {
            public int reviewer_number { get; set; }
            public string[] paths { get; set; }
        }
        public record Assignments(
            Assign[] assigns
        );

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitLabWebhookCommentTask>? logger = serviceProvider.GetService<ILogger<GitLabWebhookCommentTask>>();

            NGitLab.GitLabClient gitLabClient = serviceProvider.GetService<GitLabClientService>().GitLabClient;

            NGitLab.IMergeRequestClient mergeRequestClient = gitLabClient.GetMergeRequest(payloadComment_.project.id);
            GitLabCollectionResponse<NGitLab.Models.Diff> response = mergeRequestClient.GetDiffsAsync(payloadComment_.merge_request.iid);
            List<NGitLab.Models.Diff> diffs = new List<NGitLab.Models.Diff>();

            Context context = Context.Instance;
            context.Agents.GitLabChanges.ClearDiffs();
            await foreach (NGitLab.Models.Diff diff in response)
            {
                context.Agents.GitLabChanges.AddDiff(diff);
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
            string? organizeTemplate = Settings.Instance.GetOrganizeTemplate("ja");
            if(null != organizeTemplate)
            {
                stringBuilder.Append(organizeTemplate);
            }
            else {
                stringBuilder.Append("Organize the following reviews:\n");
            }
            foreach(string review in reviews)
            {
                stringBuilder.Append(review).Append("\n\n");
            }
            if(0 < stringBuilder.Length)
            {
                Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunExecutorAsync(stringBuilder.ToString(), context.CancellationToken);
                if(!string.IsNullOrEmpty(agentResponse.Text))
                {
                    System.IO.File.WriteAllText("result.txt", agentResponse.Text);
                }
            }
        }

        private PayloadComment payloadComment_;
    }
}
