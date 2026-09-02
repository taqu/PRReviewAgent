using System.ComponentModel.DataAnnotations;

namespace PRReviewAgent.Prompt
{
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
    }

    [LlmSchema("issues_schema", "structure of issue list")]
    public class IssuesResponse
    {
        [Required]
        public Issue[] issues { get; set; } = new Issue[0];
    }
}
