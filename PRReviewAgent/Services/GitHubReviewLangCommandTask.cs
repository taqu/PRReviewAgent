using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.AutoReview;
using PRReviewAgent.Services.GitHubWebhook;

namespace PRReviewAgent.Services
{
    public class GitHubReviewLangCommandTask
    {
        private readonly PayloadIssueComment _payload;
        private readonly string? _language;

        public GitHubReviewLangCommandTask(PayloadIssueComment payload, string? language)
        {
            _payload = payload;
            _language = language;
        }

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitHubReviewLangCommandTask>? logger = serviceProvider.GetService<ILogger<GitHubReviewLangCommandTask>>();

            if (string.IsNullOrEmpty(_language))
            {
                logger?.LogWarning("review_lang command has no language specified; ignored.");
                return;
            }

            if (!Context.Instance.Settings.HasTemplate(_language))
            {
                logger?.LogWarning("review_lang command specifies unknown language '{Language}'; ignored.", _language);
                return;
            }

            IUserLanguagePreferenceRepository? langPrefRepo = serviceProvider.GetService<IUserLanguagePreferenceRepository>();
            if (langPrefRepo == null)
            {
                logger?.LogWarning("IUserLanguagePreferenceRepository not registered; review_lang command ignored.");
                return;
            }

            ProjectRepository? projectRepository = serviceProvider.GetService<ProjectRepository>();
            if (projectRepository == null)
            {
                logger?.LogWarning("ProjectRepository not registered; review_lang command ignored.");
                return;
            }

            string externalProjectId = $"github:{_payload.repository.id}";
            Project project;
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
                logger?.LogError(ex, "Failed to resolve project for review_lang command");
                return;
            }

            string userId = $"github:{_payload.sender.id}";

            try
            {
                await langPrefRepo.SetAsync(project.Id, userId, _language, cancellationToken);
                logger?.LogInformation("User {UserId} set review language={Language} for project {ProjectId}", userId, _language, project.Id);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to save language preference for user {UserId}", userId);
            }
        }
    }
}
