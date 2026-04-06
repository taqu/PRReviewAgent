using NGitLab;
using NGitLab.Impl;
using NGitLab.Models;
using Octokit;
using PRReviewAgent.Services.GitHubWebhook;
using PRReviewAgent.Services.GitLabWebhook;
using System;
using System.ComponentModel;
using System.Text;

namespace PRReviewAgent.Services
{
    /// <summary>
    /// Represents a task that processes a GitHub webhook comment, performs a code review, and updates the comment with the review results.
    /// </summary>
    public class GitHubWebhookCommentTask
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GitHubWebhookCommentTask"/> class.
        /// </summary>
        /// <param name="payloadIssueComment">The GitHub webhook payload for the issue comment.</param>
        public GitHubWebhookCommentTask(PayloadIssueComment payloadIssueComment)
        {
            payloadIssueComment_ = payloadIssueComment;
            
            // Determine the language for the review based on the comment body.
            // If no language is specified or supported, fall back to the default language from settings.
            language_ = GitLabWebhookCommentTask.FindLanguage(payloadIssueComment_.comment.body);
            if (string.IsNullOrEmpty(language_) || !Context.Instance.Settings.HasTemplate(language_))
            {
                Tomlyn.Model.TomlTable? commonTable = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["common"];
                language_ = (string)commonTable["default_language"];
            }

            // Extract the pull request number from the pull request URL by taking the last segment.
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

        /// <summary>
        /// Represents an assignment of file paths to a reviewer.
        /// </summary>
        public class Assign
        {
            public int reviewer_number { get; set; }
            public string[] paths { get; set; }
        }

        /// <summary>
        /// Represents a collection of assignments.
        /// </summary>
        /// <param name="assigns">The array of assignments.</param>
        public record Assignments(
            Assign[] assigns
        );

        /// <summary>
        /// Represents a target file for review, including its diff and summary.
        /// </summary>
        public class Target
        {
            public PullRequestFile File { get; set; }
            public string Summary { get; set; }
        }

        /// <summary>
        /// Represents a change in a file, including its path and summary.
        /// </summary>
        public class Change
        {
            [Description("path")]
            public string path { get;set; }
            [Description("summary")]
            public string summary { get; set; }
        }

        /// <summary>
        /// Represents a collection of changes.
        /// </summary>
        /// <param name="changes">The array of changes.</param>
        public record Changes(
            [Description("Changed file paths and summaries")]
            Change[] changes
        );

        /// <summary>
        /// Gets the review diffs for the specified paths and targets.
        /// </summary>
        /// <param name="paths">The paths of the files to include in the review.</param>
        /// <param name="targets">The list of target files and their summaries.</param>
        /// <returns>A formatted string containing the review diffs, or null if no template is found.</returns>
        public string? GetReviewDiffs(string[] paths, List<Target> targets)
        {
            // Retrieve the review template corresponding to the determined language.
            string? template = Context.Instance.Settings.GetReviewTemplate(language_);
            if(null == template)
            {
                return null;
            }

            // Build the review text by appending the template followed by the diffs of assigned files.
            stringBuilder_.Clear();
            stringBuilder_.Append(template);
            stringBuilder_.Append("\n\n");
                
            foreach (string path in paths)
            {
                string diff = string.Empty;
                foreach(Target target in targets)
                {
                    // Match the assigned file path with its corresponding diff from the target list.
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
                // Append the file name as a header followed by the actual diff content.
                stringBuilder_.Append("# ").Append(path).Append("\n");
                stringBuilder_.Append(diff).Append("\n");
            }
            return stringBuilder_.ToString();
        }

        /// <summary>
        /// Runs the GitHub webhook comment task asynchronously.
        /// </summary>
        /// <param name="serviceProvider">The service provider to resolve dependencies.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitHubWebhookCommentTask>? logger = serviceProvider.GetService<ILogger<GitHubWebhookCommentTask>>();
            logger.LogInformation($"Processing comment: {payloadIssueComment_.comment.id}");

            Octokit.GitHubClient gitHubClient = serviceProvider.GetService<GitHubClientService>().GitHubClient;

            Context context = Context.Instance;

            // Step 1: Fetch the list of files included in this pull request.
            IReadOnlyList<PullRequestFile> files = await gitHubClient.PullRequest.Files(payloadIssueComment_.repository.id, pullRequestNumber_);
            List<Target> targets = new List<Target>();

            // Step 2: Identify files that are suitable for review (target extensions and not deleted/moved).
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
            if (targets.Count <= 0)
            {
                await PostCommentAsync("No reviews are generated. There are no diffs to review.", gitHubClient, logger);
                return;
            }

            // Step 3: Summarize each file's diff using an AI assistant to prepare for grouping.
            logger.LogInformation($"Summarizing {targets.Count} diffs.");
            foreach (Target target in targets)
            {
                try
                {
                    Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunAsync(Agents.Type.Assistant, $"Summarize a next diff briefly in few lines.\n{target.File.FileName}\n----\n{target.File.Patch}", context.CancellationToken);
                    target.Summary = agentResponse.Text;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.ToString());
                }
            }

            List<string> reviews = new List<string>();
            {
                // Step 4: Prepare a list of changes to send to the AI planner for grouping.
                List<Change> changes = new List<Change>(targets.Count);
                foreach (Target target in targets)
                {
                    if (string.IsNullOrEmpty(target.Summary))
                    {
                        continue;
                    }
                    changes.Add(new Change() { path = target.File.FileName, summary = target.Summary });
                }

                // Step 5: Ask the AI planner to group related files and assign them to reviewers.
                logger.LogInformation($"Assigning {changes.Count} changes to reviewers.");
                string changeFilePaths = Newtonsoft.Json.JsonConvert.SerializeObject(new Changes(changes.ToArray()));
                Assignments? assignments = null;
                try
                {
                    assignments = await context.Agents.RunAsync<Assignments>(Agents.Type.Planner, $"Group next diffs in the pull request by relevance and assign file paths to each reviewers.\n```json\n{changeFilePaths}```", context.CancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.ToString());
                    await PostCommentAsync($"No reviews are generated. Fail in assigning the files to reviewers.\n{ex.Message}", gitHubClient, logger);
                    return;
                }

                // Step 6: Generate a code review for each assigned group of files.
                logger.LogInformation($"Generating reviews for {assignments.assigns.Length} assignments.");
                foreach (Assign assign in assignments.assigns)
                {
                    string? reviewText = GetReviewDiffs(assign.paths, targets);
                    if (string.IsNullOrEmpty(reviewText))
                    {
                        continue;
                    }
                    try
                    {
                        Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunAsync(Agents.Type.Executor, reviewText, context.CancellationToken);
                        if (string.IsNullOrEmpty(agentResponse.Text))
                        {
                            continue;
                        }
                        reviews.Add(agentResponse.Text);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex.ToString());
                        continue;
                    }
                }
            }

