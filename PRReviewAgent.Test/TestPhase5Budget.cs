using PRReviewAgent.Prompt;
using PRReviewAgent.Prompt.Turn1;
using PRReviewAgent.Services;
using System.Text;

namespace PRReviewAgent.Test;

[TestClass]
public class TestPhase5Budget
{
    // -----------------------------------------------------------------------
    // ReviewBudgetConfig defaults
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ReviewBudgetConfig_Defaults_AreReasonable()
    {
        ReviewBudgetConfig cfg = new ReviewBudgetConfig();
        Assert.AreEqual(128_000, cfg.Turn1MaxSourceChars);
        Assert.AreEqual(32_000, cfg.Turn1FullFileThresholdChars);
        Assert.AreEqual(8_000, cfg.Turn1SemanticSummaryMaxChars);
        Assert.AreEqual(32_000, cfg.VerificationMaxContextChars);
        Assert.AreEqual(16, cfg.VerificationMaxContextItems);
        Assert.AreEqual(5, cfg.VerificationMaxDirectCallers);
        Assert.AreEqual(5, cfg.VerificationMaxDirectCallees);
        Assert.AreEqual(8, cfg.MaxCandidatesPerGroup);
        Assert.AreEqual(8, cfg.MaxVerificationCandidatesPerGroup);
    }

    // -----------------------------------------------------------------------
    // IContextSizeEstimator: char estimator counts characters
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CharContextSizeEstimator_ReturnsCharCount()
    {
        IContextSizeEstimator est = CharContextSizeEstimator.Instance;
        Assert.AreEqual(5, est.Estimate("hello"));
        Assert.AreEqual(0, est.Estimate(string.Empty));
    }

    // -----------------------------------------------------------------------
    // Turn1ContextMetrics: fields exist
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Turn1ContextMetrics_HasExpectedFields()
    {
        Turn1ContextMetrics m = new Turn1ContextMetrics
        {
            FileCount = 3,
            FullFileCount = 2,
            PartialFileCount = 1,
            DiffOnlyCount = 0,
            EstimatedSourceChars = 50_000,
            SemanticSummaryChars = 400,
            Truncated = false,
        };
        Assert.AreEqual(3, m.FileCount);
        Assert.AreEqual(2, m.FullFileCount);
        Assert.AreEqual(1, m.PartialFileCount);
        Assert.AreEqual(50_000, m.EstimatedSourceChars);
        Assert.IsFalse(m.Truncated);
    }

    // -----------------------------------------------------------------------
    // Small file stays full
    // -----------------------------------------------------------------------

    [TestMethod]
    public void SmallFile_StaysFull_BelowThreshold()
    {
        ReviewContext ctx = new ReviewContext
        {
            Path = "foo.cpp",
            Filename = "foo.cpp",
            Diff = "@@ -1,3 +1,3 @@\n-old\n+new\n context",
            ChangedFile = "int foo() { return 1; }",
        };

        ReviewBudgetConfig budget = new ReviewBudgetConfig
        {
            Turn1MaxSourceChars = 128_000,
            Turn1FullFileThresholdChars = 32_000,
            Turn1SemanticSummaryMaxChars = 8_000,
        };

        (string context, Turn1ContextMetrics metrics) = Turn1ContextBuilder.BuildWithMetrics(
            new[] { ctx }, budget);

        Assert.IsTrue(metrics.FullFileCount >= 1, "Small file should be counted as full");
        Assert.AreEqual(0, metrics.PartialFileCount);
        Assert.IsTrue(context.Contains("int foo()"), "Full source should be in context");
    }

    // -----------------------------------------------------------------------
    // Large file uses partial mode
    // -----------------------------------------------------------------------

    [TestMethod]
    public void LargeFile_UsesPartialMode_WhenExceedsThreshold()
    {
        string largeSource = string.Concat(Enumerable.Repeat("int x = 0;\n", 4000)); // ~44K chars

        ReviewContext ctx = new ReviewContext
        {
            Path = "big.cpp",
            Filename = "big.cpp",
            Diff = "@@ -1,3 +1,3 @@\n-old\n+new\n context",
            ChangedFile = largeSource,
        };

        ReviewBudgetConfig budget = new ReviewBudgetConfig
        {
            Turn1MaxSourceChars = 10_000,        // less than source (~44K), forcing partial mode
            Turn1FullFileThresholdChars = 5_000, // small threshold
            Turn1SemanticSummaryMaxChars = 8_000,
        };

        (string context, Turn1ContextMetrics metrics) = Turn1ContextBuilder.BuildWithMetrics(
            new[] { ctx }, budget);

        Assert.AreEqual(0, metrics.FullFileCount, "Large file should NOT be counted as full");
        Assert.AreEqual(1, metrics.PartialFileCount, "Large file should be counted as partial");
    }

