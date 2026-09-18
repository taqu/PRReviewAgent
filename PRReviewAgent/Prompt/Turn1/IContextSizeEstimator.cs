namespace PRReviewAgent.Prompt.Turn1
{
    public interface IContextSizeEstimator
    {
        int Estimate(string text);
    }

    public sealed class CharContextSizeEstimator : IContextSizeEstimator
    {
        public static readonly CharContextSizeEstimator Instance = new();
        public int Estimate(string text) => text.Length;
    }
}