            // Step 7: Consolidate multiple reviews into a single organized response.
            logger.LogInformation($"Organizing {reviews.Count} reviews.");
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
                    // Use the AI executor to merge the reviews into a final organized text.
                    Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunAsync(Agents.Type.Executor, stringBuilder_.ToString(), context.CancellationToken);
                    if (!string.IsNullOrEmpty(agentResponse.Text))
                    {
                        organizedReview = agentResponse.Text;
                    }
                }
                catch (Exception ex)
                {
                    await PostCommentAsync($"No reviews are generated. Fail in organizing the reviews.\n{ex.Message}", gitHubClient, logger);
                    logger.LogError(ex.ToString());
                }
            }
            else
            {
                await PostCommentAsync("No reviews are generated. Fail in assigning the files to reviewers.", gitHubClient, logger);
            }

            // Step 8: Post the final review by editing the original comment that triggered the review.
            if (!string.IsNullOrEmpty(organizedReview))
            {
                await PostCommentAsync(organizedReview, gitHubClient, logger);
            }
            else
            {
                // Inform the user if no reviews could be generated.
                stringBuilder_.Clear();
                stringBuilder_.Append("no reviews are generated.");
                await PostCommentAsync(stringBuilder_.ToString(), gitHubClient, logger);
            }
        }

        private const int MaxLogLength = 128;

        private async Task PostCommentAsync(string comment, Octokit.GitHubClient gitHubClient, ILogger<GitHubWebhookCommentTask>? logger)
        {
            PullRequestReviewCommentEdit pullRequestReviewCommentEdit = new PullRequestReviewCommentEdit(comment);
            try
            {
                await gitHubClient.PullRequest.ReviewComment.Edit(payloadIssueComment_.repository.id, payloadIssueComment_.comment.id, pullRequestReviewCommentEdit);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    ReadOnlySpan<char> span = comment.AsSpan();
                    int length;
                    for (length = 0; length < span.Length && length < MaxLogLength; ++length)
                    {
                        if (span[length] == '\n' || span[length] == '\r')
                        {
                            break;
                        }
                    }
                    span = span.Slice(0, length);
                    logger.LogInformation($"Comment is updated. {span}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
        }

        private PayloadIssueComment payloadIssueComment_;
        private string language_;
        private int pullRequestNumber_;
        private StringBuilder stringBuilder_ = new StringBuilder();
    }
}
