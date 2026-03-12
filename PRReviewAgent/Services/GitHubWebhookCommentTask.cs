using Octokit;
using PRReviewAgent.Services.GitHubWebhook;
using System.ComponentModel;
using System.Text;

namespace PRReviewAgent.Services
{
    public class GitHubWebhookCommentTask
    {
        public GitHubWebhookCommentTask(PayloadIssueComment payloadIssueComment)
        {
            payloadIssueComment_ = payloadIssueComment;
            language_ = GitLabWebhookCommentTask.FindLanguage(payloadIssueComment_.comment.body);
            if (string.IsNullOrEmpty(language_) || !Context.Instance.Settings.HasTemplate(language_))
            {
                Tomlyn.Model.TomlTable? commonTable = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["common"];
                language_ = (string)commonTable["default_language"];
            }
            {
                Uri uri = new Uri(payloadIssueComment_.issue.pull_request.url);
                string number = uri.Segments[uri.Segments.Length - 1];
                int.TryParse(number, out pullRequestNumber_);
            }
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

        public class Target
        {
            public PullRequestFile File { get; set; }
            public string Summary { get; set; }
        }

        public class Change
        {
            [Description("path")]
            public string path { get;set; }
            [Description("summary")]
            public string summary { get; set; }
        }

        public record Changes(
            [Description("Changed file paths and summaries")]
            Change[] changes
        );

        public string? GetReviewDiffs(string[] paths, List<Target> targets)
        {
            string? template = Context.Instance.Settings.GetReviewTemplate(language_);
            if(null == template)
            {
                return null;
            }
            stringBuilder_.Clear();
            stringBuilder_.Append(template);
            stringBuilder_.Append("\n\n");
                
            foreach (string path in paths)
            {
                string diff = string.Empty;
                foreach(Target target in targets)
                {
                    if(target.File.FileName == path)
                    {
                        diff = target.File.Patch;
                        break;
                    }
                }
                if(string.IsNullOrEmpty(diff))
                {
                    continue;
                }
                stringBuilder_.Append("# ").Append(path).Append("\n");
                stringBuilder_.Append(diff).Append("\n");
            }
            return stringBuilder_.ToString();
        }

        //public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        public async Task RunAsync(Octokit.GitHubClient gitHubClient)
        {
            //ILogger<GitHabWebhookCommentTask>? logger = serviceProvider.GetService<ILogger<GitHabWebhookCommentTask>>();

            //Octokit.PullRequest pullRequest = await gitHubClient.PullRequest.Get(payloadIssueComment_.repository.id, pullRequestNumber_);
            Context context = Context.Instance;

            IReadOnlyList<PullRequestFile> files = await gitHubClient.PullRequest.Files(payloadIssueComment_.repository.id, pullRequestNumber_);
            List<Target> targets = new List<Target>();
            foreach (PullRequestFile file in files)
            {
                bool isDeleted = !string.IsNullOrEmpty(file.PreviousFileName) && string.IsNullOrEmpty(file.FileName);
                bool isMoved = !string.IsNullOrEmpty(file.PreviousFileName) && !string.IsNullOrEmpty(file.FileName) && file.PreviousFileName!=file.FileName && string.IsNullOrEmpty(file.Patch);
                if (isDeleted || isMoved)
                {
                    continue;
                }
                if (context.Settings.IsTargetExtension(file.FileName))
                {
                    targets.Add(new Target() { File = file, Summary = string.Empty });
                }
            }

            foreach (Target target in targets)
            {
                Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunPlannerAsync($"Summarize a next diff briefly in few lines.\n{target.File.FileName}\n----\n{target.File.Patch}", context.CancellationToken);
                target.Summary = agentResponse.Text;
            }

            List<string> reviews = new List<string>();
            {
                List<Change> changes = new List<Change>(targets.Count);
                foreach (Target target in targets)
                {
                    if (string.IsNullOrEmpty(target.Summary))
                    {
                        continue;
                    }
                    changes.Add(new Change() { path = target.File.FileName, summary = target.Summary });
                }

                string changeFilePaths = Newtonsoft.Json.JsonConvert.SerializeObject(new Changes(changes.ToArray()));
                Assignments? assignments = await context.Agents.RunPlannerAsync<Assignments>($"Group next diffs in the pull request by relevance and assign file paths to each reviewers.\n```json\n{changeFilePaths}```", context.CancellationToken);
                if (null == assignments)
                {
                    return;
                }
                foreach (Assign assign in assignments.assigns)
                {
                    string? reviewText = GetReviewDiffs(assign.paths, targets);
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
            }

            stringBuilder_.Clear();
            string organizedReview = string.Empty;
            if (reviews.Count == 1)
            {
                organizedReview = reviews[0];
            }
            else if(1<reviews.Count)
            {
                string? organizeTemplate = Context.Instance.Settings.GetOrganizeTemplate(language_);
                if (null != organizeTemplate)
                {
                    stringBuilder_.Append(organizeTemplate);
                }
                else
                {
                    stringBuilder_.Append("Organize the following reviews:\n");
                }
                foreach (string review in reviews)
                {
                    stringBuilder_.Append(review).Append("\n\n");
                }
                try
                {
                    Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunExecutorAsync(stringBuilder_.ToString(), context.CancellationToken);
                    if (!string.IsNullOrEmpty(agentResponse.Text))
                    {
                        organizedReview = agentResponse.Text;
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrEmpty(organizedReview))
            {
                stringBuilder_.Clear();
                stringBuilder_.Append($"{payloadIssueComment_.comment.body.Trim()}\n\n");
                stringBuilder_.Append(organizedReview);
                PullRequestReviewCommentEdit pullRequestReviewCommentEdit = new PullRequestReviewCommentEdit(stringBuilder_.ToString());
                try
                {
                    PullRequestReviewComment pullRequestReviewComment = await gitHubClient.PullRequest.ReviewComment.Edit(payloadIssueComment_.repository.id, payloadIssueComment_.comment.id, pullRequestReviewCommentEdit);
                }
                catch
                {
                }
            }
            else
            {
                stringBuilder_.Clear();
                stringBuilder_.Append($"{payloadIssueComment_.comment.body.Trim()}\n\n");
                stringBuilder_.Append("no reviews are generated.");
                PullRequestReviewCommentEdit pullRequestReviewCommentEdit = new PullRequestReviewCommentEdit(stringBuilder_.ToString());
                try
                {
                    await gitHubClient.PullRequest.ReviewComment.Edit(payloadIssueComment_.repository.id, payloadIssueComment_.comment.id, pullRequestReviewCommentEdit);
                }
                catch
                {
                }
            }
        }

        private PayloadIssueComment payloadIssueComment_;
        private string language_;
        private int pullRequestNumber_;
        private StringBuilder stringBuilder_ = new StringBuilder();
    }
}
