namespace PRReviewAgent.Services.AutoReview
{
    public interface IAutoReviewPolicy
    {
        Task<bool> IsEnabledAsync(long projectId, string userId, CancellationToken ct);
    }
}
