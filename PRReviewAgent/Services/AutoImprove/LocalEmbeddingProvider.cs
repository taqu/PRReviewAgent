namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class LocalEmbeddingProvider : IDisposable
    {
        private LlamaClient llamaClient_;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public LocalEmbeddingProvider(string modelPath)
        {
            llamaClient_ = new LlamaClient(modelPath, 128, 4);
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
