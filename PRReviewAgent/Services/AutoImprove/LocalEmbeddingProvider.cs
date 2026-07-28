namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class LocalEmbeddingProvider : IDisposable
    {
        private LlamaPure.LlamaPureClient llamaClient_;
        private int chunk_overlap_;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public LocalEmbeddingProvider(string modelPath, uint context_size, int chunk_overlap)
        {
            llamaClient_ = new LlamaPure.LlamaPureClient(modelPath, context_size, 1);
            chunk_overlap_ = chunk_overlap;
        }

        public List<float[]> GetEmbedding(string text)
        {
            _semaphore.Wait();
            try
            {
                List<float[]> result = llamaClient_.GetEmbedding(text, chunk_overlap_);
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
