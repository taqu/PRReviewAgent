namespace PRReviewAgent
{
    public class Context
    {
        public static Context Instance => context_;
        public static void Initialize()
        {
            context_.agents_ = new Agents();
        }

        private static Context context_ = new Context();

        public Agents? Agents => agents_;
        public CancellationToken CancellationToken => CancellationTokenSource_.Token;

        private Context()
        {
        }

        private Agents? agents_;
        private CancellationTokenSource CancellationTokenSource_ = new CancellationTokenSource();
    }
}
