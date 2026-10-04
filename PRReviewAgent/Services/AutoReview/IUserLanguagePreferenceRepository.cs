namespace PRReviewAgent.Services.AutoReview
{
    public interface IUserLanguagePreferenceRepository
    {
        Task<string?> GetAsync(long projectId, string userId, CancellationToken ct);
        Task SetAsync(long projectId, string userId, string language, CancellationToken ct);
        Task InitializeAsync();
    }
}