    // -----------------------------------------------------------------------
    // Truncated flag set when budget exceeded
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BudgetExceeded_SetsTruncatedFlag()
    {
        // Two files each slightly over half the budget
        string source = string.Concat(Enumerable.Repeat("x", 60_000));

        ReviewContext ctx1 = new ReviewContext { Path = "a.cpp", Filename = "a.cpp", Diff = "+a", ChangedFile = source };
        ReviewContext ctx2 = new ReviewContext { Path = "b.cpp", Filename = "b.cpp", Diff = "+b", ChangedFile = source };

        ReviewBudgetConfig budget = new ReviewBudgetConfig
        {
            Turn1MaxSourceChars = 50_000,
            Turn1FullFileThresholdChars = 100_000,
            Turn1SemanticSummaryMaxChars = 8_000,
        };

        (string _, Turn1ContextMetrics metrics) = Turn1ContextBuilder.BuildWithMetrics(
            new[] { ctx1, ctx2 }, budget);

        Assert.IsTrue(metrics.Truncated, "Truncated flag should be set when budget is exceeded");
    }

    // -----------------------------------------------------------------------
    // SemanticSummary respects maxChars
    // -----------------------------------------------------------------------

    [TestMethod]
    public void SemanticSummaryBuilder_RespectsMaxChars()
    {
        ReviewContext ctx = new ReviewContext
        {
            Path = "foo.cpp",
            Filename = "foo.cpp",
            Diff = string.Empty,
            AstJson = BuildAstJson(),
        };

        string summary = SemanticSummaryBuilder.Build(new[] { ctx }, maxChars: 50);
        Assert.IsTrue(summary.Length <= 50, $"Summary should be <= 50 chars, was {summary.Length}");
    }

    // -----------------------------------------------------------------------
    // CandidatePreFilter: removes malformed candidates
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CandidatePreFilter_RemovesMalformed_MissingLocation()
    {
        CandidateIssue[] candidates = new[]
        {
            new CandidateIssue { candidate_id = "c0", location = "", hypothesis = "h", trigger = "t", category = "correctness", verify_symbols = Array.Empty<string>() },
            new CandidateIssue { candidate_id = "c1", location = "foo.cpp: Bar::Baz", hypothesis = "h", trigger = "t", category = "correctness", verify_symbols = Array.Empty<string>() },
        };

        var (accepted, rejected) = CandidatePreFilter.Filter(candidates);

        Assert.AreEqual(1, accepted.Length);
        Assert.AreEqual("c1", accepted[0].candidate_id);
        Assert.AreEqual(1, rejected.Length);
    }

    [TestMethod]
    public void CandidatePreFilter_RemovesMalformed_EmptyHypothesis()
    {
        CandidateIssue[] candidates = new[]
        {
            new CandidateIssue { candidate_id = "c0", location = "foo.cpp: X", hypothesis = "   ", trigger = "t", category = "correctness", verify_symbols = Array.Empty<string>() },
        };

        var (accepted, rejected) = CandidatePreFilter.Filter(candidates);
        Assert.AreEqual(0, accepted.Length);
        Assert.AreEqual(1, rejected.Length);
        StringAssert.Contains(rejected[0].Reason, "hypothesis");
    }

    [TestMethod]
    public void CandidatePreFilter_RemovesDuplicates()
    {
        CandidateIssue[] candidates = new[]
        {
            new CandidateIssue { candidate_id = "c0", location = "foo.cpp: Bar", hypothesis = "Null dereference", trigger = "t", category = "correctness", verify_symbols = Array.Empty<string>() },
            new CandidateIssue { candidate_id = "c1", location = "foo.cpp: Bar", hypothesis = "null dereference", trigger = "t2", category = "Correctness", verify_symbols = Array.Empty<string>() },
        };

        var (accepted, rejected) = CandidatePreFilter.Filter(candidates);
        Assert.AreEqual(1, accepted.Length, "Duplicate should be removed");
        Assert.AreEqual(1, rejected.Length);
        StringAssert.Contains(rejected[0].Reason, "duplicate");
    }

    [TestMethod]
    public void CandidatePreFilter_KeepsDistinctCandidates()
    {
        CandidateIssue[] candidates = new[]
        {
            new CandidateIssue { candidate_id = "c0", location = "foo.cpp: Bar", hypothesis = "Null deref", trigger = "t", category = "correctness", verify_symbols = Array.Empty<string>() },
            new CandidateIssue { candidate_id = "c1", location = "foo.cpp: Bar", hypothesis = "Buffer overflow", trigger = "t2", category = "memory safety", verify_symbols = Array.Empty<string>() },
        };

        var (accepted, rejected) = CandidatePreFilter.Filter(candidates);
        Assert.AreEqual(2, accepted.Length, "Distinct candidates should be kept");
        Assert.AreEqual(0, rejected.Length);
    }

    // -----------------------------------------------------------------------
    // CandidatePreFilter: category priority
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CandidatePreFilter_CategoryPriority_MemorySafetyFirst()
    {
        Assert.IsTrue(CandidatePreFilter.GetCategoryPriority("memory safety") <
                      CandidatePreFilter.GetCategoryPriority("performance"));
        Assert.IsTrue(CandidatePreFilter.GetCategoryPriority("correctness") <
                      CandidatePreFilter.GetCategoryPriority("maintainability"));
        Assert.IsTrue(CandidatePreFilter.GetCategoryPriority("concurrency") <
                      CandidatePreFilter.GetCategoryPriority("maintainability"));
    }

