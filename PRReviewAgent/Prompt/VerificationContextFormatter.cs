using System.Text;

namespace PRReviewAgent.Prompt
{
    public static class VerificationContextFormatter
    {
        public static string Format(VerificationContext context, CandidateIssue candidate)
        {
            var sb = new StringBuilder();

            sb.Append("[Candidate]\n");
            sb.Append($"ID: {context.CandidateId}\n");
            sb.Append($"Location: {candidate.location}\n");
            if (!string.IsNullOrEmpty(candidate.hypothesis))
                sb.Append($"Hypothesis: {candidate.hypothesis}\n");
            sb.Append('\n');

            foreach (SourceContextItem item in context.Items)
            {
                sb.Append($"[{KindLabel(item.Kind)}]\n");
                sb.Append($"FILE: {item.Path}\n");
                sb.Append($"SYMBOL: {item.Symbol}\n\n");
                sb.Append(item.Source);
                sb.Append("\n\n");
            }

            if (context.UnresolvedTargets.Count > 0)
            {
                sb.Append("[Unresolved]\n");
                foreach (string target in context.UnresolvedTargets)
                    sb.Append($"- {target}\n");
                sb.Append('\n');
            }

            if (context.Truncated)
                sb.Append("[Context truncated due to budget limits]\n");

            return sb.ToString();
        }

        internal static string KindLabel(VerificationContextKind kind) => kind switch
        {
            VerificationContextKind.ChangedScope => "Changed Scope",
            VerificationContextKind.Declaration => "Declaration",
            VerificationContextKind.Definition => "Definition",
            VerificationContextKind.ContainingType => "Containing Type",
            VerificationContextKind.Field => "Field",
            VerificationContextKind.DirectCaller => "Direct Caller",
            VerificationContextKind.DirectCallee => "Direct Callee",
            VerificationContextKind.ReferencedType => "Referenced Type",
            VerificationContextKind.RelatedDeclaration => "Related Declaration",
            VerificationContextKind.PairFile => "Pair File",
            _ => "Context",
        };
    }
}
