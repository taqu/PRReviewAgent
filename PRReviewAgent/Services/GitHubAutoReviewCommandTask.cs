using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.AutoReview;
using PRReviewAgent.Services.GitHubWebhook;

namespace PRReviewAgent.Services
{
    public class GitHubAutoReviewCommandTask
    {
        private readonly PayloadIssueComment _payload;
        private readonly ReviewCommandType _commandType;
        private readonly string? _language;

        public GitHubAutoReviewCommandTask(PayloadIssueComment payload, ReviewCommandType commandType, string? language)
        {
            _payload = payload;
            _commandType = commandType;
            _language = language;
        }

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitHubAutoReviewCommandTask>? logger = serviceProvider.GetService<ILogger<GitHubAutoReviewCommandTask>>();

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

            string externalProjectId = $"github:{_payload.repository.id}";
            AutoImprove.Project project;
            try
            {
                project = await projectRepository.GetOrCreateAsync(
                    externalProjectId,
                    _payload.repository.name ?? externalProjectId,
                    _payload.repository.html_url,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to resolve project for auto_review command");
                return;
            }

            string userId = $"github:{_payload.sender.id}";
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
