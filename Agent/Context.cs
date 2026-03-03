using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agent
{
    public class Context
    {
        public static Context Instance => context_;
        public static void Initialize(string url)
        {
            context_.agents_ = new Agents(url);
        }

        private static Context context_ = new Context();

        public Agents? Agents => agents_;
        public CancellationToken CancellationToken => CancellationTokenSource_.Token;
        private Agents? agents_;
        private CancellationTokenSource CancellationTokenSource_ = new CancellationTokenSource();
    }
}
