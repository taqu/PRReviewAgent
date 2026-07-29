using NGitLab;
using NGitLab.Models;
using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.GitLabWebhook;
using PRReviewAget.Prompt;
using System.Text;

namespace PRReviewAgent.Services
{
    public class GitLabMergeMRTask
    {
        private readonly PayloadMergeRequestEvent _payload;

        public GitLabMergeMRTask(PayloadMergeRequestEvent payload)
        {
            _payload = payload;
        }

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitLabMergeMRTask>? logger = serviceProvider.GetService<ILogger<GitLabMergeMRTask>>();
            RuleExtractionService? ruleExtractionService = serviceProvider.GetService<RuleExtractionService>();
            RuleLifecycleService? ruleLifecycleService = serviceProvider.GetService<RuleLifecycleService>();

            if (ruleExtractionService == null && ruleLifecycleService == null) return;

            NGitLab.GitLabClient gitLabClient = serviceProvider.GetService<GitLabClientService>()!.GitLabClient;
            Context context = Context.Instance;

            try
            {
                long projectId = _payload.project.id;
                long mrIid = _payload.object_attributes.iid;
                string prKey = $"gitlab/{projectId}/{mrIid}";

                IMergeRequestClient mergeRequestClient = gitLabClient.GetMergeRequest((int)projectId);
                GitLabCollectionResponse<NGitLab.Models.Diff> response = mergeRequestClient.GetDiffsAsync((int)mrIid);

                List<ReviewContext> reviewContexts = new List<ReviewContext>();
                StringBuilder allDiffs = new StringBuilder();

                await foreach (NGitLab.Models.Diff diff in response)
                {
                    ReviewContext? ctx = GitLabWebhookCommentTask.IsTarget(diff, context.Settings.IsTargetExtension);
                    if (ctx == null) continue;
                    reviewContexts.Add(ctx);
                    if (!string.IsNullOrEmpty(diff.Difference))
                        allDiffs.AppendLine(diff.Difference);
                }

                if (ruleLifecycleService != null)
                {
                    await ruleLifecycleService.OnPrMergedAsync(prKey, allDiffs.ToString(), cancellationToken);
                }

                if (ruleExtractionService == null || reviewContexts.Count == 0) return;

                IRepositoryClient repository = gitLabClient.GetRepository(_payload.object_attributes.source_project_id);
                string sourceBranch = _payload.object_attributes.source_branch;

                foreach (ReviewContext ctx in reviewContexts)
                {
                    try
                    {
                        FileData file = await repository.Files.GetAsync(ctx.Path, sourceBranch, cancellationToken);
                        ctx.ChangedFile = file.DecodedContent;
                    }
                    catch { }
                }

                ContextCollector.FindPair(reviewContexts);

                foreach (ReviewContext ctx in reviewContexts)
                {
                    try
                    {
                        (string json, string expandedDiff) = AstContextExtractor.Run(
                            ctx.Path, ctx.ChangedFile, ctx.Path, ctx.Diff, ctx.PairPath, ctx.PairFile);
                        ctx.AstJson = json;
                        ctx.ExpandedDiff = expandedDiff;

                        await ruleExtractionService.ExtractAndSaveRuleAsync(
                            json, ctx.ExpandedDiff ?? ctx.Diff ?? string.Empty, ctx.PairPath, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Failed to extract rule for {Path}", ctx.Path);
                    }
                }

                logger?.LogInformation("Rule extraction complete for MR {Iid}", mrIid);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error processing merged MR");
            }
        }
    }
}
