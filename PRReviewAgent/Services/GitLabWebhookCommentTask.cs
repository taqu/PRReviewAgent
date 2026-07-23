using Microsoft.AspNetCore.Mvc.Rendering;
using NGitLab;
using NGitLab.Models;
using PRReviewAgent.Services.GitLabWebhook;
using PRReviewAget.Prompt;
using System.Text;

namespace PRReviewAgent.Services
{
    /// <summary>
    /// Represents the payload for a GitLab webhook comment.
    /// </summary>
    public class GitLabWebhookCommentPayload
    {
        public string object_kind { get; set; }
    }

    /// <summary>
    /// Processes a GitLab webhook comment, performs a code review using AST context, and posts the result.
    /// </summary>
    public class GitLabWebhookCommentTask
    {
        /// <summary>
        /// Finds the language code (e.g. 'en', 'ja') from the first line of a comment.
        /// </summary>
        public static string FindLanguage(string comment)
        {
            ReadOnlySpan<char> line = comment.AsSpan().Trim();
            int index = line.IndexOfAny("\n\r".AsSpan());
            if (0 <= index)
            {
                line = line.Slice(0, index);
            }

            for (int i = 0; i < line.Length;)
            {
                if ('/' != line[i])
                {
                    ++i;
                    continue;
                }

                if ((i + 3) <= line.Length)
                {
                    ReadOnlySpan<char> lang = line.Slice(i, 3);
                    if (!char.IsAsciiLetter(lang[1]) || !char.IsAsciiLetter(lang[2]))
                    {
                        i += 3;
                        continue;
                    }

                    if ((i + 3) < line.Length)
                    {
                        if (!char.IsWhiteSpace(line[i + 3]))
                        {
                            i += 4;
                            continue;
                        }
                    }

                    lang = lang.Slice(1);
                    return lang.ToString();
                }
                break;
            }
            return string.Empty;
        }

        public GitLabWebhookCommentTask(PayloadComment payloadComment)
        {
            payloadComment_ = payloadComment;

            language_ = FindLanguage(payloadComment_.object_attributes.note);
            if (string.IsNullOrEmpty(language_) || !Context.Instance.Settings.HasTemplate(language_))
            {
                Tomlyn.Model.TomlTable? commonTable = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["common"];
                language_ = (string)commonTable["default_language"];
            }
        }

        /// <summary>
        /// Determines if a diff should be reviewed. Returns null for deleted/renamed/empty files or non-target extensions.
        /// </summary>
        public static ReviewContext? IsTarget(NGitLab.Models.Diff diff, Func<string, bool> isTarget)
        {
            if (diff.IsDeletedFile) return null;
            if (diff.IsRenamedFile) return null;
            if (string.IsNullOrEmpty(diff.Difference)) return null;

            string path;
            if (string.IsNullOrEmpty(diff.NewPath))
            {
                if (!string.IsNullOrEmpty(diff.OldPath))
                    path = diff.OldPath;
                else
                    return null;
            }
            else
            {
                path = diff.NewPath;
            }

            if (!isTarget(path)) return null;

            return new ReviewContext
            {
                Path = path,
                Filename = Path.GetFileName(path),
                Diff = diff.Difference,
                ChangedFile = string.Empty,
                PairFile = string.Empty,
                PairPath = string.Empty,
            };
        }

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitLabWebhookCommentTask>? logger = serviceProvider.GetService<ILogger<GitLabWebhookCommentTask>>();
            logger.LogInformation($"Processing comment: {payloadComment_.object_attributes.id}");

            Context context = Context.Instance;
            NGitLab.GitLabClient gitLabClient = serviceProvider.GetService<GitLabClientService>().GitLabClient;

            // Step 1: Fetch all diffs for the merge request.
            NGitLab.IMergeRequestClient mergeRequestClient = gitLabClient.GetMergeRequest(payloadComment_.project.id);
            GitLabCollectionResponse<NGitLab.Models.Diff> response = mergeRequestClient.GetDiffsAsync(payloadComment_.merge_request.iid);
            List<ReviewContext> reviewContexts = new List<ReviewContext>();

            // Step 2: Filter to files that should be reviewed.
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

            // Step 3: Fetch full file contents from the source branch.
            IRepositoryClient repository = gitLabClient.GetRepository(payloadComment_.merge_request.target_project_id);
            string sourceBranch = payloadComment_.merge_request.source_branch;
            logger.LogInformation($"Fetching file contents for {reviewContexts.Count} files.");
            foreach (ReviewContext reviewContext in reviewContexts)
            {
                try
                {
                    FileData file = await repository.Files.GetAsync(reviewContext.Path, sourceBranch, cancellationToken);
                    reviewContext.ChangedFile = file.DecodedContent;
                }
                catch { }
            }

            // Step 4: Resolve related (pair) files deterministically.
            ContextCollector.FindPair(reviewContexts);
            await FindPairAsync(reviewContexts, repository.Files, sourceBranch, cancellationToken);

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
            reviewRequest.MergeRequestTitle = payloadComment_.merge_request.title ?? string.Empty;
            reviewRequest.MergeRequestDescription = payloadComment_.merge_request.description;
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
                PostComment("No reviews are generated.", mergeRequestClient, logger);
                return;
            }

            stringBuilder_.Clear();
            foreach (string review in reviews)
            {
                stringBuilder_.Append(review).Append("\n\n---\n\n");
            }
            string organizedReview = stringBuilder_.ToString();
            PostComment(organizedReview, mergeRequestClient, logger);
            logger.LogInformation($"Final review:\n{organizedReview}");
        }

        private static async Task<FileData?> GetFileAsync(IFilesClient filesClient, string path, string sourceBranch, CancellationToken cancellationToken)
        {
            try {
                return await filesClient.GetAsync(path, sourceBranch, cancellationToken);
            }
            catch {
                return null;
            }
        }

        private static async Task FindPairAsync(List<ReviewContext> reviewContexts, IFilesClient filesClient, string sourceBranch, CancellationToken cancellationToken)
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
                FileData? file = null;
                if(null != (file = await GetFileAsync(filesClient, pairPath, sourceBranch, cancellationToken)))
                {
                    reviewContext.PairPath = file.Path;
                    reviewContext.PairFile = file.DecodedContent;
                    continue;
                }

                pairPath = GuessPair2(reviewContext.Path);
                if (pairPath == null){
                    continue;
                }
                if(null != (file = await GetFileAsync(filesClient, pairPath, sourceBranch, cancellationToken)))
                {
                    reviewContext.PairPath = file.Path;
                    reviewContext.PairFile = file.DecodedContent;
                    continue;
                }

                pairPath = GuessPair3(reviewContext.Path);
                if (pairPath == null){
                    continue;
                }
                if(null != (file = await GetFileAsync(filesClient, pairPath, sourceBranch, cancellationToken)))
                {
                    reviewContext.PairPath = file.Path;
                    reviewContext.PairFile = file.DecodedContent;
                    continue;
                }

                pairPath = GuessPair4(reviewContext.Path);
                if (pairPath == null){
                    continue;
                }
                if(null != (file = await GetFileAsync(filesClient, pairPath, sourceBranch, cancellationToken)))
                {
                    reviewContext.PairPath = file.Path;
                    reviewContext.PairFile = file.DecodedContent;
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
                        if (span[length] == '\n' || span[length] == '\r') break;
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
