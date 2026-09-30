using PRReviewAgent.Prompt;
using PRReviewAgent.Services;
using PRReviewAgent.Services.Coverage;
using PRReviewAgent.Services.Grouping;
using PRReviewAgent.Services.Recovery;
using System.Text;

namespace PRReviewAgent.Test;

[TestClass]
public class TestRecoveryDetection
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ReviewContext Ctx(string path, string diff = "", string? astJson = null) =>
        new ReviewContext
        {
            Path = path,
            Filename = Path.GetFileName(path),
            Diff = diff,
            ChangedFile = string.Empty,
            AstJson = astJson,
            ExpandedDiff = diff,
        };

    private static string MakeDiff(string path, int newStart, params string[] plusLines)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"--- a/{path}");
        sb.AppendLine($"+++ b/{path}");
        sb.AppendLine($"@@ -{newStart},{plusLines.Length} +{newStart},{plusLines.Length} @@");
        foreach (string l in plusLines)
            sb.AppendLine($"+{l}");
        return sb.ToString();
    }

    private static IssuesResponse Issues(params (string location, string problem)[] items)
    {
        var issues = items.Select((x, i) => new Issue
        {
            candidate_id = $"c{i}",
            location = x.location,
            problem = x.problem,
            confidence = "High",
        }).ToArray();
        return new IssuesResponse { issues = issues };
    }

    private static FileGroup Group(params ReviewContext[] ctxs)
    {
        var fg = new FileGroup { Topic = "test_topic" };
        foreach (var ctx in ctxs) fg.ReviewContexts.Add(ctx);
        return fg;
    }

    private static ReviewRequest MakeRequest(string? recoveryTemplate = "## Recovery\nFind remaining issues.") =>
        new ReviewRequest
        {
            ReviewRulesTurn1 = "## Rules",
            ReviewRulesTurn1Recovery = recoveryTemplate,
            ReviewRulesTurn2 = "## Selection",
            MergeRequestTitle = "Test MR",
        };

    // -------------------------------------------------------------------------
    // Case 1: No unreported regions → recovery should be skipped
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case1_NoUnreportedRegions_RecoverySkipped()
    {
        // Build a coverage where every region is reported.
        string diff = MakeDiff("foo.cpp", 10, "int x = 1;");
        var ctx = Ctx("foo.cpp", diff);
        var regions = ChangedRegionBuilder.Build(new[] { ctx });
        Assert.IsTrue(regions.Count > 0, "should have at least one region");

        // Map all regions to a candidate.
        var mappings = regions
            .Select((r, i) => new CandidateMapping($"c{i}", $"foo.cpp:{r.StartLine}", new[] { r.RegionId }.ToList<string>()))
            .ToList();
        var coverage = new ReviewCoverage(regions, mappings);

        Assert.AreEqual(0, coverage.UnreportedRegionIds.Count);

        // The skip condition: no unreported regions → skip.
        bool recoverySkipped = coverage.UnreportedRegionIds.Count == 0
            || string.IsNullOrEmpty(MakeRequest().ReviewRulesTurn1Recovery);
        Assert.IsTrue(recoverySkipped);
    }

    // -------------------------------------------------------------------------
    // Case 2: Remaining regions appear correctly in BuildTurn1Recovery
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case2_BuildTurn1Recovery_ListsUnreportedRegions()
    {
        string diff1 = MakeDiff("alpha.cpp", 10, "int x = 1;");
        string diff2 = MakeDiff("beta.cpp", 20, "int y = 2;");
        var ctxAlpha = Ctx("alpha.cpp", diff1);
        var ctxBeta = Ctx("beta.cpp", diff2);
        var group = Group(ctxAlpha, ctxBeta);
        var request = MakeRequest();

        var regions = ChangedRegionBuilder.Build(new[] { ctxAlpha, ctxBeta });
        // Only map alpha's region → beta remains unreported.
        var alphaRegions = regions.Where(r => r.FilePath.Contains("alpha")).ToList();
        var mappings = alphaRegions.Select((r, i) =>
            new CandidateMapping($"c{i}", $"alpha.cpp:{r.StartLine}", new[] { r.RegionId }.ToList<string>()))
            .ToList();
        var coverage = new ReviewCoverage(regions, mappings);

        Assert.IsTrue(coverage.UnreportedRegionIds.Count > 0, "beta should be unreported");

        var primaryCandidates = Issues(("alpha.cpp:10 DoSomething", "null deref"));

        RecoveryContextResult recoveryContext = RecoveryContextBuilder.Build(coverage, group);
        string prompt = PromptBuilder.BuildTurn1Recovery(
            request, group, coverage, primaryCandidates, recoveryContext, new StringBuilder());

        Assert.IsTrue(prompt.Contains("## Recovery"), "template should be included");
        Assert.IsTrue(prompt.Contains("# Already Reported Findings"), "should list reported findings");
        Assert.IsTrue(prompt.Contains("alpha.cpp:10"), "reported finding location should appear");
        Assert.IsTrue(prompt.Contains("# Remaining Changed Regions"), "should list unreported regions");
        Assert.IsTrue(prompt.Contains("beta.cpp"), "beta.cpp should be in remaining regions");
    }

    // -------------------------------------------------------------------------
    // Case 3: Recovery adds new candidate → combined has both
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case3_RecoveryAddsNew_CombinedHasBoth()
    {
        var primary = Issues(("foo.cpp:10 Bar", "null deref"));
        var recovery = Issues(("baz.cpp:50 Qux", "buffer overflow"));

        string diff1 = MakeDiff("baz.cpp", 50, "char buf[8];");
        string diff2 = MakeDiff("foo.cpp", 10, "int x = 1;");
        // Build together for unique region IDs.
        var allRegions = ChangedRegionBuilder.Build(new[] { Ctx("baz.cpp", diff1), Ctx("foo.cpp", diff2) });
        var fooRegions = allRegions.Where(r => r.FilePath.Contains("foo")).ToList();
        var bazRegions = allRegions.Where(r => r.FilePath.Contains("baz")).ToList();

        var primaryMappings = fooRegions.Select(r =>
            new CandidateMapping("c0", $"foo.cpp:{r.StartLine}", new[] { r.RegionId }.ToList<string>())).ToList();
        var primaryCoverage = new ReviewCoverage(allRegions, primaryMappings);

        var recoveryMappings = bazRegions.Select(r =>
            new CandidateMapping("c1", $"baz.cpp:{r.StartLine}", new[] { r.RegionId }.ToList<string>())).ToList();
        var recoveryCoverage = new ReviewCoverage(allRegions, recoveryMappings);

        (IssuesResponse combined, int dups) = CandidateUnionBuilder.Union(primary, recovery, primaryCoverage, recoveryCoverage);

        Assert.AreEqual(0, dups);
        Assert.AreEqual(2, combined.issues.Length);
        Assert.IsTrue(combined.issues.Any(i => i.location.Contains("foo.cpp")));
        Assert.IsTrue(combined.issues.Any(i => i.location.Contains("baz.cpp")));
    }

    // -------------------------------------------------------------------------
    // Case 4: Recovery repeats primary location → dedup removes it
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case4_RecoveryDuplicateLocation_Removed()
    {
        var primary = Issues(("foo.cpp:10 Bar", "null deref"));
        // Same location as primary.
        var recovery = new IssuesResponse
        {
            issues = new[]
            {
                new Issue { candidate_id = "c1", location = "foo.cpp:10 Bar", problem = "same issue again", confidence = "High" }
            }
        };

        var primaryCoverage = ReviewCoverage.Empty;
        var recoveryCoverage = ReviewCoverage.Empty;

        (IssuesResponse combined, int dups) = CandidateUnionBuilder.Union(primary, recovery, primaryCoverage, recoveryCoverage);

        Assert.AreEqual(1, dups);
        Assert.AreEqual(1, combined.issues.Length);
        Assert.AreEqual("foo.cpp:10 Bar", combined.issues[0].location);
    }

    // -------------------------------------------------------------------------
    // Case 5: Same function, different regions → both preserved
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case5_SameFunctionDifferentRegions_BothPreserved()
    {
        // Different locations even if same function → not deduped.
        var primary = Issues(("foo.cpp:10 Render", "null deref"));
        var recovery = Issues(("foo.cpp:25 Render", "out of bounds"));

        (IssuesResponse combined, int dups) = CandidateUnionBuilder.Union(
            primary, recovery, ReviewCoverage.Empty, ReviewCoverage.Empty);

        Assert.AreEqual(0, dups);
        Assert.AreEqual(2, combined.issues.Length);
    }

    // -------------------------------------------------------------------------
    // Case 6: Recovery returns zero issues → primary unchanged for Turn 2
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case6_RecoveryReturnsZero_PrimaryUnchanged()
    {
        var primary = Issues(("foo.cpp:10 Bar", "null deref"), ("baz.cpp:20 Qux", "leak"));
        var emptyRecovery = new IssuesResponse { issues = Array.Empty<Issue>() };

        (IssuesResponse combined, int dups) = CandidateUnionBuilder.Union(
            primary, emptyRecovery, ReviewCoverage.Empty, ReviewCoverage.Empty);

        Assert.AreEqual(0, dups);
        Assert.AreEqual(2, combined.issues.Length);
        Assert.AreSame(primary, combined, "should return primary unchanged when recovery is empty");
    }

    // -------------------------------------------------------------------------
    // Case 7: Recovery null (simulates failure) → primary preserved
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case7_RecoveryNull_PrimaryPreserved()
    {
        var primary = Issues(("foo.cpp:10 Bar", "null deref"));

        (IssuesResponse combined, int dups) = CandidateUnionBuilder.Union(
            primary, null, ReviewCoverage.Empty, ReviewCoverage.Empty);

        Assert.AreEqual(0, dups);
        Assert.AreSame(primary, combined);
    }

    // -------------------------------------------------------------------------
    // Case 8: Coverage after recovery shows newly-reported regions
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case8_CoverageAfterRecovery_ShowsNewlyReported()
    {
        string diff1 = MakeDiff("foo.cpp", 10, "int x = 1;");
        string diff2 = MakeDiff("bar.cpp", 20, "int y = 2;");
        // Build all regions together so IDs are unique (r0, r1, ...) across files.
        var allRegions = ChangedRegionBuilder.Build(new[]
        {
            Ctx("bar.cpp", diff2),  // sorted first alphabetically
            Ctx("foo.cpp", diff1),
        });
        var fooRegions = allRegions.Where(r => r.FilePath.Contains("foo")).ToList();
        var barRegions = allRegions.Where(r => r.FilePath.Contains("bar")).ToList();

        // Primary only reports foo.
        var primaryMappings = fooRegions.Select(r =>
            new CandidateMapping("c0", $"foo.cpp:{r.StartLine}", new[] { r.RegionId }.ToList<string>()))
            .ToList();
        var primaryCoverage = new ReviewCoverage(allRegions, primaryMappings);

        Assert.AreEqual(1, primaryCoverage.UnreportedRegionIds.Count, "bar should be unreported after primary");

        // Recovery reports bar.
        var recoveryMappings = barRegions.Select(r =>
            new CandidateMapping("c1", $"bar.cpp:{r.StartLine}", new[] { r.RegionId }.ToList<string>()))
            .ToList();
        var recoveryCoverage = new ReviewCoverage(allRegions, recoveryMappings);

        int newlyReported = recoveryCoverage.ReportedRegionIds
            .Count(rid => !primaryCoverage.ReportedRegionIds.Contains(rid));
        Assert.AreEqual(1, newlyReported, "one newly-reported region after recovery");

        ReviewCoverage final = ReviewCoverage.Merge(primaryCoverage, recoveryCoverage);
        Assert.AreEqual(0, final.UnreportedRegionIds.Count, "all regions reported after merge");
    }

    // -------------------------------------------------------------------------
    // Case 9: Combined count above 10 is supported
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case9_CombinedCountAbove10_Supported()
    {
        var primaryIssues = Enumerable.Range(0, 8).Select(i =>
            new Issue { candidate_id = $"c{i}", location = $"file{i}.cpp:10", problem = "p", confidence = "High" })
            .ToArray();
        var primary = new IssuesResponse { issues = primaryIssues };

        var recoveryIssues = Enumerable.Range(8, 5).Select(i =>
            new Issue { candidate_id = $"c{i}", location = $"file{i}.cpp:10", problem = "p", confidence = "High" })
            .ToArray();
        var recovery = new IssuesResponse { issues = recoveryIssues };

        (IssuesResponse combined, int dups) = CandidateUnionBuilder.Union(
            primary, recovery, ReviewCoverage.Empty, ReviewCoverage.Empty);

        Assert.AreEqual(0, dups);
        Assert.AreEqual(13, combined.issues.Length, "all 13 candidates preserved");
        // IDs should be sequential 0..12.
        for (int i = 0; i < combined.issues.Length; i++)
            Assert.AreEqual($"c{i}", combined.issues[i].candidate_id);
    }

    // -------------------------------------------------------------------------
    // NormalizeLocation edge cases
    // -------------------------------------------------------------------------

    [TestMethod]
    public void NormalizeLocation_BackslashAndWhitespace_Normalized()
    {
        string loc = @"  foo\bar.cpp:10  ";
        string normalized = CandidateUnionBuilder.NormalizeLocation(loc);
        Assert.AreEqual("foo/bar.cpp:10", normalized);
    }

    [TestMethod]
    public void NormalizeLocation_Null_ReturnsEmpty()
    {
        string normalized = CandidateUnionBuilder.NormalizeLocation(null!);
        Assert.AreEqual(string.Empty, normalized);
    }

    // -------------------------------------------------------------------------
    // BuildTurn1Recovery: no reported findings → section omitted
    // -------------------------------------------------------------------------

    [TestMethod]
    public void BuildTurn1Recovery_NoPrimaryFindings_SectionOmitted()
    {
        string diff = MakeDiff("foo.cpp", 10, "int x = 1;");
        var ctx = Ctx("foo.cpp", diff);
        var group = Group(ctx);
        var request = MakeRequest();

        var regions = ChangedRegionBuilder.Build(new[] { ctx });
        var coverage = new ReviewCoverage(regions, new List<CandidateMapping>());
        var emptyPrimary = new IssuesResponse { issues = Array.Empty<Issue>() };

        RecoveryContextResult recoveryContext2 = RecoveryContextBuilder.Build(coverage, group);
        string prompt = PromptBuilder.BuildTurn1Recovery(
            request, group, coverage, emptyPrimary, recoveryContext2, new StringBuilder());

        Assert.IsFalse(prompt.Contains("# Already Reported Findings"), "section should be omitted when no findings");
    }
}
