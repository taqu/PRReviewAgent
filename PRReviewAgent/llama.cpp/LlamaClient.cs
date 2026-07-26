using System.Runtime.InteropServices;
using System.Text;

namespace PRReviewAgent
{
    public sealed class LlamaClient : IDisposable
    {
        private IntPtr _model;
        private IntPtr _ctx;
        private IntPtr _vocab;
        private int _nVocab;
        private int _nEmbd;
        private bool _disposed;

        // -------------------------------------------------------------------------
        // Native DLL loader
        // -------------------------------------------------------------------------
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        /// <summary>
        /// Explicitly loads all native DLLs from the extension directory using
        /// their full paths, so the OS loader never searches PATH or CWD.
        /// Must be called before any P/Invoke into llama.dll.
        /// </summary>
        public static void LoadNativeDlls()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            // Load in dependency order: base Å® cpu Å® ggml Å® llama
#if DEBUG
            string[] dlls = { "ggml-base.dll", "ggml-cpu.dll", "ggml.dll", "llama.dll" };
#else
            string[] dlls = { "ggml-base.dll", "ggml-cpu.dll", "ggml.dll", "llama.dll" };
#endif
            foreach (string dll in dlls)
            {
                string fullPath = System.IO.Path.Combine(exeDir, "llama.cpp", dll);
                if (LoadLibraryW(fullPath) == IntPtr.Zero) {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"LoadLibrary failed for: {fullPath}");
                }
            }
        }


        public LlamaClient(string modelPath, uint contextSize = 2048, int threads = 4)
        {
            if (string.IsNullOrEmpty(modelPath))
                throw new ArgumentException("Model path must not be null or empty.", nameof(modelPath));

            LlamaNative.llama_backend_init();

            LlamaNative.LlamaModelParams modelParams = LlamaNative.llama_model_default_params();
            modelParams.load_mode = LlamaNative.LlamaLoadMode.None;
            _model = LlamaNative.llama_model_load_from_file(modelPath, modelParams);
            if (_model == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to load model from '{modelPath}'.");

            _vocab = LlamaNative.llama_model_get_vocab(_model);
            if (_vocab == IntPtr.Zero)
                throw new InvalidOperationException("Failed to get vocabulary from model.");

            _nVocab = LlamaNative.llama_vocab_n_tokens(_vocab);
            _nEmbd  = LlamaNative.llama_model_n_embd(_model);

            var ctxParams = LlamaNative.llama_context_default_params();
            ctxParams.n_ctx       = contextSize;
            ctxParams.n_batch     = contextSize;
            ctxParams.n_threads   = threads;
            ctxParams.embeddings  = 1;

            _ctx = LlamaNative.llama_init_from_model(_model, ctxParams);
            if (_ctx == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create llama context.");
        }

        public float[] GetEmbedding(string text)
        {
            ThrowIfDisposed();
            if (text == null) throw new ArgumentNullException(nameof(text));

            int[] tokenIds = Tokenize(text, addSpecial: true);
            if (tokenIds.Length == 0)
                throw new InvalidOperationException("Tokenization produced no tokens.");

            LlamaNative.LlamaBatch batch = LlamaNative.llama_batch_init(tokenIds.Length, 0, 1);
            try
            {
                FillBatch(ref batch, tokenIds, posOffset: 0, setLastLogit: true);

                int ret = LlamaNative.llama_decode(_ctx, batch);
                if (ret != 0)
                    throw new InvalidOperationException($"llama_decode failed with code {ret}.");

                int lastIdx = tokenIds.Length - 1;
                IntPtr embdPtr = LlamaNative.llama_get_embeddings_ith(_ctx, lastIdx);
                if (embdPtr == IntPtr.Zero)
                    throw new InvalidOperationException("llama_get_embeddings_ith returned null.");

                var result = new float[_nEmbd];
                Marshal.Copy(embdPtr, result, 0, _nEmbd);
                return result;
            }
            finally
            {
                LlamaNative.llama_batch_free(batch);
            }
        }

        public string Complete(string prompt, int maxNewTokens = 128)
        {
            ThrowIfDisposed();
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));
            if (maxNewTokens <= 0) throw new ArgumentOutOfRangeException(nameof(maxNewTokens));

            int[] promptTokens = Tokenize(prompt, addSpecial: true);
            if (promptTokens.Length == 0)
                throw new InvalidOperationException("Tokenization produced no tokens.");

            // Prefill: decode all prompt tokens, request logits for the last one only.
            LlamaNative.LlamaBatch batch = LlamaNative.llama_batch_init(promptTokens.Length, 0, 1);
            int prefillRet;
            try
            {
                FillBatch(ref batch, promptTokens, posOffset: 0, setLastLogit: true);
                prefillRet = LlamaNative.llama_decode(_ctx, batch);
            }
            finally
            {
                LlamaNative.llama_batch_free(batch);
            }

            if (prefillRet != 0)
                throw new InvalidOperationException($"llama_decode (prefill) failed with code {prefillRet}.");

            // Sample first token from the last prompt token's logits.
            IntPtr logitsPtr = LlamaNative.llama_get_logits_ith(_ctx, promptTokens.Length - 1);
            int sampledToken = GreedySample(logitsPtr, _nVocab);

            var sb = new StringBuilder();
            int currentPos = promptTokens.Length;

            for (int step = 0; step < maxNewTokens; step++)
            {
                if (IsEndOfGeneration(sampledToken))
                    break;

                sb.Append(TokenToString(sampledToken));

                // Decode the single new token.
                LlamaNative.LlamaBatch stepBatch = LlamaNative.llama_batch_init(1, 0, 1);
                int stepRet;
                try
                {
                    FillBatch(ref stepBatch, new[] { sampledToken }, posOffset: currentPos, setLastLogit: true);
                    stepRet = LlamaNative.llama_decode(_ctx, stepBatch);
                }
                finally
                {
                    LlamaNative.llama_batch_free(stepBatch);
                }

                if (stepRet != 0)
                    throw new InvalidOperationException($"llama_decode (step {step}) failed with code {stepRet}.");

                currentPos++;
                logitsPtr   = LlamaNative.llama_get_logits_ith(_ctx, 0);
                sampledToken = GreedySample(logitsPtr, _nVocab);
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_ctx != IntPtr.Zero)
            {
                LlamaNative.llama_free(_ctx);
                _ctx = IntPtr.Zero;
            }

            if (_model != IntPtr.Zero)
            {
                LlamaNative.llama_model_free(_model);
                _model = IntPtr.Zero;
            }

            LlamaNative.llama_backend_free();
        }

        private int[] Tokenize(string text, bool addSpecial)
        {
            int nBytes = Encoding.UTF8.GetByteCount(text);
            int maxTokens = nBytes + 16;
            var tokens = new int[maxTokens];

            byte specialFlag = addSpecial ? (byte)1 : (byte)0;
            int count = LlamaNative.llama_tokenize(
                _vocab, text, nBytes, tokens, maxTokens, specialFlag, 0);

            if (count < 0)
            {
                // Buffer too small; reallocate with the exact required size.
                maxTokens = -count;
                tokens = new int[maxTokens];
                count = LlamaNative.llama_tokenize(
                    _vocab, text, nBytes, tokens, maxTokens, specialFlag, 0);
            }

            if (count < 0)
                throw new InvalidOperationException($"Tokenization failed (code {count}).");

            var result = new int[count];
            Array.Copy(tokens, result, count);
            return result;
        }

        private void FillBatch(ref LlamaNative.LlamaBatch batch, int[] tokenIds, int posOffset, bool setLastLogit)
        {
            int n = tokenIds.Length;
            batch.n_tokens = n;

            for (int i = 0; i < n; i++)
            {
                // token[i]
                Marshal.WriteInt32(batch.token + i * 4, tokenIds[i]);
                // pos[i]
                Marshal.WriteInt32(batch.pos + i * 4, posOffset + i);
                // n_seq_id[i] = 1
                Marshal.WriteInt32(batch.n_seq_id + i * 4, 1);
                // seq_id[i][0] = 0
                IntPtr seqIdRow = Marshal.ReadIntPtr(batch.seq_id + i * IntPtr.Size);
                Marshal.WriteInt32(seqIdRow, 0);
                // logits[i]
                byte logitFlag = (setLastLogit && i == n - 1) ? (byte)1 : (byte)0;
                Marshal.WriteByte(batch.logits + i, logitFlag);
            }
        }

        private static int GreedySample(IntPtr logitsPtr, int nVocab)
        {
            if (logitsPtr == IntPtr.Zero)
                throw new InvalidOperationException("Logits pointer is null.");

            int bestToken = 0;
            float bestLogit = ReadFloat(logitsPtr, 0);

            for (int i = 1; i < nVocab; i++)
            {
                float logit = ReadFloat(logitsPtr, i);
                if (logit > bestLogit)
                {
                    bestLogit = logit;
                    bestToken = i;
                }
            }

            return bestToken;
        }

        private string TokenToString(int token)
        {
            var buf = new byte[256];
            int len = LlamaNative.llama_token_to_piece(_vocab, token, buf, buf.Length, 0, 0);
            if (len <= 0) return string.Empty;
            return Encoding.UTF8.GetString(buf, 0, len);
        }

        private bool IsEndOfGeneration(int token)
        {
            return LlamaNative.llama_vocab_is_eog(_vocab, token) != 0;
        }

        private static float ReadFloat(IntPtr ptr, int index)
        {
            // Read 4 bytes and reinterpret as float without unsafe code.
            int bits = Marshal.ReadInt32(ptr + index * 4);
            return Int32BitsToFloat(bits);
        }

        private static float Int32BitsToFloat(int value)
        {
            // BitConverter.ToSingle on a 4-byte stack allocation avoids unsafe.
            byte[] bytes = BitConverter.GetBytes(value);
            return BitConverter.ToSingle(bytes, 0);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(LlamaClient));
            }
        }
    }
}
