namespace PRReviewAgent.Services.ReviewStatus
{
    public interface IReviewStatusCommentRepository
    {
        Task<string?> GetCommentIdAsync(long projectId, string mergeRequestId, CancellationToken cancellationToken = default);
        Task UpsertAsync(long projectId, string mergeRequestId, string commentId, CancellationToken cancellationToken = default);
        Task DeleteAsync(long projectId, string mergeRequestId, CancellationToken cancellationToken = default);
    }
}
