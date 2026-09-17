namespace PRReviewAgent.Services.AutoReview
{
    public interface IAutoReviewUserSettingRepository
    {
        Task<bool?> GetAsync(long projectId, string userId, CancellationToken ct);
        Task UpsertAsync(long projectId, string userId, bool enabled, CancellationToken ct);
        Task InitializeAsync();
    }
}
