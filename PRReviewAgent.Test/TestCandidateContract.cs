using PRReviewAgent.Prompt;
using PRReviewAgent.Services;
using System.Text;
using System.Text.Json;

namespace PRReviewAgent.Test;

[TestClass]
public class TestCandidateContract
{
    // -------------------------------------------------------------------------
    // Empty result: {"issues":[]} deserializes to zero candidates
    // -------------------------------------------------------------------------

    [TestMethod]
    public void EmptyResult_DeserializesToZeroCandidates()
    {
        CandidateResponse? result = JsonSerializer.Deserialize<CandidateResponse>("{\"issues\":[]}");
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.issues.Length);
    }

    // -------------------------------------------------------------------------
    // Candidate structure contains required discovery fields
    // -------------------------------------------------------------------------

    [TestMethod]
    public void CandidateIssue_HasRequiredDiscoveryFields()
    {
        string json = """
            {
              "issues": [
                {
                  "location": "foo.cpp: Foo::Open",
                  "category": "lifetime",
                  "hypothesis": "The returned pointer may outlive the owning object.",
                  "trigger": "Foo::Open now returns resource_.get() instead of an owning object.",
                  "verify_symbols": ["Foo::~Foo", "Foo::resource_"]
                }
              ]
            }
            """;
        CandidateResponse? result = JsonSerializer.Deserialize<CandidateResponse>(json);
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.issues.Length);
        CandidateIssue c = result.issues[0];
        Assert.AreEqual("foo.cpp: Foo::Open", c.location);
        Assert.AreEqual("lifetime", c.category);
        Assert.IsFalse(string.IsNullOrEmpty(c.hypothesis));
        Assert.IsFalse(string.IsNullOrEmpty(c.trigger));
        Assert.IsNotNull(c.verify_symbols);
        Assert.AreEqual(2, c.verify_symbols.Length);
    }

    // -------------------------------------------------------------------------
    // No severity: CandidateIssue has no severity field
    // -------------------------------------------------------------------------

    [TestMethod]
    public void CandidateIssue_HasNoSeverityField()
    {
        var props = typeof(CandidateIssue).GetProperties();
        string[] propNames = props.Select(p => p.Name.ToLowerInvariant()).ToArray();
        Assert.IsFalse(propNames.Contains("severity"), "CandidateIssue must not have a severity field");
        Assert.IsFalse(propNames.Contains("critical"), "CandidateIssue must not have a critical field");
        Assert.IsFalse(propNames.Contains("major"), "CandidateIssue must not have a major field");
        Assert.IsFalse(propNames.Contains("minor"), "CandidateIssue must not have a minor field");
    }

    // -------------------------------------------------------------------------
    // No confidence: CandidateIssue has no confidence field
    // -------------------------------------------------------------------------

    [TestMethod]
    public void CandidateIssue_HasNoConfidenceField()
    {
        var props = typeof(CandidateIssue).GetProperties();
        Assert.IsFalse(props.Any(p => p.Name == "confidence"), "CandidateIssue must not have a confidence field");
    }

    // -------------------------------------------------------------------------
    // verify_symbols can be empty array
    // -------------------------------------------------------------------------

    [TestMethod]
    public void CandidateIssue_AcceptsEmptyVerifySymbols()
    {
        string json = """
            {
              "issues": [
                {
                  "location": "bar.cpp: Bar::Run",
                  "category": "error_handling",
                  "hypothesis": "Return value is not checked.",
                  "trigger": "Bar::Run now ignores the return value of Process().",
                  "verify_symbols": []
                }
              ]
            }
            """;
        CandidateResponse? result = JsonSerializer.Deserialize<CandidateResponse>(json);
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.issues.Length);
        Assert.IsNotNull(result.issues[0].verify_symbols);
        Assert.AreEqual(0, result.issues[0].verify_symbols.Length);
    }

    // -------------------------------------------------------------------------
    // candidate_id assignment: ids are deterministic c0, c1, c2...
    // -------------------------------------------------------------------------

    [TestMethod]
    public void CandidateIdAssignment_IsSequential()
    {
        CandidateResponse response = new CandidateResponse
        {
            issues = new[]
            {
                new CandidateIssue { location = "a.cpp", category = "correctness", hypothesis = "h1", trigger = "t1", verify_symbols = Array.Empty<string>() },
                new CandidateIssue { location = "b.cpp", category = "lifetime",    hypothesis = "h2", trigger = "t2", verify_symbols = Array.Empty<string>() },
            }
        };

        for (int i = 0; i < response.issues.Length; i++)
            response.issues[i].candidate_id = $"c{i}";

        Assert.AreEqual("c0", response.issues[0].candidate_id);
        Assert.AreEqual("c1", response.issues[1].candidate_id);
    }

    // -------------------------------------------------------------------------
    // BuildTurn2 presents hypothesis and trigger (not raw JSON schema)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void BuildTurn2_PresentsHypothesisAndTrigger()
    {
        CandidateResponse response = new CandidateResponse
        {
            issues = new[]
            {
                new CandidateIssue
                {
                    location = "foo.cpp: Foo::Open",
                    category = "lifetime",
                    hypothesis = "The returned pointer may outlive the owning object.",
                    trigger = "Foo::Open now returns resource_.get().",
                    verify_symbols = new[] { "Foo::~Foo" },
                    candidate_id = "c0"
                }
            }
        };

        ReviewRequest request = new ReviewRequest
        {
            MergeRequestTitle = "Test",
            ReviewRulesTurn2 = "# Issue Candidates\n"
        };

        string prompt = PromptBuilder.BuildTurn2(request, response, new StringBuilder());
        Assert.IsTrue(prompt.Contains("Hypothesis"), "Prompt must present hypothesis label");
        Assert.IsTrue(prompt.Contains("The returned pointer may outlive the owning object."), "Prompt must include hypothesis text");
        Assert.IsTrue(prompt.Contains("Changed-code trigger"), "Prompt must present trigger label");
        Assert.IsTrue(prompt.Contains("Foo::Open now returns resource_.get()"), "Prompt must include trigger text");
        Assert.IsTrue(prompt.Contains("Foo::~Foo"), "Prompt must include verify_symbols");
    }

    // -------------------------------------------------------------------------
    // BuildTurn2 includes attribution metadata request when candidate_ids present
    // -------------------------------------------------------------------------

    [TestMethod]
    public void BuildTurn2_IncludesAttributionMetadataWhenCandidateIdsPresent()
    {
        CandidateResponse response = new CandidateResponse
        {
            issues = new[]
            {
                new CandidateIssue
                {
                    location = "x.cpp",
                    category = "correctness",
                    hypothesis = "h",
                    trigger = "t",
                    verify_symbols = Array.Empty<string>(),
                    candidate_id = "c0"
                }
            }
        };

        ReviewRequest request = new ReviewRequest { MergeRequestTitle = string.Empty, ReviewRulesTurn2 = string.Empty };
        string prompt = PromptBuilder.BuildTurn2(request, response, new StringBuilder());
        Assert.IsTrue(prompt.Contains("SELECTED_CANDIDATES"), "Attribution metadata request must be present");
    }
}
