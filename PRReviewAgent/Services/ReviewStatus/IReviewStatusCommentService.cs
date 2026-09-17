namespace PRReviewAgent.Services.ReviewStatus
{
    public interface IReviewStatusCommentService
    {
        Task BeginReviewAsync(IReviewCommentProvider provider, long projectId, string mergeRequestId, CancellationToken cancellationToken = default);
        Task CompleteReviewAsync(IReviewCommentProvider provider, long projectId, string mergeRequestId, string reviewText, CancellationToken cancellationToken = default);
        Task FailReviewAsync(IReviewCommentProvider provider, long projectId, string mergeRequestId, CancellationToken cancellationToken = default);
    }
}
