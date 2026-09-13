namespace PRReviewAgent
{
    public interface ISubAgent
    {
        Task<string?> RunAsync(string prompt, CancellationToken cancellationToken = default);
    }
}
