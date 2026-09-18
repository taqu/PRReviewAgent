using Microsoft.AspNetCore.Mvc.Rendering;
using NGitLab;
using NGitLab.Models;
using OpenAI.Chat;
using PRReviewAgent.Prompt;
using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.GitLabWebhook;
using PRReviewAgent.Services.ReviewStatus;
using PRReviewAgent.Services.Statistics;
using PRReviewAget.Prompt;
using PRReviewAgent.Services.AutoReview;
using System;
using System.Diagnostics;
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

        public GitLabWebhookCommentTask(GitLabMrNoteWebhook payloadComment)
        {
            gitLabMrNoteWebhook_ = payloadComment;

            language_ = FindLanguage(gitLabMrNoteWebhook_.ObjectAttributes.Note);
            if (string.IsNullOrEmpty(language_) || !Context.Instance.Settings.HasTemplate(language_))
            {
                Tomlyn.Model.TomlTable? commonTable = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["common"];
                language_ = (string)commonTable["default_language"];
            }
        }

        public static GitLabWebhookCommentTask FromMROpened(GitLabMergeRequestWebhook mrEvent, string language)
        {
            var synthetic = new GitLabMrNoteWebhook
            {
                ObjectKind = "note",
                User = mrEvent.User,
                Project = mrEvent.Project,
                ObjectAttributes = new WebhookObjectAttributes
                {
                    Id = 0,
                    Note = string.Empty,
                    NoteableType = "MergeRequest",
                },
                MergeRequest = new WebhookMergeRequest
                {
                    Iid = mrEvent.ObjectAttributes?.Iid,
                    TargetProjectId = mrEvent.ObjectAttributes?.TargetProjectId ?? mrEvent.Project?.Id,
                    SourceBranch = mrEvent.ObjectAttributes?.SourceBranch,
                    Title = mrEvent.ObjectAttributes?.Title,
                    Description = mrEvent.ObjectAttributes?.Description,
                },
            };

            var task = new GitLabWebhookCommentTask(synthetic);
            task.language_ = language;
            if (string.IsNullOrEmpty(task.language_) || !Context.Instance.Settings.HasTemplate(task.language_))
            {
                Tomlyn.Model.TomlTable? commonTable = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["common"];
                task.language_ = (string)commonTable["default_language"];
            }
            return task;
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
            logger.LogInformation($"Processing comment: {gitLabMrNoteWebhook_.ObjectAttributes.Id}");

            Context context = Context.Instance;
            NGitLab.GitLabClient gitLabClient = serviceProvider.GetService<GitLabClientService>().GitLabClient;

            // Resolve project identity early — needed for status comment service.
            string externalProjectId = $"gitlab:{gitLabMrNoteWebhook_.Project.Id}";
            string mergeRequestId = $"gitlab/{gitLabMrNoteWebhook_.Project.Id}/{gitLabMrNoteWebhook_.MergeRequest.Iid}";
            ProjectRepository? projectRepository = serviceProvider.GetService<ProjectRepository>();
            AutoImprove.Project? project = null;
            if (projectRepository != null)
            {
                try
                {
                    project = await projectRepository.GetOrCreateAsync(externalProjectId, externalProjectId, null, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to resolve project");
                }
            }

            // Step 1: Fetch all diffs for the merge request.
            NGitLab.IMergeRequestClient mergeRequestClient = gitLabClient.GetMergeRequest((long)gitLabMrNoteWebhook_.Project.Id);

            IReviewStatusCommentService? statusService = serviceProvider.GetService<IReviewStatusCommentService>();
            GitLabReviewCommentProvider? statusProvider = project != null
                ? new GitLabReviewCommentProvider(mergeRequestClient.Comments((long)gitLabMrNoteWebhook_.MergeRequest.Iid))
                : null;

            if (statusService != null && statusProvider != null)
            {
                try { await statusService.BeginReviewAsync(statusProvider, project!.Id, mergeRequestId, cancellationToken); }
                catch (Exception ex) { logger.LogError(ex, "Failed to post review begin status comment"); }
            }

            GitLabCollectionResponse<NGitLab.Models.Diff> response = mergeRequestClient.GetDiffsAsync((long)gitLabMrNoteWebhook_.MergeRequest.Iid);
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
                if (statusService != null && statusProvider != null)
                {
                    try { await statusService.CompleteReviewAsync(statusProvider, project!.Id, mergeRequestId, "No reviews are generated. There are no diffs to review.", cancellationToken); }
                    catch (Exception ex) { logger.LogError(ex, "Failed to post review complete status comment"); }
                }
                return;
            }

            // Step 3: Fetch full file contents from the source branch.
            IRepositoryClient repository = gitLabClient.GetRepository((long)gitLabMrNoteWebhook_.MergeRequest.TargetProjectId);
            string sourceBranch = gitLabMrNoteWebhook_.MergeRequest.SourceBranch;
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

            // Step 6: Build file groups using semantic grouping.
            logger.LogInformation($"Building file groups for {reviewContexts.Count} files.");
            ReviewRequest reviewRequest = new ReviewRequest();
            reviewRequest.MergeRequestTitle = gitLabMrNoteWebhook_.MergeRequest.Title ?? string.Empty;
            reviewRequest.MergeRequestDescription = gitLabMrNoteWebhook_.MergeRequest.Description ?? string.Empty;
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

            // Step 7: Execute 2 turn review for each file group.
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

                        // Verification turn (one per candidate)
                        List<VerifiedIssue> verifiedIssues = new List<VerifiedIssue>();
                        foreach (CandidateIssue candidate in candidatesForVerification)
                        {
                            try
                            {
                                VerificationContext verCtx = new VerificationContextResolver().Resolve(candidate, fileGroup.ReviewContexts, budget, logger);
                                if (verCtx.Items.Count == 0)
                                {
                                    logger.LogWarning("Skipping verification for candidate {Id}: no context resolved (NoContext)", candidate.candidate_id ?? "?");
                                    continue;
                                }
                                string promptTurn2 = PromptBuilder.BuildTurn2(reviewRequest, candidate, verCtx, stringBuilder_);

                                DateTimeOffset verStart = DateTimeOffset.UtcNow;
                                Stopwatch verSw = Stopwatch.StartNew();
                                long? verTurnId = null;
                                if (turnRecorder != null && executionId.HasValue)
                                {
                                    try { verTurnId = await turnRecorder.StartAsync(executionId.Value, ReviewTurnType.Verification, context.Agents.Model, verStart, cancellationToken); }
                                    catch (Exception ex) { logger.LogError(ex, "Failed to start Verification turn record"); }
                                }

                                VerifiedResponse? verResp = null;
                                int? verInputTokens = null, verOutputTokens = null;
                                Exception? verException = null;
                                try
                                {
                                    (verResp, verInputTokens, verOutputTokens) = await context.Agents.RunJsonWithUsageAsync<VerifiedResponse>(promptTurn2, context.CancellationToken);
                                }
                                catch (Exception ex) { verException = ex; throw; }
                                finally
                                {
                                    verSw.Stop();
                                    if (turnRecorder != null && verTurnId.HasValue)
                                    {
                                        try
                                        {
                                            int validCount = verResp?.issues.Count(i => i.valid) ?? 0;
                                            if (verException != null)
                                                await turnRecorder.CompleteFailureAsync(verTurnId.Value, verInputTokens, verOutputTokens, ClassifyError(verException), DateTimeOffset.UtcNow, verSw.ElapsedMilliseconds, cancellationToken);
                                            else
                                                await turnRecorder.CompleteSuccessAsync(verTurnId.Value, verInputTokens, verOutputTokens, validCount, DateTimeOffset.UtcNow, verSw.ElapsedMilliseconds, cancellationToken);
                                        }
                                        catch (Exception rex) { logger.LogError(rex, "Failed to complete Verification turn record"); }
                                    }
                                }

                                logger.LogInformation(
                                    "Verification: candidate={Id} items={Items} estimated_chars={Chars} truncated={Truncated} unresolved={Unresolved}",
                                    candidate.candidate_id ?? "?",
                                    verCtx.Items.Count,
                                    verCtx.Items.Sum(i => i.Source.Length),
                                    verCtx.Truncated,
                                    verCtx.UnresolvedTargets.Count);

                                if (verResp != null)
                                {
                                    foreach (VerifiedIssue vi in verResp.issues)
                                    {
                                        if (!vi.valid) continue;
                                        VerifiedIssue withRule = vi;
                                        if (string.IsNullOrEmpty(vi.rule_id) && candidateToRuleId.TryGetValue(vi.candidate_id, out string? rid))
                                            withRule.rule_id = rid;
                                        verifiedIssues.Add(withRule);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, $"Verification failed for candidate {candidate.candidate_id}");
                            }
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
                try
                {
                    await statusService.CompleteReviewAsync(statusProvider, project!.Id, mergeRequestId, organizedReview, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to post review complete status comment");
                }
            }
            logger.LogInformation($"Final review:\n{organizedReview}");
        }

        private static string ClassifyError(Exception ex) => ex switch
        {
            OperationCanceledException => "Cancellation",
            TimeoutException => "Timeout",
            _ => ex.GetType().Name,
        };

        private static async Task<FileData?> GetFileAsync(IFilesClient filesClient, string path, string sourceBranch, CancellationToken cancellationToken)
        {
            try
            {
                return await filesClient.GetAsync(path, sourceBranch, cancellationToken);
            }
            catch
            {
                return null;
            }
        }

        private static async Task FindPairAsync(List<ReviewContext> reviewContexts, IFilesClient filesClient, string sourceBranch, CancellationToken cancellationToken)
        {
            foreach (ReviewContext reviewContext in reviewContexts)
            {
                if (!string.IsNullOrEmpty(reviewContext.PairPath))
                {
                    continue;
                }
                string? pairPath = GuessPair1(reviewContext.Path);
                if (pairPath == null)
                {
                    continue;
                }
                FileData? file = null;
                if (null != (file = await GetFileAsync(filesClient, pairPath, sourceBranch, cancellationToken)))
                {
                    reviewContext.PairPath = file.Path;
                    reviewContext.PairFile = file.DecodedContent;
                    continue;
                }

                pairPath = GuessPair2(reviewContext.Path);
                if (pairPath == null)
                {
                    continue;
                }
                if (null != (file = await GetFileAsync(filesClient, pairPath, sourceBranch, cancellationToken)))
                {
                    reviewContext.PairPath = file.Path;
                    reviewContext.PairFile = file.DecodedContent;
                    continue;
                }

                pairPath = GuessPair3(reviewContext.Path);
                if (pairPath == null)
                {
                    continue;
                }
                if (null != (file = await GetFileAsync(filesClient, pairPath, sourceBranch, cancellationToken)))
                {
                    reviewContext.PairPath = file.Path;
                    reviewContext.PairFile = file.DecodedContent;
                    continue;
                }

                pairPath = GuessPair4(reviewContext.Path);
                if (pairPath == null)
                {
                    continue;
                }
                if (null != (file = await GetFileAsync(filesClient, pairPath, sourceBranch, cancellationToken)))
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

        private GitLabMrNoteWebhook gitLabMrNoteWebhook_;
        private string language_;
        private StringBuilder stringBuilder_ = new StringBuilder();
    }
}
