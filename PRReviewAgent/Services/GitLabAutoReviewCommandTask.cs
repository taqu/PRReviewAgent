using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.AutoReview;
using PRReviewAgent.Services.GitHubWebhook;
using PRReviewAgent.Services.GitLabWebhook;

namespace PRReviewAgent.Services
{
    public class GitLabAutoReviewCommandTask
    {
        private readonly GitLabMrNoteWebhook _payload;
        private readonly ReviewCommandType _commandType;

        public GitLabAutoReviewCommandTask(GitLabMrNoteWebhook payload, ReviewCommandType commandType)
        {
            _payload = payload;
            _commandType = commandType;
        }

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitLabAutoReviewCommandTask>? logger = serviceProvider.GetService<ILogger<GitLabAutoReviewCommandTask>>();

            IAutoReviewUserSettingRepository? repository = serviceProvider.GetService<IAutoReviewUserSettingRepository>();
            if (repository == null)
            {
                logger?.LogWarning("IAutoReviewUserSettingRepository not registered; auto_review command ignored.");
                return;
            }

            ProjectRepository? projectRepository = serviceProvider.GetService<ProjectRepository>();
            if (projectRepository == null)
            {
                logger?.LogWarning("ProjectRepository not registered; auto_review command ignored.");
                return;
            }

            string externalProjectId = $"gitlab:{_payload.Project?.Id}";
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
                logger?.LogError(ex, "Failed to resolve project for auto_review command");
                return;
            }

            long commentAuthorId = _payload.ObjectAttributes?.AuthorId ?? _payload.User?.Id ?? 0;
            string userId = $"gitlab:{commentAuthorId}";
            bool enabled = _commandType == ReviewCommandType.AutoReviewOn;

            try
            {
                await repository.UpsertAsync(project.Id, userId, enabled, cancellationToken);
                logger?.LogInformation("User {UserId} set auto_review={Enabled} for project {ProjectId}", userId, enabled, project.Id);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to save auto_review setting for user {UserId}", userId);
            }
        }
    }
}
