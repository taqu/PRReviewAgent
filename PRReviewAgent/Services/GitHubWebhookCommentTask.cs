using NGitLab;
using NGitLab.Impl;
using NGitLab.Models;
using Octokit;
using PRReviewAgent.Services.GitHubWebhook;
using PRReviewAgent.Services.GitLabWebhook;
using PRReviewAget.Prompt;
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
        /// Represents a collection of changes.
        /// </summary>
        /// <param name="changes">The array of changes.</param>
        public record Changes(
            [Description("Changed file paths and summaries")]
            Change[] changes
        );

        /// <summary>
        /// Determines if a diff should be reviewed. Returns null for deleted/renamed/empty files or non-target extensions.
        /// </summary>
        public static ReviewContext? IsTarget(PullRequestFile requestFile, Func<string, bool> isTarget)
        {
            bool isDeleted = !string.IsNullOrEmpty(requestFile.PreviousFileName) && string.IsNullOrEmpty(requestFile.FileName);
            bool isMoved = !string.IsNullOrEmpty(requestFile.PreviousFileName) && !string.IsNullOrEmpty(requestFile.FileName) && requestFile.PreviousFileName != requestFile.FileName && string.IsNullOrEmpty(requestFile.Patch);
            if (isDeleted || isMoved)
            {
                return null;
            }

            if (!isTarget(requestFile.FileName))
            {
                return null;
            }

            return new ReviewContext
            {
                Path = requestFile.FileName,
                Filename = Path.GetFileName(requestFile.FileName),
                Diff = requestFile.Patch,
                ChangedFile = string.Empty,
                PairFile = string.Empty,
                PairPath = string.Empty,
            };
        }

        public static async Task<string> GetPullRequestFileContentAsync(
            GitHubClient client,
            long repositoryId,
            string reference,
            string filePath)
        {
            try
            {
                IReadOnlyList<RepositoryContent> fileContents = await client.Repository.Content.GetAllContentsByRef(repositoryId, filePath, reference);
                RepositoryContent targetFile = fileContents[0];
                if (targetFile.Content != null)
                {
                    return targetFile.Content;
                }
                else
                {
                    byte[] rawBytes = Convert.FromBase64String(targetFile.EncodedContent);
                    return Encoding.UTF8.GetString(rawBytes);
                }
            }
            catch (ApiException ex)
            {
                return string.Empty;
            }
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
            List<ReviewContext> reviewContexts = new List<ReviewContext>();

            // Step 2: Identify files that are suitable for review (target extensions and not deleted/moved).
            foreach (PullRequestFile file in files)
            {
                ReviewContext? reviewContext = IsTarget(file, context.Settings.IsTargetExtension);
                if (null != reviewContext)
                {
                    reviewContexts.Add(reviewContext);
                }
            }
            if (reviewContexts.Count <= 0)
            {
                await PostCommentAsync("No reviews are generated. There are no diffs to review.", gitHubClient, logger);
                return;
            }

            // Step 3: Fetch full file contents from the source branch.
            logger.LogInformation($"Fetching file contents for {reviewContexts.Count} files.");
            PullRequest pullRequest = await gitHubClient.PullRequest.Get(payloadIssueComment_.repository.id, pullRequestNumber_);
            foreach (ReviewContext reviewContext in reviewContexts)
            {
                try
                {
                    string file = await GetPullRequestFileContentAsync(gitHubClient, payloadIssueComment_.repository.id, pullRequest.Head.Sha, reviewContext.Path);
                    if (string.IsNullOrEmpty(file))
                    {
                        reviewContext.ChangedFile = file;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.ToString());
                }
            }

            // Step 4: Resolve related (pair) files deterministically.
            ContextCollector.FindPair(reviewContexts);
            await FindPairAsync(reviewContexts, gitHubClient, payloadIssueComment_.repository.id, pullRequest.Head.Sha, cancellationToken);

            // Step 5: Extract AST context for each file.
            logger.LogInformation($"Extracting AST context for {reviewContexts.Count} files.");
            foreach (ReviewContext reviewContext in reviewContexts)
            {
                try
                {
                    (string json, string expandedDiff) = AstContextExtractor.Run(
                        reviewContext.Path,
                        reviewContext.ChangedFile,
                        reviewContext.Path,
                        reviewContext.Diff,
                        reviewContext.PairPath,
                        reviewContext.PairFile);
                    reviewContext.AstJson = json;
                    reviewContext.ExpandedDiff = expandedDiff;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.ToString());
                }
            }

            // Step 6: Build file groups deterministically by base filename.
            logger.LogInformation($"Building file groups for {reviewContexts.Count} files.");
            ReviewRequest reviewRequest = new ReviewRequest();
            reviewRequest.MergeRequestTitle = payloadIssueComment_.issue.title ?? string.Empty;
            reviewRequest.MergeRequestDescription = payloadIssueComment_.issue.body?? string.Empty;
            reviewRequest.ReviewRules = Context.Instance.Settings.GetReviewTemplate(language_);

            Dictionary<string, FileGroup> groups = new Dictionary<string, FileGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (ReviewContext reviewContext in reviewContexts)
            {
                string baseName = Path.GetFileNameWithoutExtension(reviewContext.Path);
                if (!groups.TryGetValue(baseName, out FileGroup? group))
                {
                    group = new FileGroup { Topic = baseName };
                    groups[baseName] = group;
                    reviewRequest.FileGroups.Add(group);
                }
                group.ReviewContexts.Add(reviewContext);
            }

            // Step 7: Build prompts for each group.
            PromptBuilder.Build(reviewRequest, stringBuilder_);


            // Step 8: Execute review for each file group.
            List<string> reviews = new List<string>();
            logger.LogInformation($"Generating reviews for {reviewRequest.FileGroups.Count} file groups.");
            foreach (FileGroup fileGroup in reviewRequest.FileGroups)
            {
                if (string.IsNullOrEmpty(fileGroup.Prompt)) continue;
                try
                {
                    Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunAsync(Agents.Type.Executor, fileGroup.Prompt, context.CancellationToken);
                    if (string.IsNullOrEmpty(agentResponse.Text))
                    {
                        logger.LogInformation($"No review generated for {fileGroup.ReviewContexts.Count} files.");
                        continue;
                    }
                    stringBuilder_.Clear();
                    stringBuilder_.Append($"## {fileGroup.Topic}\n\n");
                    stringBuilder_.Append(agentResponse.Text);
                    reviews.Add(stringBuilder_.ToString());
                    logger.LogInformation($"Generated review for {fileGroup.ReviewContexts.Count} files.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.ToString());
                }
            }

            // Step 9: Merge reviews and post comment.
            if (reviews.Count <= 0)
            {
                await PostCommentAsync("No reviews are generated.", gitHubClient, logger);
                return;
            }

            stringBuilder_.Clear();
            foreach (string review in reviews)
            {
                stringBuilder_.Append(review).Append("\n\n---\n\n");
            }
            string organizedReview = stringBuilder_.ToString();
            await PostCommentAsync(organizedReview, gitHubClient, logger);
            logger.LogInformation($"Final review:\n{organizedReview}");
        }

        private static async Task FindPairAsync(List<ReviewContext> reviewContexts, GitHubClient client, long repositoryId, string reference, CancellationToken cancellationToken)
        {
            foreach (ReviewContext reviewContext in reviewContexts)
            {
                if(!string.IsNullOrEmpty(reviewContext.PairPath))
                {
                    continue;
                }
                string? pairPath = GuessPair1(reviewContext.Path);
                if (pairPath == null){
                    continue;
                }
                string? file = null;
                if(null != (file = await GetPullRequestFileContentAsync(client, repositoryId, reference, pairPath)))
                {
                    reviewContext.PairPath = pairPath;
                    reviewContext.PairFile = file;
                    continue;
                }

                pairPath = GuessPair2(reviewContext.Path);
                if (pairPath == null){
                    continue;
                }
                if(null != (file = await GetPullRequestFileContentAsync(client, repositoryId, reference, pairPath)))
                {
                    reviewContext.PairPath = pairPath;
                    reviewContext.PairFile = file;
                    continue;
                }

                pairPath = GuessPair3(reviewContext.Path);
                if (pairPath == null){
                    continue;
                }
                if(null != (file = await GetPullRequestFileContentAsync(client, repositoryId, reference, pairPath)))
                {
                    reviewContext.PairPath = pairPath;
                    reviewContext.PairFile = file;
                    continue;
                }

                pairPath = GuessPair4(reviewContext.Path);
                if (pairPath == null){
                    continue;
                }
                if(null != (file = await GetPullRequestFileContentAsync(client, repositoryId, reference, pairPath)))
                {
                    reviewContext.PairPath = pairPath;
                    reviewContext.PairFile = file;
                    continue;
                }
            }
        }

        private static string? GuessPair1(string path)
        {
            string ext = Path.GetExtension(path);
            switch (ext)
            {
                case ".cpp":
                case ".cc":
                case ".cxx":
                case ".c":
                    return Path.ChangeExtension(path, ".h").Replace("src", "include");
                case ".h":
                case ".hpp":
                    return Path.ChangeExtension(path, ".cpp").Replace("include", "src");
                default:
                    return null;
            }
        }

        private static string? GuessPair2(string path)
        {
            string ext = Path.GetExtension(path);
            switch (ext)
            {
                case ".cpp":
                case ".cc":
                case ".cxx":
                case ".c":
                    return Path.ChangeExtension(path, ".h").Replace("src", "include");
                case ".h":
                    return Path.ChangeExtension(path, ".c").Replace("include", "src");
                case ".hpp":
                    return Path.ChangeExtension(path, ".cpp").Replace("include", "src");
                default:
                    return null;
            }
        }

        private static string? GuessPair3(string path)
        {
            string ext = Path.GetExtension(path);
            switch (ext)
            {
                case ".cpp":
                case ".cc":
                case ".cxx":
                case ".c":
                    return Path.ChangeExtension(path, ".h").Replace("source", "include");
                case ".h":
                case ".hpp":
                    return Path.ChangeExtension(path, ".cpp").Replace("include", "source");
                default:
                    return null;
            }
        }

        private static string? GuessPair4(string path)
        {
            string ext = Path.GetExtension(path);
            switch (ext)
            {
                case ".cpp":
                case ".cc":
                case ".cxx":
                case ".c":
                    return Path.ChangeExtension(path, ".h").Replace("source", "include");
                case ".h":
                    return Path.ChangeExtension(path, ".c").Replace("include", "source");
                case ".hpp":
                    return Path.ChangeExtension(path, ".cpp").Replace("include", "source");
                default:
                    return null;
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
