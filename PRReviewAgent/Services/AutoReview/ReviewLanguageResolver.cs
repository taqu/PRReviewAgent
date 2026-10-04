using Tomlyn.Model;

namespace PRReviewAgent.Services.AutoReview
{
    public static class ReviewLanguageResolver
    {
        public static string GetDefaultLanguage()
        {
            TomlTable commonTable = (TomlTable)Context.Instance.Settings.Config["common"];
            return (string)commonTable["default_language"];
        }

        public static async Task<string> ResolveAsync(
            long projectId,
            string userId,
            IUserLanguagePreferenceRepository? store,
            CancellationToken ct)
        {
            if (store != null && projectId > 0)
            {
                try
                {
                    string? stored = await store.GetAsync(projectId, userId, ct);
                    if (!string.IsNullOrEmpty(stored) && Context.Instance.Settings.HasTemplate(stored))
                        return stored;
                }
                catch { /* fall through to default */ }
            }
            return GetDefaultLanguage();
        }
    }
}
