using LLama;
using LLama.Common;

namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class LocalEmbeddingProvider : IDisposable
    {
        private readonly LLamaWeights _weights;
        private readonly LLamaEmbedder _embedder;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public LocalEmbeddingProvider(string modelPath)
        {
            ModelParams parameters = new ModelParams(modelPath)
            {
                ContextSize = 8192,
                GpuLayerCount = 0,
                Embeddings = true,
            };
            _weights = LLamaWeights.LoadFromFile(parameters);
            _embedder = new LLamaEmbedder(_weights, parameters);
        }

        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                IReadOnlyList<float[]> result = await _embedder.GetEmbeddings(text, cancellationToken);
                return result.Count > 0 ? result[0] : Array.Empty<float>();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _embedder.Dispose();
            _weights.Dispose();
            _semaphore.Dispose();
        }
    }
}
