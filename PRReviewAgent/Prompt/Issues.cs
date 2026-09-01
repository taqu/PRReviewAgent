namespace PRReviewAgent.Prompt
{
    public struct Issue
    {
        public string location { get; set; }
        public string problem { get; set; }
        public string evidence { get; set; }
        public string impact { get; set; }
        public string suggested_fix { get; set; }
        public string confidence { get; set; }
    }

    public class IssuesResponse
    {
        public Issue[] issues { get; set; } = new Issue[0];
    }
}
