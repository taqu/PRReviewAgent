using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.AutoReview;
using PRReviewAgent.Services.GitLabWebhook;

namespace PRReviewAgent.Services
{
    public class GitLabMROpenedTask
    {
        private readonly GitLabMergeRequestWebhook _mrEvent;

        public GitLabMROpenedTask(GitLabMergeRequestWebhook mrEvent)
        {
            _mrEvent = mrEvent;
        }

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitLabMROpenedTask>? logger = serviceProvider.GetService<ILogger<GitLabMROpenedTask>>();

            IAutoReviewPolicy? policy = serviceProvider.GetService<IAutoReviewPolicy>();
            if (policy == null)
            {
                logger?.LogDebug("IAutoReviewPolicy not registered; skipping auto review on MR opened.");
                return;
            }

            ProjectRepository? projectRepository = serviceProvider.GetService<ProjectRepository>();
            if (projectRepository == null) return;

            string externalProjectId = $"gitlab:{_mrEvent.Project?.Id}";
            AutoImprove.Project project;
            try
            {
                project = await projectRepository.GetOrCreateAsync(
                    externalProjectId,
                    externalProjectId,
                    null,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to resolve project for MR opened auto review");
                return;
            }

            long authorId = _mrEvent.ObjectAttributes?.AuthorId ?? _mrEvent.User?.Id ?? 0;
            string userId = $"gitlab:{authorId}";
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

            GitLabWebhookCommentTask reviewTask = GitLabWebhookCommentTask.FromMROpened(_mrEvent, defaultLanguage);
            await taskQueue.QueueBackgroundWorkItemAsync(reviewTask.RunAsync);
            logger?.LogInformation("Auto review queued for MR !{Iid} by user {UserId}", _mrEvent.ObjectAttributes?.Iid, userId);
        }
    }
}
