using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using NGitLab;
using NGitLab.Impl;
using NGitLab.Models;
using Octokit;
using PRReviewAgent.Services.GitLabWebhook;
using System.ComponentModel;
using System.Formats.Tar;
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
            public string topic { get; set; }
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
        public static ReviewContext? IsTarget(NGitLab.Models.Diff diff, Func<string, bool> isTarget)
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
            return new ReviewContext
            {
                Path = path,

                Diff = diff.Difference,

                ChangedFile = string.Empty,

                PairFile = string.Empty,

                PairPath = string.Empty,
            };
        }

        /// <summary>
        /// Gets the review diffs for the specified paths and targets.
        /// </summary>
        /// <param name="paths">The paths of the files to include in the review.</param>
        /// <param name="targets">The list of target files and their summaries.</param>
        /// <returns>A formatted string containing the review diffs, or null if no template is found.</returns>
        public FileGroup? GetReviewFiles(Assign assign, List<ReviewContext> contexts)
        {
            string? template = Context.Instance.Settings.GetReviewTemplate(language_);
            if(null == template)
            {
                return null;
            }
            FileGroup files = new FileGroup();
            files.Topic = assign.topic;
            foreach (string path in assign.paths)
            {
                ReviewContext? reviewContext = null;
                foreach(ReviewContext context in contexts)
                {
                    if(context.Path == path)
                    {
                        reviewContext = context;
                        break;
                    }
                }
                if(null == reviewContext)
                {
                    continue;
                }
                files.ReviewContexts.Add(reviewContext);
            }
            return files;
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
            List<ReviewContext> reviewContexts = new List<ReviewContext>();

            // Step 2: Filter the diffs to identify files that should be reviewed.
            await foreach (NGitLab.Models.Diff diff in response)
            {
                ReviewContext? reviewContext = IsTarget(diff, context.Settings.IsTargetExtension);
                if (null != reviewContext)
                {
                    reviewContexts.Add(reviewContext);
                }
            }
            if (reviewContexts.Count <= 0)
            {
                PostComment("No reviews are generated. There are no diffs to review.", mergeRequestClient, logger);
                return;
            }

            // Step 3: Analyze each identified diff using an AI assistant.
            logger.LogInformation($"Summarizing {reviewContexts.Count} diffs.");
            foreach (ReviewContext reviewContext in reviewContexts)
            {
                try
                {
                    Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunAsync(Agents.Type.Assistant, $"Analyze this diff.\\nReturn:\\n\\n- What is modified?\\n- Why might it be modified?\\n- What APIs/classes/functions are involved?\\n- Which identifiers look important?\\n- What assumptions does this change make?\\n\\nKeep under 200 words.\n{reviewContext.Path}\n----\n{reviewContext.Diff}", context.CancellationToken);
                    reviewContext.Summary = agentResponse.Text;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.ToString());
                }
            }
            await ContextCollector.CollectAsync(gitLabClient, payloadComment_.merge_request, reviewContexts);

            List<string> reviews = new List<string>();
            {
                // Step 4: Prepare the list of summarized changes for the AI planner.
                List<Change> changes = new List<Change>(reviewContexts.Count);
                foreach (ReviewContext reviewContext in reviewContexts)
                {
                    if (string.IsNullOrEmpty(reviewContext.Summary))
                    {
                        continue;
                    }
                    changes.Add(new Change() { path = reviewContext.Path, summary = reviewContext.Summary });
                }

                // Step 5: Ask the AI planner to group the files and assign them for review.
                logger.LogInformation($"Assigning {changes.Count} changes to reviewers.");
                string changeFilePaths = Newtonsoft.Json.JsonConvert.SerializeObject(new Changes(changes.ToArray()));
                Assignments? assignments = null;
                try
                {
                    assignments = await context.Agents.RunAsync<Assignments>(Agents.Type.Planner, $"Group next diffs in the pull request by relevance or topic, and assign file paths to each reviewers.\n```json\n{changeFilePaths}```", context.CancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.ToString());
                    PostComment($"No reviews are generated. Fail in assigning the files to reviewers.\n{ex.Message}", mergeRequestClient, logger);
                    return;
                }

                ReviewRequest reviewRequest = new ReviewRequest();
                reviewRequest.ReviewRules = Context.Instance.Settings.GetReviewTemplate(language_);

                // Step 6: Generate review request for each assigned group of files.
                logger.LogInformation($"Generating reviews for {assignments.assigns.Length} assignments.");
                foreach (Assign assign in assignments.assigns)
                {
                    FileGroup? files = GetReviewFiles(assign, reviewContexts);
                    if (null == files || files.ReviewContexts.Count<=0)
                    {
                        logger.LogInformation($"No assignment is generated for {assign.paths.Length} files.");
                        continue;
                    }
                    reviewRequest.FileGroups.Add(files);
                }

                // Step 7: Generate detailed reviews for each assigned group of files.
                PromptBuilder.Build(reviewRequest, stringBuilder_);
                logger.LogInformation($"Generating reviews for {assignments.assigns.Length} assignments.");
                foreach (FileGroup fileGroup in reviewRequest.FileGroups)
                {
                    if (string.IsNullOrEmpty(fileGroup.Prompt))
                    {
                        continue;
                    }
                    try
                    {
                        Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunAsync(Agents.Type.Executor, fileGroup.Prompt, context.CancellationToken);
                        if (string.IsNullOrEmpty(agentResponse.Text))
                        {
                            logger.LogInformation($"No review is generated for {fileGroup.ReviewContexts.Count} files.");
                            continue;
                        }
                        stringBuilder_.Clear();
                        if (!string.IsNullOrEmpty(fileGroup.Topic))
                        {
                            stringBuilder_.Append($"Topic: {fileGroup.Topic}\n------------------------------------\n");
                        }
                        stringBuilder_.Append(agentResponse.Text);
                        reviews.Add(stringBuilder_.ToString());
                        logger.LogInformation($"Generated review for {fileGroup.ReviewContexts.Count} files.\n{agentResponse.Text}");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex.ToString());
                        continue;
                    }
                }
            }

            // Step 8: Consolidate the reviews into a final organized message.
            logger.LogInformation($"Organizing {reviews.Count} reviews.");
            stringBuilder_.Clear();
            string organizedReview = string.Empty;
            if(1<=reviews.Count)
            {
                // Use a template to organize the combined review if available.
                foreach (string review in reviews)
                {
                    stringBuilder_.Append(review).Append("\n\n");
                }
                organizedReview = stringBuilder_.ToString();
            }
            else
            {
                PostComment("No reviews are generated. Fail in assigning the files to reviewers.", mergeRequestClient, logger);
            }

            // Step 8: Update the original comment on GitLab with the final review results.
            if (!string.IsNullOrEmpty(organizedReview))
            {
                PostComment(organizedReview, mergeRequestClient, logger);
                logger.LogInformation($"Final review:\n{organizedReview}");
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
