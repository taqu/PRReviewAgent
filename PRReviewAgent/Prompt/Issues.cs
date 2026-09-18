using System.ComponentModel.DataAnnotations;

namespace PRReviewAgent.Prompt
{
    public struct CandidateIssue
    {
        public string location { get; set; }
        public string category { get; set; }
        public string hypothesis { get; set; }
        public string trigger { get; set; }
        public string[] verify_symbols { get; set; }
        public string? rule_id { get; set; }
        public string? candidate_id { get; set; }
    }

    [LlmSchema("candidate_schema", "structure of candidate issue list")]
    public class CandidateResponse
    {
        public CandidateIssue[] issues { get; set; } = new CandidateIssue[0];
    }

    public struct Issue
    {
        [Required]
        public string location { get; set; }
        [Required]
        public string problem { get; set; }
        [Required]
        public string evidence { get; set; }
        [Required]
        public string impact { get; set; }
        [Required]
        public string suggested_fix { get; set; }
        [Required]
        public string confidence { get; set; }
        /// <summary>ID of the learned rule that triggered this finding. Null for general findings.</summary>
        public string? rule_id { get; set; }
        /// <summary>Internal candidate identifier assigned after Detection for Selection attribution tracking.</summary>
        public string? candidate_id { get; set; }
    }

    [LlmSchema("issues_schema", "structure of issue list")]
    public class IssuesResponse
    {
        [Required]
        public Issue[] issues { get; set; } = new Issue[0];
    }
}