    // -----------------------------------------------------------------------
    // CandidateState: all states present
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CandidateState_HasAllExpectedStates()
    {
        string[] names = Enum.GetNames<CandidateState>();
        Assert.IsTrue(names.Contains("Discovered"));
        Assert.IsTrue(names.Contains("Filtered"));
        Assert.IsTrue(names.Contains("SkippedBudget"));
        Assert.IsTrue(names.Contains("NoContext"));
        Assert.IsTrue(names.Contains("VerificationFailed"));
        Assert.IsTrue(names.Contains("Rejected"));
        Assert.IsTrue(names.Contains("Verified"));
        Assert.IsTrue(names.Contains("Reported"));
    }

    // -----------------------------------------------------------------------
    // VerificationContextResolver: respects budget limits
    // -----------------------------------------------------------------------

    [TestMethod]
    public void VerificationContextResolver_RespectsMaxContextChars()
    {
        // Build a large source context
        string largeSource = string.Concat(Enumerable.Repeat("void x() { }\n", 3000)); // ~40K
        ReviewContext ctx = new ReviewContext
        {
            Path = "foo.cpp",
            Filename = "foo.cpp",
            Diff = "+changed",
            ChangedFile = largeSource,
            AstJson = BuildAstJsonWithChangedFunction(1, 100),
        };

        CandidateIssue candidate = new CandidateIssue
        {
            candidate_id = "c0",
            location = "foo.cpp: Foo::Open",
            category = "lifetime",
            hypothesis = "h",
            trigger = "t",
            verify_symbols = Array.Empty<string>(),
        };

        ReviewBudgetConfig budget = new ReviewBudgetConfig
        {
            VerificationMaxContextChars = 500, // very small limit
            VerificationMaxContextItems = 16,
        };

        VerificationContext result = new VerificationContextResolver().Resolve(candidate, new[] { ctx }, budget);

        int totalChars = result.Items.Sum(i => i.Source.Length);
        Assert.IsTrue(totalChars <= 500 || result.Truncated,
            "Context should stay within budget or be marked truncated");
    }

    [TestMethod]
    public void VerificationContextResolver_RespectsMaxContextItems()
    {
        ReviewBudgetConfig budget = new ReviewBudgetConfig
        {
            VerificationMaxContextItems = 2,
            VerificationMaxContextChars = 100_000,
        };

        ReviewContext ctx = new ReviewContext
        {
            Path = "foo.cpp",
            Filename = "foo.cpp",
            Diff = "+changed",
            ChangedFile = "void Foo::A(){} void Foo::B(){} void Foo::C(){}",
            AstJson = BuildAstJsonWithChangedFunction(1, 1),
        };

        CandidateIssue candidate = new CandidateIssue
        {
            candidate_id = "c0",
            location = "foo.cpp: Foo::A",
            category = "correctness",
            hypothesis = "h",
            trigger = "t",
            verify_symbols = new[] { "Foo::B", "Foo::C", "Foo::D" },
        };

        VerificationContext result = new VerificationContextResolver().Resolve(candidate, new[] { ctx }, budget);

        Assert.IsTrue(result.Items.Count <= 2, $"Should respect max items=2, got {result.Items.Count}");
    }

    // -----------------------------------------------------------------------
    // BuildTurn1WithMetrics: returns metrics with the prompt
    // -----------------------------------------------------------------------

    [TestMethod]
    public void PromptBuilder_BuildTurn1WithMetrics_ReturnsPromptAndMetrics()
    {
        ReviewRequest req = new ReviewRequest
        {
            ReviewRulesTurn1 = "TEMPLATE",
            MergeRequestTitle = "Test MR",
        };

        FileGroup group = new FileGroup { Topic = "Test" };
        group.ReviewContexts.Add(new ReviewContext
        {
            Path = "test.cpp",
            Filename = "test.cpp",
            Diff = "@@ -1,1 +1,1 @@\n-old\n+new",
            ChangedFile = "int main() { return 0; }",
        });

        (string prompt, Turn1ContextMetrics metrics) = PromptBuilder.BuildTurn1WithMetrics(
            req, group, new StringBuilder(), new ReviewBudgetConfig());

        Assert.IsTrue(prompt.Contains("TEMPLATE"), "Should include template");
        Assert.IsTrue(prompt.Contains("Test MR"), "Should include MR title");
        Assert.IsTrue(metrics.FileCount >= 1);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string BuildAstJson()
    {
        return """
            {
              "functions": [
                {
                  "qualified_name": "Foo::Open",
                  "signature": "void* Foo::Open()",
                  "start_line": 1,
                  "end_line": 5,
                  "change": "modified"
                }
              ]
            }
            """;
    }

    private static string BuildAstJsonWithChangedFunction(int start, int end)
    {
        return $$"""
            {
              "functions": [
                {
                  "qualified_name": "Foo::Open",
                  "signature": "void* Foo::Open()",
                  "start_line": {{start}},
                  "end_line": {{end}},
                  "change": "modified"
                }
              ]
            }
            """;
    }
}
