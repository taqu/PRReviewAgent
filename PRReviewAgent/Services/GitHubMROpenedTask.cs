using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.AutoReview;
using PRReviewAgent.Services.GitHubWebhook;

namespace PRReviewAgent.Services
{
    public class GitHubMROpenedTask
    {
        private readonly PayloadPullRequestEvent _prEvent;

        public GitHubMROpenedTask(PayloadPullRequestEvent prEvent)
        {
            _prEvent = prEvent;
        }

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitHubMROpenedTask>? logger = serviceProvider.GetService<ILogger<GitHubMROpenedTask>>();

            IAutoReviewPolicy? policy = serviceProvider.GetService<IAutoReviewPolicy>();
            if (policy == null)
            {
                logger?.LogDebug("IAutoReviewPolicy not registered; skipping auto review on PR opened.");
                return;
            }

            ProjectRepository? projectRepository = serviceProvider.GetService<ProjectRepository>();
            if (projectRepository == null) return;

            string externalProjectId = $"github:{_prEvent.repository.id}";
            AutoImprove.Project project;
            try
            {
                project = await projectRepository.GetOrCreateAsync(
                    externalProjectId,
                    _prEvent.repository.name ?? externalProjectId,
                    _prEvent.repository.html_url,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to resolve project for PR opened auto review");
                return;
            }

            string userId = $"github:{_prEvent.pull_request.user?.id ?? _prEvent.sender?.id ?? 0}";
            bool enabled = await policy.IsEnabledAsync(project.Id, userId, cancellationToken);
            if (!enabled)
            {
                logger?.LogDebug("Auto review not enabled for user {UserId} on project {ProjectId}", userId, project.Id);
                return;
            }

            Tomlyn.Model.TomlTable? commonTable = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["common"];
            string defaultLanguage = (string)commonTable["default_language"];

            IBackgroundTaskQueue? taskQueue = serviceProvider.GetKeyedService<IBackgroundTaskQueue>(Settings.TaskQueueReview);
            if (taskQueue == null) return;

            GitHubWebhookCommentTask reviewTask = new GitHubWebhookCommentTask(_prEvent, defaultLanguage);
            await taskQueue.QueueBackgroundWorkItemAsync(reviewTask.RunAsync);
            logger?.LogInformation("Auto review queued for PR #{Number} by user {UserId}", _prEvent.number, userId);
        }
    }
}
