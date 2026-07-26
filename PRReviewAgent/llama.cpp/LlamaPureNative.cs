using System;
using System.Runtime.InteropServices;

internal static class LlamaPureNative
{
    private const string LlamaDll = "llama";

    public enum LlamaSplitMode : int
    {
        None   = 0,
        Layer  = 1,
        Row    = 2,
        Tensor = 3,
    }

    public enum LlamaLoadMode : int
    {
        None     = 0,
        Mmap     = 1,
        Mlock    = 2,
        DirectIo = 3,
    }

    public enum LlamaContextType : int
    {
        Default = 0,
        Mtp     = 1,
    }

    public enum LlamaRopeScalingType : int
    {
        Unspecified = -1,
        None        = 0,
        Linear      = 1,
        Yarn        = 2,
        LongRope    = 3,
    }

    public enum LlamaPoolingType : int
    {
        Unspecified = -1,
        None        = 0,
        Mean        = 1,
        Cls         = 2,
        Last        = 3,
        Rank        = 4,
    }

    public enum LlamaAttentionType : int
    {
        Unspecified = -1,
        Causal      = 0,
        NonCausal   = 1,
    }

    public enum LlamaFlashAttnType : int
    {
        Auto     = -1,
        Disabled = 0,
        Enabled  = 1,
    }

    public enum GgmlType : int
    {
        F32  = 0,
        F16  = 1,
        Q4_0 = 2,
        Q4_1 = 3,
        Q5_0 = 6,
        Q5_1 = 7,
        Q8_0 = 8,
        Q8_1 = 9,
        Q2K  = 10,
        Q3KS = 11,
        Q3KM = 12,
        Q3KL = 13,
        Q4KS = 14,
        Q4KM = 15,
        Q5KS = 16,
        Q5KM = 17,
        Q6K  = 18,
        Q8K  = 19,
        Iq2Xxs = 20,
        Iq2Xs  = 21,
        Iq3Xxs = 22,
        Iq1S   = 23,
        Iq4Nl  = 24,
        Iq3S   = 25,
        Iq2S   = 26,
        Iq4Xs  = 27,
        I8     = 28,
        I16    = 29,
        I32    = 30,
        I64    = 31,
        F64    = 32,
        Iq1M   = 33,
        Bf16   = 34,
        Count  = 39,
    }

    // llama_model_params matches the C struct layout on x64:
    //   offset 0:  devices (ptr 8)
    //   offset 8:  tensor_buft_overrides (ptr 8)
    //   offset 16: n_gpu_layers (int32 4)
    //   offset 20: split_mode (enum/int32 4)
    //   offset 24: load_mode (enum/int32 4)
    //   offset 28: main_gpu (int32 4)
    //   offset 32: tensor_split (ptr 8)
    //   offset 40: progress_callback (ptr 8)
    //   offset 48: progress_callback_user_data (ptr 8)
    //   offset 56: kv_overrides (ptr 8)
    //   offset 64: vocab_only (byte 1)
    //   offset 65: check_tensors (byte 1)
    //   offset 66: use_extra_bufts (byte 1)
    //   offset 67: no_host (byte 1)
    //   offset 68: no_alloc (byte 1)
    //   offset 69-71: padding (3 bytes)
    //   total: 72 bytes
    [StructLayout(LayoutKind.Sequential)]
    public struct LlamaModelParams
    {
        public IntPtr devices;
        public IntPtr tensor_buft_overrides;
        public int n_gpu_layers;
        public LlamaSplitMode split_mode;
        public LlamaLoadMode load_mode;
        public int main_gpu;
        public IntPtr tensor_split;
        public IntPtr progress_callback;
        public IntPtr progress_callback_user_data;
        public IntPtr kv_overrides;
        public byte vocab_only;
        public byte check_tensors;
        public byte use_extra_bufts;
        public byte no_host;
        public byte no_alloc;
    }

