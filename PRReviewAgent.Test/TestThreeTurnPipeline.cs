using PRReviewAgent.Prompt;
using PRReviewAgent.Services;
using System.Text;
using System.Text.Json;

namespace PRReviewAgent.Test;

[TestClass]
public class TestThreeTurnPipeline
{
    // -----------------------------------------------------------------------
    // VerifiedResponse deserialization: valid candidate
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerifiedResponse_ValidCandidate_Deserializes()
    {
        string json = """
            {
              "issues": [
                {
                  "candidate_id": "c0",
                  "valid": true,
                  "evidence": "Foo owns Resource through resource_. Foo::Open returns resource_.get().",
                  "impact": "The stored pointer can become dangling after Foo is destroyed.",
                  "suggested_fix": "Return a shared handle whose lifetime is guaranteed by the API.",
                  "confidence": "high"
                }
              ]
            }
            """;
        VerifiedResponse? result = JsonSerializer.Deserialize<VerifiedResponse>(json);
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.issues.Length);
        VerifiedIssue vi = result.issues[0];
        Assert.AreEqual("c0", vi.candidate_id);
        Assert.IsTrue(vi.valid);
        Assert.IsFalse(string.IsNullOrEmpty(vi.evidence));
        Assert.AreEqual("high", vi.confidence);
    }

    // -----------------------------------------------------------------------
    // VerifiedResponse deserialization: invalid candidate
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerifiedResponse_InvalidCandidate_Deserializes()
    {
        string json = """
            {
              "issues": [
                {
                  "candidate_id": "c1",
                  "valid": false,
                  "evidence": "All supplied callers consume the pointer synchronously.",
                  "impact": "",
                  "suggested_fix": "",
                  "confidence": "high"
                }
              ]
            }
            """;
        VerifiedResponse? result = JsonSerializer.Deserialize<VerifiedResponse>(json);
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.issues.Length);
        VerifiedIssue vi = result.issues[0];
        Assert.AreEqual("c1", vi.candidate_id);
        Assert.IsFalse(vi.valid);
    }

    // -----------------------------------------------------------------------
    // VerifiedResponse: empty result
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerifiedResponse_Empty_Deserializes()
    {
        VerifiedResponse? result = JsonSerializer.Deserialize<VerifiedResponse>("{\"issues\":[]}");
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.issues.Length);
    }

    // -----------------------------------------------------------------------
    // VerifiedIssue has no severity field
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerifiedIssue_HasNoSeverityField()
    {
        string[] propNames = typeof(VerifiedIssue).GetProperties()
            .Select(p => p.Name.ToLowerInvariant())
            .ToArray();
        Assert.IsFalse(propNames.Contains("severity"), "VerifiedIssue must not have severity");
        Assert.IsFalse(propNames.Contains("critical"), "VerifiedIssue must not have critical");
        Assert.IsFalse(propNames.Contains("major"), "VerifiedIssue must not have major");
        Assert.IsFalse(propNames.Contains("minor"), "VerifiedIssue must not have minor");
    }

    // -----------------------------------------------------------------------
    // VerifiedIssue has required fields: candidate_id, valid, evidence, impact, suggested_fix, confidence
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerifiedIssue_HasRequiredVerificationFields()
    {
        string[] propNames = typeof(VerifiedIssue).GetProperties()
            .Select(p => p.Name.ToLowerInvariant())
            .ToArray();
        Assert.IsTrue(propNames.Contains("candidate_id"));
        Assert.IsTrue(propNames.Contains("valid"));
        Assert.IsTrue(propNames.Contains("evidence"));
        Assert.IsTrue(propNames.Contains("impact"));
        Assert.IsTrue(propNames.Contains("suggested_fix"));
        Assert.IsTrue(propNames.Contains("confidence"));
    }

    // -----------------------------------------------------------------------
    // Candidate ID preserved through verification
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CandidateIdPreservation_VerifiedIssueMatchesCandidate()
    {
        VerifiedIssue vi = new VerifiedIssue
        {
            candidate_id = "c2",
            valid = true,
            evidence = "evidence text",
            impact = "impact text",
            suggested_fix = "fix text",
            confidence = "medium",
        };
        Assert.AreEqual("c2", vi.candidate_id);
    }

    // -----------------------------------------------------------------------
    // Mixed candidates: filtering retains only valid issues
    // -----------------------------------------------------------------------

    [TestMethod]
    public void MixedCandidates_FilteringKeepsOnlyValid()
    {
        VerifiedIssue[] issues = new[]
        {
            new VerifiedIssue { candidate_id = "c0", valid = true,  evidence = "e0", impact = "i0", suggested_fix = "f0", confidence = "high" },
            new VerifiedIssue { candidate_id = "c1", valid = false, evidence = "e1", impact = "",   suggested_fix = "",   confidence = "high" },
            new VerifiedIssue { candidate_id = "c2", valid = true,  evidence = "e2", impact = "i2", suggested_fix = "f2", confidence = "medium" },
        };

        VerifiedIssue[] valid = issues.Where(i => i.valid).ToArray();
        Assert.AreEqual(2, valid.Length);
        Assert.AreEqual("c0", valid[0].candidate_id);
        Assert.AreEqual("c2", valid[1].candidate_id);
    }

    // -----------------------------------------------------------------------
    // All candidates rejected → verified list is empty
    // -----------------------------------------------------------------------

    [TestMethod]
    public void AllCandidatesRejected_EmptyVerifiedList()
    {
        VerifiedIssue[] issues = new[]
        {
            new VerifiedIssue { candidate_id = "c0", valid = false, evidence = "not supported", impact = "", suggested_fix = "", confidence = "high" },
            new VerifiedIssue { candidate_id = "c1", valid = false, evidence = "context insufficient", impact = "", suggested_fix = "", confidence = "high" },
        };

        VerifiedIssue[] valid = issues.Where(i => i.valid).ToArray();
        Assert.AreEqual(0, valid.Length);
    }

    // -----------------------------------------------------------------------
    // BuildTurn2 (Verification): includes candidate and context
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BuildTurn2_Verification_IncludesCandidateAndContext()
    {
        ReviewRequest req = new ReviewRequest
        {
            ReviewRulesTurn2 = "VERIFICATION_TEMPLATE",
        };

        CandidateIssue candidate = new CandidateIssue
        {
            location = "foo.cpp: Foo::Open",
            category = "lifetime",
            hypothesis = "Returned pointer may outlive the owner.",
            trigger = "Foo::Open now returns resource_.get().",
            verify_symbols = new[] { "Foo::~Foo" },
            candidate_id = "c0",
        };

        VerificationContext ctx = new VerificationContext
        {
            CandidateId = "c0",
            Items = new List<SourceContextItem>
            {
                new SourceContextItem
                {
                    Path = "foo.cpp",
                    Symbol = "Foo::Open",
                    Kind = VerificationContextKind.ChangedScope,
                    Source = "void* Foo::Open() { return resource_.get(); }",
                }
            }
        };

        string prompt = PromptBuilder.BuildTurn2(req, candidate, ctx, new StringBuilder());

        Assert.IsTrue(prompt.Contains("VERIFICATION_TEMPLATE"), "Should include template");
        Assert.IsTrue(prompt.Contains("c0"), "Should include candidate ID");
        Assert.IsTrue(prompt.Contains("foo.cpp: Foo::Open"), "Should include location");
        Assert.IsTrue(prompt.Contains("Returned pointer may outlive the owner"), "Should include hypothesis");
        Assert.IsTrue(prompt.Contains("Foo::Open"), "Should include symbol from context");
        Assert.IsTrue(prompt.Contains("resource_.get()"), "Should include source code");
    }

    // -----------------------------------------------------------------------
    // BuildTurn2 (Verification): does not include raw AST JSON
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BuildTurn2_Verification_DoesNotIncludeRawAstJson()
    {
        ReviewRequest req = new ReviewRequest { ReviewRulesTurn2 = "TMPL" };

        CandidateIssue candidate = new CandidateIssue
        {
            location = "foo.cpp: Foo::Open",
            category = "lifetime",
            hypothesis = "h",
            trigger = "t",
            verify_symbols = Array.Empty<string>(),
            candidate_id = "c0",
        };

        VerificationContext ctx = new VerificationContext
        {
            CandidateId = "c0",
            Items = new List<SourceContextItem>
            {
                new SourceContextItem
                {
                    Path = "foo.cpp",
                    Symbol = "Foo::Open",
                    Kind = VerificationContextKind.ChangedScope,
                    Source = "int foo() { return 0; }",
                }
            }
        };

        string prompt = PromptBuilder.BuildTurn2(req, candidate, ctx, new StringBuilder());

        // Should not embed raw AST JSON (curly braces typical in AST JSON, but source code is plain text)
        Assert.IsTrue(prompt.Contains("int foo()"), "Should include source code");
        // The prompt should not contain AstJson-style content (no "functions" or "types" JSON keys)
        Assert.IsFalse(prompt.Contains("\"functions\""), "Should not include raw AST JSON");
        Assert.IsFalse(prompt.Contains("\"types\""), "Should not include raw AST JSON");
    }

    // -----------------------------------------------------------------------
    // BuildTurn3 (Finalization): includes evidence, impact, suggested_fix
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BuildTurn3_Finalization_IncludesVerifiedIssueFields()
    {
        ReviewRequest req = new ReviewRequest
        {
            ReviewRulesTurn3 = "FINALIZATION_TEMPLATE\n\n# Verified Issues",
        };

        VerifiedIssue[] issues = new[]
        {
            new VerifiedIssue
            {
                candidate_id = "c0",
                valid = true,
                evidence = "Foo owns Resource through resource_. Open returns resource_.get().",
                impact = "The pointer can dangle after Foo is destroyed.",
                suggested_fix = "Return a shared handle.",
                confidence = "high",
            },
        };

        string prompt = PromptBuilder.BuildTurn3(req, issues, new StringBuilder());

        Assert.IsTrue(prompt.Contains("FINALIZATION_TEMPLATE"), "Should include template");
        Assert.IsTrue(prompt.Contains("c0"), "Should include candidate ID");
        Assert.IsTrue(prompt.Contains("Foo owns Resource"), "Should include evidence");
        Assert.IsTrue(prompt.Contains("pointer can dangle"), "Should include impact");
        Assert.IsTrue(prompt.Contains("shared handle"), "Should include suggested_fix");
        Assert.IsTrue(prompt.Contains("high"), "Should include confidence");
    }

    // -----------------------------------------------------------------------
    // BuildTurn3: does not include source code (no SourceContextItem content)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BuildTurn3_Finalization_DoesNotIncludeSourceCode()
    {
        ReviewRequest req = new ReviewRequest { ReviewRulesTurn3 = "TMPL" };

        VerifiedIssue[] issues = new[]
        {
            new VerifiedIssue
            {
                candidate_id = "c0",
                valid = true,
                evidence = "evidence only",
                impact = "impact only",
                suggested_fix = "fix only",
                confidence = "high",
            }
        };

        string prompt = PromptBuilder.BuildTurn3(req, issues, new StringBuilder());

        // No raw source code fences or AST keys
        Assert.IsFalse(prompt.Contains("```cpp"), "Should not include source code fences");
        Assert.IsFalse(prompt.Contains("\"functions\""), "Should not include AST JSON");
    }

    // -----------------------------------------------------------------------
    // BuildTurn3: includes attribution metadata request when candidate IDs present
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BuildTurn3_IncludesAttributionMetadataRequest()
    {
        ReviewRequest req = new ReviewRequest { ReviewRulesTurn3 = "TMPL" };

        VerifiedIssue[] issues = new[]
        {
            new VerifiedIssue { candidate_id = "c0", valid = true, evidence = "e", impact = "i", suggested_fix = "f", confidence = "high" },
        };

        string prompt = PromptBuilder.BuildTurn3(req, issues, new StringBuilder());

        Assert.IsTrue(prompt.Contains("SELECTED_CANDIDATES"), "Should include attribution metadata request");
    }

    // -----------------------------------------------------------------------
    // BuildTurn3: multiple verified issues included
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BuildTurn3_MultipleVerifiedIssues_AllPresent()
    {
        ReviewRequest req = new ReviewRequest { ReviewRulesTurn3 = "TMPL" };

        VerifiedIssue[] issues = new[]
        {
            new VerifiedIssue { candidate_id = "c0", valid = true, evidence = "e0", impact = "i0", suggested_fix = "f0", confidence = "high" },
            new VerifiedIssue { candidate_id = "c2", valid = true, evidence = "e2", impact = "i2", suggested_fix = "f2", confidence = "medium" },
        };

        string prompt = PromptBuilder.BuildTurn3(req, issues, new StringBuilder());

        Assert.IsTrue(prompt.Contains("c0"), "Should include c0");
        Assert.IsTrue(prompt.Contains("c2"), "Should include c2");
        Assert.IsTrue(prompt.Contains("e0"), "Should include e0 evidence");
        Assert.IsTrue(prompt.Contains("e2"), "Should include e2 evidence");
    }

    // -----------------------------------------------------------------------
    // Rule attribution: rule_id re-injected from candidateToRuleId
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RuleAttribution_InjectedFromCandidateMap()
    {
        Dictionary<string, string> candidateToRuleId = new Dictionary<string, string>
        {
            ["c0"] = "rule-lifetime-001",
        };

        VerifiedIssue vi = new VerifiedIssue
        {
            candidate_id = "c0",
            valid = true,
            evidence = "e",
            impact = "i",
            suggested_fix = "f",
            confidence = "high",
            rule_id = null,
        };

        // Simulate the re-injection logic from the webhook task
        if (string.IsNullOrEmpty(vi.rule_id) && candidateToRuleId.TryGetValue(vi.candidate_id, out string? rid))
            vi.rule_id = rid;

        Assert.AreEqual("rule-lifetime-001", vi.rule_id);
    }

    // -----------------------------------------------------------------------
    // VerificationContext truncated: formatter includes truncation notice
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationContextFormatter_IncludesTruncationNotice_WhenTruncated()
    {
        CandidateIssue candidate = new CandidateIssue
        {
            location = "foo.cpp: Foo::Open",
            hypothesis = "h",
            candidate_id = "c0",
            category = "c",
            trigger = "t",
            verify_symbols = Array.Empty<string>(),
        };

        VerificationContext ctx = new VerificationContext
        {
            CandidateId = "c0",
            Truncated = true,
        };

        string formatted = VerificationContextFormatter.Format(ctx, candidate);
        Assert.IsTrue(formatted.Contains("truncated"), "Formatter should note truncation");
    }

    // -----------------------------------------------------------------------
    // VerificationContext unresolved targets: formatter lists them
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationContextFormatter_ListsUnresolvedTargets()
    {
        CandidateIssue candidate = new CandidateIssue
        {
            location = "foo.cpp: Foo::Open",
            hypothesis = "h",
            candidate_id = "c0",
            category = "c",
            trigger = "t",
            verify_symbols = Array.Empty<string>(),
        };

        VerificationContext ctx = new VerificationContext
        {
            CandidateId = "c0",
            UnresolvedTargets = new List<string> { "UnknownSymbol" },
        };

        string formatted = VerificationContextFormatter.Format(ctx, candidate);
        Assert.IsTrue(formatted.Contains("Unresolved"), "Should list unresolved targets");
        Assert.IsTrue(formatted.Contains("UnknownSymbol"), "Should name the unresolved symbol");
    }

    // -----------------------------------------------------------------------
    // ReviewRequest has ReviewRulesTurn3
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ReviewRequest_HasReviewRulesTurn3()
    {
        ReviewRequest req = new ReviewRequest();
        req.ReviewRulesTurn3 = "turn3 template";
        Assert.AreEqual("turn3 template", req.ReviewRulesTurn3);
    }
}
