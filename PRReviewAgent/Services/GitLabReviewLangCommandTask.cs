using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.AutoReview;
using PRReviewAgent.Services.GitLabWebhook;

namespace PRReviewAgent.Services
{
    public class GitLabReviewLangCommandTask
    {
        private readonly GitLabMrNoteWebhook _payload;
        private readonly string? _language;

        public GitLabReviewLangCommandTask(GitLabMrNoteWebhook payload, string? language)
        {
            _payload = payload;
            _language = language;
        }

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitLabReviewLangCommandTask>? logger = serviceProvider.GetService<ILogger<GitLabReviewLangCommandTask>>();

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

            string externalProjectId = $"gitlab:{_payload.Project?.Id}";
            Project project;
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
                logger?.LogError(ex, "Failed to resolve project for review_lang command");
                return;
            }

            long commentAuthorId = _payload.ObjectAttributes?.AuthorId ?? _payload.User?.Id ?? 0;
            string userId = $"gitlab:{commentAuthorId}";

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