    // llama_context_params matches the C struct layout on x64:
    //   offset 0:   n_ctx (uint32 4)
    //   offset 4:   n_batch (uint32 4)
    //   offset 8:   n_ubatch (uint32 4)
    //   offset 12:  n_seq_max (uint32 4)
    //   offset 16:  n_rs_seq (uint32 4)
    //   offset 20:  n_outputs_max (uint32 4)
    //   offset 24:  n_threads (int32 4)
    //   offset 28:  n_threads_batch (int32 4)
    //   offset 32:  ctx_type (enum/int32 4)
    //   offset 36:  rope_scaling_type (enum/int32 4)
    //   offset 40:  pooling_type (enum/int32 4)
    //   offset 44:  attention_type (enum/int32 4)
    //   offset 48:  flash_attn_type (enum/int32 4)
    //   offset 52:  rope_freq_base (float 4)
    //   offset 56:  rope_freq_scale (float 4)
    //   offset 60:  yarn_ext_factor (float 4)
    //   offset 64:  yarn_attn_factor (float 4)
    //   offset 68:  yarn_beta_fast (float 4)
    //   offset 72:  yarn_beta_slow (float 4)
    //   offset 76:  yarn_orig_ctx (uint32 4)
    //   offset 80:  defrag_thold (float 4)
    //   offset 84:  _pad (4 bytes, aligns cb_eval to 8)
    //   offset 88:  cb_eval (ptr 8)
    //   offset 96:  cb_eval_user_data (ptr 8)
    //   offset 104: type_k (enum/int32 4)
    //   offset 108: type_v (enum/int32 4)
    //   offset 112: abort_callback (ptr 8)
    //   offset 120: abort_callback_data (ptr 8)
    //   offset 128: embeddings (byte 1)
    //   offset 129: offload_kqv (byte 1)
    //   offset 130: no_perf (byte 1)
    //   offset 131: op_offload (byte 1)
    //   offset 132: swa_full (byte 1)
    //   offset 133: kv_unified (byte 1)
    //   offset 134: _pad2 (2 bytes, aligns samplers to 8)
    //   offset 136: samplers (ptr 8)
    //   offset 144: n_samplers (size_t 8)
    //   offset 152: ctx_other (ptr 8)
    //   total: 160 bytes
    [StructLayout(LayoutKind.Explicit, Size = 160)]
    public struct LlamaContextParams
    {
        [FieldOffset(0)]   public uint n_ctx;
        [FieldOffset(4)]   public uint n_batch;
        [FieldOffset(8)]   public uint n_ubatch;
        [FieldOffset(12)]  public uint n_seq_max;
        [FieldOffset(16)]  public uint n_rs_seq;
        [FieldOffset(20)]  public uint n_outputs_max;
        [FieldOffset(24)]  public int n_threads;
        [FieldOffset(28)]  public int n_threads_batch;
        [FieldOffset(32)]  public LlamaContextType ctx_type;
        [FieldOffset(36)]  public LlamaRopeScalingType rope_scaling_type;
        [FieldOffset(40)]  public LlamaPoolingType pooling_type;
        [FieldOffset(44)]  public LlamaAttentionType attention_type;
        [FieldOffset(48)]  public LlamaFlashAttnType flash_attn_type;
        [FieldOffset(52)]  public float rope_freq_base;
        [FieldOffset(56)]  public float rope_freq_scale;
        [FieldOffset(60)]  public float yarn_ext_factor;
        [FieldOffset(64)]  public float yarn_attn_factor;
        [FieldOffset(68)]  public float yarn_beta_fast;
        [FieldOffset(72)]  public float yarn_beta_slow;
        [FieldOffset(76)]  public uint yarn_orig_ctx;
        [FieldOffset(80)]  public float defrag_thold;
        [FieldOffset(88)]  public IntPtr cb_eval;
        [FieldOffset(96)]  public IntPtr cb_eval_user_data;
        [FieldOffset(104)] public GgmlType type_k;
        [FieldOffset(108)] public GgmlType type_v;
        [FieldOffset(112)] public IntPtr abort_callback;
        [FieldOffset(120)] public IntPtr abort_callback_data;
        [FieldOffset(128)] public byte embeddings;
        [FieldOffset(129)] public byte offload_kqv;
        [FieldOffset(130)] public byte no_perf;
        [FieldOffset(131)] public byte op_offload;
        [FieldOffset(132)] public byte swa_full;
        [FieldOffset(133)] public byte kv_unified;
        [FieldOffset(136)] public IntPtr samplers;
        [FieldOffset(144)] public UIntPtr n_samplers;
        [FieldOffset(152)] public IntPtr ctx_other;
    }

    // llama_batch: n_tokens + 6 pointers = 4 + 6*8 = 52 bytes + 4 pad = 56 bytes
    [StructLayout(LayoutKind.Sequential)]
    public struct LlamaBatch
    {
        public int n_tokens;
        public IntPtr token;    // llama_token*   (int32_t*)
        public IntPtr embd;     // float*
        public IntPtr pos;      // llama_pos*     (int32_t*)
        public IntPtr n_seq_id; // int32_t*
        public IntPtr seq_id;   // llama_seq_id** (int32_t**)
        public IntPtr logits;   // int8_t*
    }

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_backend_init();

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_backend_free();

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern LlamaModelParams llama_model_default_params();

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern LlamaContextParams llama_context_default_params();

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern IntPtr llama_model_load_from_file(string path_model, LlamaModelParams @params);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_model_free(IntPtr model);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_init_from_model(IntPtr model, LlamaContextParams @params);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_free(IntPtr ctx);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_model_get_vocab(IntPtr model);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_n_tokens(IntPtr vocab);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_model_n_embd(IntPtr model);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_bos(IntPtr vocab);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_eos(IntPtr vocab);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern byte llama_vocab_is_eog(IntPtr vocab, int token);

    // bool params are C99 _Bool (1 byte); marshal as byte to avoid BOOL (4-byte) default
    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern int llama_tokenize(
        IntPtr vocab,
        string text,
        int text_len,
        int[] tokens,
        int n_tokens_max,
        byte add_special,
        byte parse_special);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_token_to_piece(
        IntPtr vocab,
        int token,
        byte[] buf,
        int length,
        int lstrip,
        byte special);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern LlamaBatch llama_batch_init(int n_tokens, int embd, int n_seq_max);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_batch_free(LlamaBatch batch);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_decode(IntPtr ctx, LlamaBatch batch);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_get_logits(IntPtr ctx);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_get_logits_ith(IntPtr ctx, int i);

    [DllImport(LlamaDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_get_embeddings_ith(IntPtr ctx, int i);
}
