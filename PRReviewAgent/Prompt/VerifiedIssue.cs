namespace PRReviewAgent.Prompt
{
    public struct VerifiedIssue
    {
        public string candidate_id { get; set; }
        public bool valid { get; set; }
        public string evidence { get; set; }
        public string impact { get; set; }
        public string suggested_fix { get; set; }
        public string confidence { get; set; }
        public string? rule_id { get; set; }
    }

    [LlmSchema("verified_schema", "structure of verified issue list")]
    public class VerifiedResponse
    {
        public VerifiedIssue[] issues { get; set; } = new VerifiedIssue[0];
    }
}
