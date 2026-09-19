using NGitLab;
using NGitLab.Impl;
using NGitLab.Models;
using Octokit;
using OpenAI.Chat;
using PRReviewAgent.Prompt;
using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.GitHubWebhook;
using PRReviewAgent.Services.GitLabWebhook;
using PRReviewAgent.Services.ReviewStatus;
using PRReviewAgent.Services.Statistics;
using PRReviewAget.Prompt;
using PRReviewAgent.Services.AutoReview;
using System;
using System.ComponentModel;
using System.Diagnostics;
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

        public GitHubWebhookCommentTask(PayloadPullRequestEvent prEvent, string language)
        {
            pullRequestNumber_ = prEvent.number;
            language_ = language;
            if (string.IsNullOrEmpty(language_) || !Context.Instance.Settings.HasTemplate(language_))
            {
                Tomlyn.Model.TomlTable? commonTable = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["common"];
                language_ = (string)commonTable["default_language"];
            }
            payloadIssueComment_ = new GitHubWebhook.PayloadIssueComment
            {
                repository = prEvent.repository,
                issue = new GitHubWebhook.PayloadIssue
                {
                    title = prEvent.pull_request.title,
                    body = prEvent.pull_request.body ?? string.Empty,
                    pull_request = new GitHubWebhook.PayloadPullRequest { url = string.Empty },
                },
                comment = new GitHubWebhook.PayloadComment { id = 0, body = string.Empty },
            };
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

            // Resolve project identity early — needed for status comment service.
            string externalProjectId = $"github:{payloadIssueComment_.repository.id}";
            string mergeRequestId = $"github/{payloadIssueComment_.repository.id}/{pullRequestNumber_}";
            ProjectRepository? projectRepository = serviceProvider.GetService<ProjectRepository>();
            AutoImprove.Project? project = null;
            if (projectRepository != null)
            {
                try
                {
                    project = await projectRepository.GetOrCreateAsync(
                        externalProjectId,
                        payloadIssueComment_.repository.name ?? externalProjectId,
                        payloadIssueComment_.repository.html_url,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to resolve project");
                }
            }

            IReviewStatusCommentService? statusService = serviceProvider.GetService<IReviewStatusCommentService>();
            GitHubReviewCommentProvider? statusProvider = project != null
                ? new GitHubReviewCommentProvider(gitHubClient, payloadIssueComment_.repository.id, pullRequestNumber_)
                : null;

            if (statusService != null && statusProvider != null)
            {
                try { await statusService.BeginReviewAsync(statusProvider, project!.Id, mergeRequestId, cancellationToken); }
                catch (Exception ex) { logger.LogError(ex, "Failed to post review begin status comment"); }
            }

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
                if (statusService != null && statusProvider != null)
                {
                    try { await statusService.CompleteReviewAsync(statusProvider, project!.Id, mergeRequestId, "No reviews are generated. There are no diffs to review.", cancellationToken); }
                    catch (Exception ex) { logger.LogError(ex, "Failed to post review complete status comment"); }
                }
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
                    if (!string.IsNullOrEmpty(file))
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

            // Step 6: Build file groups using semantic grouping.
            logger.LogInformation($"Building file groups for {reviewContexts.Count} files.");
            ReviewRequest reviewRequest = new ReviewRequest();
            reviewRequest.MergeRequestTitle = payloadIssueComment_.issue.title ?? string.Empty;
            reviewRequest.MergeRequestDescription = payloadIssueComment_.issue.body?? string.Empty;
            reviewRequest.ReviewRulesTurn1 = Context.Instance.Settings.GetReview1Template("en");
            reviewRequest.ReviewRulesTurn2 = Context.Instance.Settings.GetReview2Template("en");
            reviewRequest.ReviewRulesTurn3 = Context.Instance.Settings.GetReview3Template(language_);

            var groupingConfig = Context.Instance.Settings.GetGroupingConfig();
            IReadOnlyList<FileGroup> builtGroups = PRReviewAgent.Services.Grouping.SemanticReviewGroupBuilder.Build(reviewContexts, groupingConfig, logger);
            foreach (FileGroup group in builtGroups)
            {
                reviewRequest.FileGroups.Add(group);
                if (group.GroupingReasons.Count > 0)
                    logger.LogDebug("Group {Topic}: {Reasons}", group.Topic, string.Join("; ", group.GroupingReasons));
            }
            logger.LogInformation("Built {GroupCount} semantic review groups from {FileCount} files.",
                builtGroups.Count, reviewContexts.Count);

            // Step 7: Execute review for each file group.
            IReviewExecutionRecorder? recorder = serviceProvider.GetService<IReviewExecutionRecorder>();
            IReviewTurnRecorder? turnRecorder = serviceProvider.GetService<IReviewTurnRecorder>();
            IRuleSearchExecutionRecorder? searchRecorder = serviceProvider.GetService<IRuleSearchExecutionRecorder>();
            IReviewRuleUsageRepository? usageRepo = serviceProvider.GetService<IReviewRuleUsageRepository>();
            long? executionId = null;
            DateTimeOffset reviewStartedAt = DateTimeOffset.UtcNow;
            Stopwatch reviewStopwatch = Stopwatch.StartNew();
            if (recorder != null && project != null)
            {
                try { executionId = await recorder.StartAsync(project.Id, mergeRequestId, reviewStartedAt, cancellationToken); }
                catch (Exception ex) { logger.LogError(ex, "Failed to start review execution record"); }
            }

            // Retrieve learned rules via RAG and attach to request (after execution start so executionId is available).
            RuleRetrievalService? ruleRetrievalService = serviceProvider.GetService<RuleRetrievalService>();
            RuleLifecycleService? ruleLifecycleService = serviceProvider.GetService<RuleLifecycleService>();
            // selectedRuleIds: IDs of rules included in the prompt (for Detection validation).
            HashSet<string> selectedRuleIds = new HashSet<string>();
            // candidateToRuleId: maps candidate_id ("c0", "c1"...) to rule_id for Selection attribution.
            Dictionary<string, string> candidateToRuleId = new Dictionary<string, string>();
            if (ruleRetrievalService != null && project != null)
            {
                try
                {
                    string queryContext = string.Join("\n", reviewContexts
                        .Where(ctx => !string.IsNullOrEmpty(ctx.AstJson))
                        .Select(ctx => ctx.AstJson));
                    if (string.IsNullOrEmpty(queryContext))
                        queryContext = string.Join("\n", reviewContexts.Select(ctx => ctx.Path));

                    RuleSearchResult searchResult = await ruleRetrievalService.GetRelevantRulesAsync(
                        queryContext, project.Id,
                        reviewExecutionId: executionId,
                        searchRecorder: searchRecorder,
                        cancellationToken: cancellationToken);
                    if (searchResult.Selected.Count > 0)
                    {
                        reviewRequest.LearnedRules = RuleRetrievalService.FormatRulesForPrompt(searchResult.Selected, language_);
                        await ruleLifecycleService?.TrackReviewedRulesAsync(mergeRequestId, searchResult.Selected, cancellationToken);
                        selectedRuleIds = searchResult.Selected.Select(r => r.Id).ToHashSet();
                    }

                    // Persist per-rule usage rows for all candidates (threshold survivors).
                    if (usageRepo != null && executionId.HasValue && project != null && searchResult.Candidates.Count > 0)
                    {
                        try
                        {
                            DateTimeOffset now = DateTimeOffset.UtcNow;
                            List<ReviewRuleUsage> usageRows = searchResult.Candidates.Select(c => new ReviewRuleUsage
                            {
                                ReviewExecutionId = executionId.Value,
                                ProjectId = project.Id,
                                RuleId = c.Rule.Id,
                                SimilarityScore = c.Score,
                                UsedInPrompt = selectedRuleIds.Contains(c.Rule.Id),
                                ProducedCandidate = false,
                                ProducedFinalFinding = false,
                                CreatedAt = now,
                            }).ToList();
                            await usageRepo.AddRangeAsync(usageRows, cancellationToken);
                            logger.LogDebug("Recorded usage for {RuleCount} learned rules in review {ReviewExecutionId}",
                                usageRows.Count, executionId.Value);
                        }
                        catch (Exception ex) { logger.LogError(ex, "Failed to persist rule usage rows"); }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to retrieve learned rules");
                }
            }

            PRReviewAgent.Prompt.Turn1.ReviewBudgetConfig budget = Context.Instance.Settings.GetReviewBudgetConfig();
            PRReviewAgent.Services.Verification.AdaptiveVerificationConfig adaptiveConfig = Context.Instance.Settings.GetAdaptiveVerificationConfig();
            int globalMaxConcurrent = adaptiveConfig.Policy == PRReviewAgent.Services.Verification.VerificationPolicy.Adaptive
                ? adaptiveConfig.HardMaxConcurrentBatches
                : budget.MaxConcurrentBatches;
            System.Threading.SemaphoreSlim globalVerificationSem = new System.Threading.SemaphoreSlim(Math.Max(1, globalMaxConcurrent));
            List<string> reviews = new List<string>();
            int totalCandidates = 0, totalSelected = 0;
            logger.LogInformation($"Generating reviews for {reviewRequest.FileGroups.Count} file groups.");
            try
            {
                foreach (FileGroup fileGroup in reviewRequest.FileGroups)
                {
                    var (promptTurn1, turn1Metrics) = PromptBuilder.BuildTurn1WithMetrics(reviewRequest, fileGroup, stringBuilder_, budget);
                    logger.LogInformation(
                        "Turn1Context: files={Files} full={Full} partial={Partial} diff_only={DiffOnly} estimated_chars={Chars} semantic_chars={SemChars} truncated={Truncated}",
                        turn1Metrics.FileCount, turn1Metrics.FullFileCount, turn1Metrics.PartialFileCount,
                        turn1Metrics.DiffOnlyCount, turn1Metrics.EstimatedSourceChars, turn1Metrics.SemanticSummaryChars, turn1Metrics.Truncated);
                    if (string.IsNullOrEmpty(promptTurn1)) continue;
                    try
                    {
                        // Detection turn
                        DateTimeOffset detectionStart = DateTimeOffset.UtcNow;
                        Stopwatch detectionSw = Stopwatch.StartNew();
                        long? detectionTurnId = null;
                        if (turnRecorder != null && executionId.HasValue)
                        {
                            try { detectionTurnId = await turnRecorder.StartAsync(executionId.Value, ReviewTurnType.Detection, context.Agents.Model, detectionStart, cancellationToken); }
                            catch (Exception ex) { logger.LogError(ex, "Failed to start Detection turn record"); }
                        }

                        CandidateResponse? issuesResponse = null;
                        int? detInputTokens = null, detOutputTokens = null;
                        Exception? detException = null;
                        try
                        {
                            (issuesResponse, detInputTokens, detOutputTokens) = await context.Agents.RunJsonWithUsageAsync<CandidateResponse>(promptTurn1, context.CancellationToken);
                        }
                        catch (Exception ex) { detException = ex; throw; }
                        finally
                        {
                            detectionSw.Stop();
                            if (turnRecorder != null && detectionTurnId.HasValue)
                            {
                                try
                                {
                                    if (detException != null)
                                        await turnRecorder.CompleteFailureAsync(detectionTurnId.Value, detInputTokens, detOutputTokens, ClassifyError(detException), DateTimeOffset.UtcNow, detectionSw.ElapsedMilliseconds, cancellationToken);
                                    else
                                        await turnRecorder.CompleteSuccessAsync(detectionTurnId.Value, detInputTokens, detOutputTokens, issuesResponse?.issues.Length ?? 0, DateTimeOffset.UtcNow, detectionSw.ElapsedMilliseconds, cancellationToken);
                                }
                                catch (Exception rex) { logger.LogError(rex, "Failed to complete Detection turn record"); }
                            }
                        }

                        if (null == issuesResponse || issuesResponse.issues.Length <= 0)
                        {
                            logger.LogInformation($"No review generated for {fileGroup.Topic}:{fileGroup.ReviewContexts.Count} files.");
                            PromptBuilder.AddNotFound(fileGroup, reviews, language_, stringBuilder_);
                            continue;
                        }

                        // Assign candidate IDs and validate rule attribution from Detection.
                        for (int ci = 0; ci < issuesResponse.issues.Length; ci++)
                        {
                            issuesResponse.issues[ci].candidate_id = $"c{ci}";
                            // Validate rule_id: ignore attributions not in the allowed set.
                            string? rid = issuesResponse.issues[ci].rule_id;
                            if (rid != null && !selectedRuleIds.Contains(rid))
                                issuesResponse.issues[ci].rule_id = null;
                            if (issuesResponse.issues[ci].rule_id != null)
                                candidateToRuleId[$"c{ci}"] = issuesResponse.issues[ci].rule_id!;
                        }

                        // Mark produced_candidate for rules that appeared in Detection output.
                        if (usageRepo != null && executionId.HasValue && project != null)
                        {
                            string[] detectedRuleIds = issuesResponse.issues
                                .Where(i => i.rule_id != null)
                                .Select(i => i.rule_id!)
                                .Distinct()
                                .ToArray();
                            if (detectedRuleIds.Length > 0)
                            {
                                try { await usageRepo.MarkProducedCandidateAsync(executionId.Value, project.Id, detectedRuleIds, cancellationToken); }
                                catch (Exception ex) { logger.LogError(ex, "Failed to mark produced_candidate"); }
                            }
                        }

                        // Deterministic pre-filtering
                        var (filteredCandidates, rejectedCandidates) = PRReviewAgent.Prompt.CandidatePreFilter.Filter(issuesResponse.issues);
                        if (rejectedCandidates.Length > 0)
                        {
                            foreach (var (rc, reason) in rejectedCandidates)
                                logger.LogDebug("Pre-filtered candidate {Id} ({Loc}): {Reason}", rc.candidate_id ?? "?", rc.location, reason);
                        }

                        // Candidate cap: select top N by priority category
                        CandidateIssue[] candidatesForVerification = filteredCandidates;
                        if (candidatesForVerification.Length > budget.MaxVerificationCandidatesPerGroup)
                        {
                            candidatesForVerification = filteredCandidates
                                .OrderBy(c => PRReviewAgent.Prompt.CandidatePreFilter.GetCategoryPriority(c.category))
                                .Take(budget.MaxVerificationCandidatesPerGroup)
                                .ToArray();
                            int skipped = filteredCandidates.Length - candidatesForVerification.Length;
                            logger.LogInformation("Candidate cap: kept {Kept} of {Total}, skipped {Skipped} (SkippedBudget)",
                                candidatesForVerification.Length, filteredCandidates.Length, skipped);
                        }

                        int fileGroupCandidates = issuesResponse.issues.Length;
                        totalCandidates += fileGroupCandidates;

                        // Phase 8: Context resolution + adaptive planning + batched verification
                        List<PRReviewAgent.Services.Verification.VerificationBatchItem> workItems = new();
                        foreach (CandidateIssue candidate in candidatesForVerification)
                        {
                            PRReviewAgent.Prompt.VerificationContext vCtx = new PRReviewAgent.Prompt.VerificationContextResolver().Resolve(candidate, fileGroup.ReviewContexts, budget, logger);
                            if (vCtx.Items.Count == 0)
                            {
                                logger.LogWarning("Skipping candidate {Id}: no context resolved (NoContext)", candidate.candidate_id ?? "?");
                                continue;
                            }
                            workItems.Add(new PRReviewAgent.Services.Verification.VerificationBatchItem { Candidate = candidate, Context = vCtx });
                        }

                        int planMaxCandidates = budget.MaxCandidatesPerBatch;
                        int planMaxChars = budget.MaxBatchInputChars;

                        if (adaptiveConfig.Policy == PRReviewAgent.Services.Verification.VerificationPolicy.Adaptive && workItems.Count > 0)
                        {
                            try
                            {
                                PRReviewAgent.Services.Verification.ReviewWorkloadProfile profile =
                                    PRReviewAgent.Services.Verification.ReviewWorkloadProfileBuilder.Build(workItems);
                                PRReviewAgent.Services.Verification.VerificationExecutionPlan activePlan =
                                    PRReviewAgent.Services.Verification.VerificationExecutionPlanner.Plan(profile, adaptiveConfig, budget, logger);
                                logger.LogInformation(
                                    "VerificationPlan: mode={Mode} candidates={Candidates} estimated_chars={Chars} avg_chars={Avg} max_chars={Max} batch_size={BatchSize} concurrency={Concurrency} reason={Reason}",
                                    activePlan.Mode, profile.CandidateCount, profile.EstimatedVerificationCharsTotal,
                                    profile.EstimatedVerificationCharsAverage, profile.EstimatedVerificationCharsMax,
                                    activePlan.MaxCandidatesPerBatch, activePlan.MaxConcurrentBatches, activePlan.Reason);
                                if (activePlan.Mode == PRReviewAgent.Services.Verification.VerificationExecutionMode.NoOp)
                                {
                                    PromptBuilder.AddNotFound(fileGroup, reviews, language_, stringBuilder_);
                                    continue;
                                }
                                planMaxCandidates = activePlan.MaxCandidatesPerBatch;
                                planMaxChars = activePlan.MaxBatchInputChars;
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "VerificationExecutionPlanner failed; falling back to fixed mode.");
                            }
                        }

                        string groupId = fileGroup.Topic.Replace('/', '-').Replace(' ', '_');
                        IReadOnlyList<PRReviewAgent.Services.Verification.VerificationBatch> verBatches =
                            PRReviewAgent.Services.Verification.VerificationBatchBuilder.BuildFromResolved(
                                workItems, planMaxCandidates, planMaxChars, groupId);

                        logger.LogInformation(
                            "Verification batches: group={Group} candidates={Candidates} batches={Batches} maxBatch={MaxBatch} concurrency={Concurrency}",
                            fileGroup.Topic, workItems.Count, verBatches.Count, planMaxCandidates, globalMaxConcurrent);

                        Func<string, CancellationToken, Task<(PRReviewAgent.Prompt.VerifiedResponse?, int?, int?)>> verLlmCall =
                            (prompt, ct) => context.Agents.RunJsonWithUsageAsync<PRReviewAgent.Prompt.VerifiedResponse>(prompt, ct);

                        var (verResults, verMetrics) = await PRReviewAgent.Services.Verification.VerificationExecutor.ExecuteAsync(
                            reviewRequest, verBatches, verLlmCall, budget,
                            turnRecorder, executionId, context.Agents.Model, logger, cancellationToken,
                            globalSemaphore: globalVerificationSem);

                        logger.LogInformation(
                            "VerificationMetrics: batches={Batches} success={Success} failed={Failed} retries={Retries} splits={Splits} singleFallback={Single} wallMs={WallMs} totalBatchMs={TotalMs}",
                            verMetrics.BatchCount, verMetrics.BatchSuccessCount, verMetrics.BatchFailureCount,
                            verMetrics.BatchRetryCount, verMetrics.BatchSplitCount, verMetrics.SingleCandidateFallbackCount,
                            verMetrics.WallClockMs, verMetrics.TotalBatchDurationMs);

                        List<VerifiedIssue> verifiedIssues = new();
                        foreach (VerifiedIssue vi in verResults)
                        {
                            if (!vi.valid) continue;
                            VerifiedIssue withRule = vi;
                            if (string.IsNullOrEmpty(vi.rule_id) && candidateToRuleId.TryGetValue(vi.candidate_id, out string? rid))
                                withRule.rule_id = rid;
                            verifiedIssues.Add(withRule);
                        }

                        if (verifiedIssues.Count == 0)
                        {
                            logger.LogInformation($"No verified issues for {fileGroup.Topic}:{fileGroup.ReviewContexts.Count} files.");
                            PromptBuilder.AddNotFound(fileGroup, reviews, language_, stringBuilder_);
                            continue;
                        }

                        // Finalization turn
                        string promptTurn3 = PromptBuilder.BuildTurn3(reviewRequest, verifiedIssues.ToArray(), stringBuilder_);
                        DateTimeOffset finStart = DateTimeOffset.UtcNow;
                        Stopwatch finSw = Stopwatch.StartNew();
                        long? finTurnId = null;
                        if (turnRecorder != null && executionId.HasValue)
                        {
                            try { finTurnId = await turnRecorder.StartAsync(executionId.Value, ReviewTurnType.Finalization, context.Agents.Model, finStart, cancellationToken); }
                            catch (Exception ex) { logger.LogError(ex, "Failed to start Finalization turn record"); }
                        }

                        string reviewResponse = string.Empty;
                        string cleanedResponse = string.Empty;
                        IReadOnlyList<string> selectedCandidateIds = Array.Empty<string>();
                        int? finInputTokens = null, finOutputTokens = null;
                        Exception? finException = null;
                        try
                        {
                            (reviewResponse, finInputTokens, finOutputTokens) = await context.Agents.RunWithUsageAsync(promptTurn3, context.CancellationToken);
                            (cleanedResponse, selectedCandidateIds) = PromptBuilder.ExtractSelectionMetadata(reviewResponse);
                        }
                        catch (Exception ex) { finException = ex; throw; }
                        finally
                        {
                            finSw.Stop();
                            if (turnRecorder != null && finTurnId.HasValue)
                            {
                                try
                                {
                                    if (finException != null)
                                        await turnRecorder.CompleteFailureAsync(finTurnId.Value, finInputTokens, finOutputTokens, ClassifyError(finException), DateTimeOffset.UtcNow, finSw.ElapsedMilliseconds, cancellationToken);
                                    else
                                        await turnRecorder.CompleteSuccessAsync(finTurnId.Value, finInputTokens, finOutputTokens, selectedCandidateIds.Count, DateTimeOffset.UtcNow, finSw.ElapsedMilliseconds, cancellationToken);
                                }
                                catch (Exception rex) { logger.LogError(rex, "Failed to complete Finalization turn record"); }
                            }
                        }

                        if (string.IsNullOrEmpty(reviewResponse))
                        {
                            logger.LogInformation($"No review generated for {fileGroup.Topic}:{fileGroup.ReviewContexts.Count} files.");
                            continue;
                        }

                        // Mark produced_final_finding for rules whose candidates survived Finalization.
                        if (usageRepo != null && executionId.HasValue && project != null && selectedCandidateIds.Count > 0)
                        {
                            string[] finalRuleIds = selectedCandidateIds
                                .Where(cid => candidateToRuleId.ContainsKey(cid))
                                .Select(cid => candidateToRuleId[cid])
                                .Distinct()
                                .ToArray();
                            if (finalRuleIds.Length > 0)
                            {
                                try { await usageRepo.MarkProducedFinalFindingAsync(executionId.Value, project.Id, finalRuleIds, cancellationToken); }
                                catch (Exception ex) { logger.LogError(ex, "Failed to mark produced_final_finding"); }
                            }
                        }

                        totalSelected += selectedCandidateIds.Count;
                        stringBuilder_.Clear();
                        stringBuilder_.Append($"# {fileGroup.Topic}\n\n");
                        stringBuilder_.Append(cleanedResponse);
                        reviews.Add(stringBuilder_.ToString());
                        logger.LogInformation($"Generated review for {fileGroup.ReviewContexts.Count} files.");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex.ToString());
                    }
                }

                if (recorder != null && executionId.HasValue)
                {
                    try
                    {
                        reviewStopwatch.Stop();
                        await recorder.CompleteSuccessAsync(executionId.Value, new ReviewExecutionResult(
                            CompletedAt: DateTimeOffset.UtcNow,
                            DurationMs: reviewStopwatch.ElapsedMilliseconds,
                            CandidateFindingCount: totalCandidates,
                            SelectedFindingCount: totalSelected,
                            CriticalCount: 0,
                            MajorCount: 0,
                            MinorCount: 0), cancellationToken);
                    }
                    catch (Exception ex) { logger.LogError(ex, "Failed to complete review execution record"); }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.ToString());
                if (recorder != null && executionId.HasValue)
                {
                    try
                    {
                        reviewStopwatch.Stop();
                        await recorder.CompleteFailureAsync(executionId.Value, ex.GetType().Name, DateTimeOffset.UtcNow, reviewStopwatch.ElapsedMilliseconds, cancellationToken);
                    }
                    catch (Exception rex) { logger.LogError(rex, "Failed to record review execution failure"); }
                }
                if (statusService != null && statusProvider != null)
                {
                    try { await statusService.FailReviewAsync(statusProvider, project!.Id, mergeRequestId, cancellationToken); }
                    catch (Exception rex) { logger.LogError(rex, "Failed to post review fail status comment"); }
                }
                return;
            }

            // Step 8: Merge reviews and post comment.
            string organizedReview;
            if (reviews.Count <= 0)
            {
                organizedReview = "No reviews are generated.";
            }
            else
            {
                stringBuilder_.Clear();
                foreach (string review in reviews)
                {
                    stringBuilder_.Append(review).Append("\n\n---\n\n");
                }
                organizedReview = stringBuilder_.ToString();
            }

            if (statusService != null && statusProvider != null)
            {
                try { await statusService.CompleteReviewAsync(statusProvider, project!.Id, mergeRequestId, organizedReview, cancellationToken); }
                catch (Exception ex) { logger.LogError(ex, "Failed to post review complete status comment"); }
            }
            logger.LogInformation($"Final review:\n{organizedReview}");
        }

        private static string ClassifyError(Exception ex) => ex switch
        {
            OperationCanceledException => "Cancellation",
            TimeoutException => "Timeout",
            _ => ex.GetType().Name,
        };

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

        private PayloadIssueComment payloadIssueComment_;
        private string language_;
        private int pullRequestNumber_;
        private StringBuilder stringBuilder_ = new StringBuilder();
    }
}
