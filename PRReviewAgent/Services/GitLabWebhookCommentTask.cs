using Microsoft.Extensions.Logging;
using NGitLab;
using NGitLab.Impl;
using NGitLab.Models;
using Octokit;
using PRReviewAgent.Services.GitLabWebhook;
using System.ComponentModel;
using System.Text;

namespace PRReviewAgent.Services
{
    /// <summary>
    /// Represents the payload for a GitLab webhook comment.
    /// </summary>
    public class  GitLabWebhookCommentPayload
    {
        /// <summary>
        /// Gets or sets the object kind.
        /// </summary>
        public string object_kind { get; set; }
    }

    /// <summary>
    /// Represents a task that processes a GitLab webhook comment, performs a code review, and updates the comment with the review results.
    /// </summary>
    public class GitLabWebhookCommentTask
    {
        /// <summary>
        /// Finds the language code in the given comment.
        /// Searches for a pattern like '/en' or '/ja' at the beginning of the comment.
        /// </summary>
        /// <param name="comment">The comment text to search.</param>
        /// <returns>The language code if found; otherwise, an empty string.</returns>
        public static string FindLanguage(string comment)
        {
            // Trim the comment and focus on the first line.
            ReadOnlySpan<char> line = comment.AsSpan().Trim();
            int index = line.IndexOfAny("\n\r".AsSpan());
            if (0 <= index)
            {
                line = line.Slice(0, index);
            }

            // Look for a language tag starting with '/' followed by two ASCII letters.
            for(int i = 0; i < line.Length;)
            {
                if ('/' != line[i])
                {
                    ++i;
                    continue;
                }

                // Ensure there are at least 2 characters after '/'
                if ((i + 3) <= line.Length)
                {
                    ReadOnlySpan<char> lang = line.Slice(i, 3);
                    // Check if characters after '/' are letters.
                    if (!char.IsAsciiLetter(lang[1])
                        || !char.IsAsciiLetter(lang[2]))
                    {
                        i += 3;
                        continue;
                    }

                    // If there's a character after the tag, it must be whitespace.
                    if ((i + 3) < line.Length)
                    {
                        if (!char.IsWhiteSpace(line[i + 3]))
                        {
                            i += 4;
                            continue;
                        }
                    }
                    
                    // Extract and return the 2-character language code.
                    lang = lang.Slice(1);
                    return lang.ToString();
                }
                break;
            }
            return string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GitLabWebhookCommentTask"/> class.
        /// </summary>
        /// <param name="payloadComment">The GitLab webhook payload for the comment.</param>
        public GitLabWebhookCommentTask(PayloadComment payloadComment)
        {
            payloadComment_ = payloadComment;

            // Extract the language code (e.g., 'en', 'ja') from the comment text.
            // If the language is not found or no template exists for it, use the default language.
            language_ = FindLanguage(payloadComment_.object_attributes.note);
            if (string.IsNullOrEmpty(language_) || !Context.Instance.Settings.HasTemplate(language_))
            {
                Tomlyn.Model.TomlTable? commonTable = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["common"];
                language_ = (string)commonTable["default_language"];
            }
#if false
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload_, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText("payload_comment.json", json);
#endif
        }

        /// <summary>
        /// Represents a target for review, including its diff, path, and summary.
        /// </summary>
        public class Target
        {
            public NGitLab.Models.Diff Diff { get; set; }
            public string Path { get; set; }
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
        /// Determines if a diff is a target for review.
        /// </summary>
        /// <param name="diff">The diff to check.</param>
        /// <param name="isTarget">A function to check if a path is a target.</param>
        /// <returns>A <see cref="Target"/> object if the diff is a target; otherwise, null.</returns>
        public static Target? IsTarget(NGitLab.Models.Diff diff, Func<string, bool> isTarget)
        {
            if (diff.IsDeletedFile)
            {
                return null;
            }
            if (diff.IsRenamedFile)
            {
                return null;
            }
            if (string.IsNullOrEmpty(diff.Difference))
            {
                return null;
            }
            string path;
            if (string.IsNullOrEmpty(diff.NewPath))
            {
                if (!string.IsNullOrEmpty(diff.OldPath))
                {
                    path = diff.OldPath;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                path = diff.NewPath;
            }
            if (!isTarget(path))
            {
                return null;
            }
            return new Target{ Diff = diff, Path = path, Summary = string.Empty };
        }

        /// <summary>
        /// Gets the review diffs for the specified paths and targets.
        /// </summary>
        /// <param name="paths">The paths of the files to include in the review.</param>
        /// <param name="targets">The list of target files and their summaries.</param>
        /// <returns>A formatted string containing the review diffs, or null if no template is found.</returns>
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
                    if(target.Path == path)
                    {
                        diff = target.Diff.Difference;
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

        /// <summary>
        /// Runs the GitLab webhook comment task asynchronously.
        /// </summary>
        /// <param name="serviceProvider">The service provider to resolve dependencies.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitLabWebhookCommentTask>? logger = serviceProvider.GetService<ILogger<GitLabWebhookCommentTask>>();
            logger.LogInformation($"Processing comment: {payloadComment_.object_attributes.id}");

            Context context = Context.Instance;

            NGitLab.GitLabClient gitLabClient = serviceProvider.GetService<GitLabClientService>().GitLabClient;

            // Step 1: Fetch all diffs associated with the merge request.
            NGitLab.IMergeRequestClient mergeRequestClient = gitLabClient.GetMergeRequest(payloadComment_.project.id);
            GitLabCollectionResponse<NGitLab.Models.Diff> response = mergeRequestClient.GetDiffsAsync(payloadComment_.merge_request.iid);
            List<Target> targets = new List<Target>();

            // Step 2: Filter the diffs to identify files that should be reviewed.
            await foreach (NGitLab.Models.Diff diff in response)
            {
                Target? target = IsTarget(diff, context.Settings.IsTargetExtension);
                if (null != target)
                {
                    targets.Add(target);
                }
            }
            if (targets.Count <= 0)
            {
                PostComment("No reviews are generated. There are no diffs to review.", mergeRequestClient, logger);
                return;
            }

            // Step 3: Summarize each identified diff using an AI assistant.
            logger.LogInformation($"Summarizing {targets.Count} diffs.");
            foreach (Target target in targets)
            {
                try
                {
                    Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunAsync(Agents.Type.Assistant, $"Summarize a next diff briefly in few lines.\n{target.Path}\n----\n{target.Diff.Difference}", context.CancellationToken);
                    target.Summary = agentResponse.Text;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.ToString());
                }
            }
            List<string> reviews = new List<string>();
            {
                // Step 4: Prepare the list of summarized changes for the AI planner.
                List<Change> changes = new List<Change>(targets.Count);
                foreach (Target target in targets)
                {
                    if (string.IsNullOrEmpty(target.Summary))
                    {
                        continue;
                    }
                    changes.Add(new Change() { path = target.Path, summary = target.Summary });
                }

                // Step 5: Ask the AI planner to group the files and assign them for review.
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
                    PostComment($"No reviews are generated. Fail in assigning the files to reviewers.\n{ex.Message}", mergeRequestClient, logger);
                    return;
                }

                // Step 6: Generate detailed reviews for each assigned group of files.
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

            // Step 7: Consolidate the reviews into a final organized message.
            logger.LogInformation($"Organizing {reviews.Count} reviews.");
            stringBuilder_.Clear();
            string organizedReview = string.Empty;
            if (reviews.Count == 1)
            {
                organizedReview = reviews[0];
            }
            else if(1<reviews.Count)
            {
                // Use a template to organize the combined review if available.
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
                    // Use the AI executor to create the final organized review text.
                    Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunAsync(Agents.Type.Executor, stringBuilder_.ToString(), context.CancellationToken);
                    if (!string.IsNullOrEmpty(agentResponse.Text))
                    {
                        organizedReview = agentResponse.Text;
                    }
                }
                catch(Exception ex)
                {
                    PostComment($"No reviews are generated. Fail in organizing the reviews.\n{ex.Message}", mergeRequestClient, logger);
                    logger.LogError(ex.ToString());
                }
            }
            else
            {
                PostComment("No reviews are generated. Fail in assigning the files to reviewers.", mergeRequestClient, logger);
            }

            // Step 8: Update the original comment on GitLab with the final review results.
            if (!string.IsNullOrEmpty(organizedReview))
            {
                PostComment(organizedReview, mergeRequestClient, logger);
            }
            else
            {
                // Fallback message if no review content was generated.
                stringBuilder_.Clear();
                stringBuilder_.Append("No reviews are generated.");
                PostComment(stringBuilder_.ToString(), mergeRequestClient, logger);
            }
        }

        private const int MaxLogLength = 128;
        private void PostComment(string comment, NGitLab.IMergeRequestClient mergeRequestClient, ILogger<GitLabWebhookCommentTask>? logger)
        {
            IMergeRequestCommentClient mergeRequestCommentClient = mergeRequestClient.Comments(payloadComment_.merge_request.iid);
            MergeRequestCommentEdit mergeRequestCommentEdit = new MergeRequestCommentEdit();
            mergeRequestCommentEdit.Body = comment;
            try
            {
                MergeRequestComment _ = mergeRequestCommentClient.Edit(payloadComment_.object_attributes.id, mergeRequestCommentEdit);
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

        private PayloadComment payloadComment_;
        private string language_;
        private StringBuilder stringBuilder_ = new StringBuilder();
    }
}
