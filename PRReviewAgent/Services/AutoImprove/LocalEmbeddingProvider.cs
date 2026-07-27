namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class LocalEmbeddingProvider : IDisposable
    {
        private LlamaPure.LlamaPureClient llamaClient_;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public LocalEmbeddingProvider(string modelPath)
        {
            llamaClient_ = new LlamaPure.LlamaPureClient(modelPath, 8192, 1);
        }

        public float[] GetEmbedding(string text)
        {
            _semaphore.Wait();
            try
            {
                float[] result = llamaClient_.GetEmbedding(text);
                return result;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed){
                return;
            }
            _disposed = true;
            llamaClient_.Dispose();
            _semaphore.Dispose();
        }
    }
}
