using Octokit;
using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.GitHubWebhook;
using PRReviewAget.Prompt;
using System.Text;

namespace PRReviewAgent.Services
{
    public class GitHubMergePRTask
    {
        private readonly PayloadPullRequestEvent _payload;
        private readonly long _repositoryId;
        private readonly int _prNumber;

        public GitHubMergePRTask(PayloadPullRequestEvent payload)
        {
            _payload = payload;
            _repositoryId = payload.repository.id;
            _prNumber = payload.number;
        }

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitHubMergePRTask>? logger = serviceProvider.GetService<ILogger<GitHubMergePRTask>>();
            RuleExtractionService? ruleExtractionService = serviceProvider.GetService<RuleExtractionService>();
            RuleLifecycleService? ruleLifecycleService = serviceProvider.GetService<RuleLifecycleService>();

            if (ruleExtractionService == null && ruleLifecycleService == null) return;

            GitHubClient gitHubClient = serviceProvider.GetService<GitHubClientService>()!.GitHubClient;
            Context context = Context.Instance;

            try
            {
                IReadOnlyList<PullRequestFile> files = await gitHubClient.PullRequest.Files(_repositoryId, _prNumber);
                List<ReviewContext> reviewContexts = new List<ReviewContext>();
                StringBuilder allDiffs = new StringBuilder();

                foreach (PullRequestFile file in files)
                {
                    ReviewContext? ctx = GitHubWebhookCommentTask.IsTarget(file, context.Settings.IsTargetExtension);
                    if (ctx == null) continue;
                    reviewContexts.Add(ctx);
                    if (!string.IsNullOrEmpty(file.Patch))
                        allDiffs.AppendLine(file.Patch);
                }

                string prKey = $"github/{_repositoryId}/{_prNumber}";
                if (ruleLifecycleService != null)
                {
                    await ruleLifecycleService.OnPrMergedAsync(prKey, allDiffs.ToString(), cancellationToken);
                }

                if (ruleExtractionService == null || reviewContexts.Count == 0) return;

                PullRequest pullRequest = await gitHubClient.PullRequest.Get(_repositoryId, _prNumber);
                foreach (ReviewContext ctx in reviewContexts)
                {
                    try
                    {
                        ctx.ChangedFile = await GitHubWebhookCommentTask.GetPullRequestFileContentAsync(
                            gitHubClient, _repositoryId, pullRequest.Head.Sha, ctx.Path);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Failed to fetch file content for {Path}", ctx.Path);
                    }
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

                logger?.LogInformation("Rule extraction complete for PR {Number}", _prNumber);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error processing merged PR {Number}", _prNumber);
            }
        }
    }
}
