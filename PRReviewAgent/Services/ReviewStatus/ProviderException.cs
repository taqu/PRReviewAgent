namespace PRReviewAgent.Services.ReviewStatus
{
    public sealed class ProviderNotFoundException : Exception
    {
        public ProviderNotFoundException(string? message = null, Exception? inner = null)
            : base(message ?? "Provider resource not found.", inner) { }
    }

    public sealed class ProviderForbiddenException : Exception
    {
        public ProviderForbiddenException(string? message = null, Exception? inner = null)
            : base(message ?? "Provider request forbidden.", inner) { }
    }
}
