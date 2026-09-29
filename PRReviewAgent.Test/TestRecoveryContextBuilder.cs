using PRReviewAgent.Services;
using PRReviewAgent.Services.Coverage;
using PRReviewAgent.Services.Grouping;
using PRReviewAgent.Services.Recovery;

namespace PRReviewAgent.Test;

[TestClass]
public class TestRecoveryContextBuilder
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ReviewContext Ctx(string path, string diff = "", string? astJson = null) =>
        new ReviewContext
        {
            Path = path,
            Filename = System.IO.Path.GetFileName(path),
            Diff = diff,
            ChangedFile = string.Empty,
            AstJson = astJson,
            ExpandedDiff = diff,
        };

    private static string MakeDiff(string path, int newStart, params string[] plusLines)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- a/{path}");
        sb.AppendLine($"+++ b/{path}");
        sb.AppendLine($"@@ -{newStart},{plusLines.Length} +{newStart},{plusLines.Length} @@");
        foreach (string l in plusLines)
            sb.AppendLine($"+{l}");
        return sb.ToString();
    }

    // Diff with two hunks in the same file at different positions.
    private static string MakeDiffTwoHunks(string path,
        int h1Start, string[] h1Lines, int h2Start, string[] h2Lines)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- a/{path}");
        sb.AppendLine($"+++ b/{path}");
        sb.AppendLine($"@@ -{h1Start},{h1Lines.Length} +{h1Start},{h1Lines.Length} @@");
        foreach (string l in h1Lines) sb.AppendLine($"+{l}");
        sb.AppendLine($"@@ -{h2Start},{h2Lines.Length} +{h2Start},{h2Lines.Length} @@");
        foreach (string l in h2Lines) sb.AppendLine($"+{l}");
        return sb.ToString();
    }

    private static FileGroup Group(params ReviewContext[] ctxs)
    {
        var fg = new FileGroup { Topic = "test" };
        foreach (var ctx in ctxs) fg.ReviewContexts.Add(ctx);
        return fg;
    }

    private static ReviewCoverage BuildCoverage(
        IReadOnlyList<ChangedRegion> allRegions,
        IEnumerable<string> reportedIds)
    {
        var reported = reportedIds.ToHashSet(StringComparer.Ordinal);
        var mappings = allRegions
            .Where(r => reported.Contains(r.RegionId))
            .Select((r, i) => new CandidateMapping($"c{i}", $"{r.FilePath}:{r.StartLine}", new[] { r.RegionId }.ToList<string>()))
            .ToList();
        return new ReviewCoverage(allRegions, mappings);
    }

    // -------------------------------------------------------------------------
    // ParseDiffHunks unit tests
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ParseDiffHunks_SingleHunk_ParsedCorrectly()
    {
        string diff = MakeDiff("foo.cpp", 10, "int x = 1;", "int y = 2;");
        var hunks = RecoveryContextBuilder.ParseDiffHunks(diff);

        Assert.AreEqual(1, hunks.Count);
        Assert.AreEqual(10, hunks[0].NewStartLine);
        Assert.AreEqual(2, hunks[0].AddedLines.Count);
        Assert.IsTrue(hunks[0].HunkText.Contains("@@"));
    }

    [TestMethod]
    public void ParseDiffHunks_TwoHunks_BothParsed()
    {
        string diff = MakeDiffTwoHunks("foo.cpp",
            10, new[] { "int x = 1;" },
            50, new[] { "int y = 2;" });
        var hunks = RecoveryContextBuilder.ParseDiffHunks(diff);

        Assert.AreEqual(2, hunks.Count);
        Assert.AreEqual(10, hunks[0].NewStartLine);
        Assert.AreEqual(50, hunks[1].NewStartLine);
    }

    [TestMethod]
    public void ParseDiffHunks_EmptyDiff_ReturnsEmpty()
    {
        var hunks = RecoveryContextBuilder.ParseDiffHunks(string.Empty);
        Assert.AreEqual(0, hunks.Count);
    }

    // -------------------------------------------------------------------------
    // Case 10 — No Remaining Regions → Empty result
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case10_NoUnreportedRegions_ReturnsEmpty()
    {
        string diff = MakeDiff("foo.cpp", 10, "int x = 1;");
        var ctx = Ctx("foo.cpp", diff);
        var allRegions = ChangedRegionBuilder.Build(new[] { ctx });
        var coverage = BuildCoverage(allRegions, allRegions.Select(r => r.RegionId));

        RecoveryContextResult result = RecoveryContextBuilder.Build(coverage, Group(ctx));

        Assert.AreSame(RecoveryContextResult.Empty, result);
        Assert.AreEqual(0, result.Fragments.Count);
    }

    // -------------------------------------------------------------------------
    // Case 1 — Reported and unreported in different functions
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case1_ReportedAndUnreportedInDifferentFunctions_OnlyUnreportedInFragments()
    {
        // Two separate hunks: r0 at line 10 (reported), r1 at line 50 (unreported).
        string diff = MakeDiffTwoHunks("foo.cpp",
            10, new[] { "void Foo_A() {}" },
            50, new[] { "void Foo_B() {}" });
        var ctx = Ctx("foo.cpp", diff);
        var allRegions = ChangedRegionBuilder.Build(new[] { ctx });
        Assert.AreEqual(2, allRegions.Count, "should have two regions");

        // Report only the first region.
        var coverage = BuildCoverage(allRegions, new[] { allRegions[0].RegionId });
        Assert.AreEqual(1, coverage.UnreportedRegionIds.Count);

        RecoveryContextResult result = RecoveryContextBuilder.Build(coverage, Group(ctx));

        Assert.AreEqual(1, result.Fragments.Count, "only the unreported hunk should be a fragment");
        Assert.IsTrue(result.Fragments[0].DiffText.Contains("Foo_B"), "fragment should contain the unreported change");
        Assert.IsFalse(result.Fragments[0].DiffText.Contains("Foo_A"), "reported change should not be in the fragment");
    }

    // -------------------------------------------------------------------------
    // Case 5 — Nearby regions merge into one fragment
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case5_NearbyUnreportedRegions_MergedIntoOneFragment()
    {
        // Two hunks 30 lines apart — within MergeLineThreshold.
        string diff = MakeDiffTwoHunks("foo.cpp",
            100, new[] { "line A;" },
            110, new[] { "line B;" });
        var ctx = Ctx("foo.cpp", diff);
        var allRegions = ChangedRegionBuilder.Build(new[] { ctx });
        Assert.AreEqual(2, allRegions.Count);

        // Both unreported.
        var coverage = BuildCoverage(allRegions, Array.Empty<string>());
        Assert.AreEqual(2, coverage.UnreportedRegionIds.Count);

        RecoveryContextResult result = RecoveryContextBuilder.Build(coverage, Group(ctx));

        Assert.AreEqual(1, result.Fragments.Count, "nearby hunks should be merged");
        Assert.AreEqual(2, result.Fragments[0].TargetRegionIds.Count, "merged fragment covers both regions");
    }

    // -------------------------------------------------------------------------
    // Case 6 — Distant regions stay separate
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case6_DistantUnreportedRegions_KeptSeparate()
    {
        // Two hunks 900 lines apart — exceeds MergeLineThreshold.
        string diff = MakeDiffTwoHunks("foo.cpp",
            100, new[] { "line A;" },
            900, new[] { "line B;" });
        var ctx = Ctx("foo.cpp", diff);
        var allRegions = ChangedRegionBuilder.Build(new[] { ctx });
        Assert.AreEqual(2, allRegions.Count);

        var coverage = BuildCoverage(allRegions, Array.Empty<string>());

        RecoveryContextResult result = RecoveryContextBuilder.Build(coverage, Group(ctx));

        Assert.AreEqual(2, result.Fragments.Count, "distant hunks should remain separate fragments");
        Assert.AreEqual(1, result.Fragments[0].TargetRegionIds.Count);
        Assert.AreEqual(1, result.Fragments[1].TargetRegionIds.Count);
    }

    // -------------------------------------------------------------------------
    // Case 2 — Mixed reported/unreported in one function
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case2_MixedRegionsInOneFunction_OnlyUnreportedAreTargets()
    {
        // r0 at 679 (unreported), r1 at 691 (unreported), r2 at 721 (reported).
        string diff = new System.Text.StringBuilder()
            .AppendLine("--- a/mesh.cpp")
            .AppendLine("+++ b/mesh.cpp")
            .AppendLine("@@ -679,1 +679,1 @@")
            .AppendLine("+roughness = uv.x;")
            .AppendLine("@@ -691,1 +691,1 @@")
            .AppendLine("+bitangent = cross(n, t);")
            .AppendLine("@@ -721,1 +721,1 @@")
            .AppendLine("+env_light = sample();")
            .ToString();
        var ctx = Ctx("mesh.cpp", diff);
        var allRegions = ChangedRegionBuilder.Build(new[] { ctx });
        Assert.AreEqual(3, allRegions.Count);

        // Only r2 (line 721) is reported.
        var coverage = BuildCoverage(allRegions, new[] { allRegions[2].RegionId });
        Assert.AreEqual(2, coverage.UnreportedRegionIds.Count);

        RecoveryContextResult result = RecoveryContextBuilder.Build(coverage, Group(ctx));

        Assert.AreEqual(2, result.TargetRegionCount, "only r0 and r1 are targets");
        // r0 (679) and r1 (691) are 12 lines apart — merged.
        Assert.AreEqual(1, result.Fragments.Count, "r0 and r1 should be merged (≤100 line gap)");
        Assert.IsFalse(result.Fragments[0].DiffText.Contains("env_light"), "reported hunk should not appear");
    }

    // -------------------------------------------------------------------------
    // Case 9 — No containing symbol → region still included
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case9_NoContainingSymbol_RegionStillIncluded()
    {
        string diff = MakeDiff("util.cpp", 5, "int flag = 0;");
        var ctx = Ctx("util.cpp", diff);
        var allRegions = ChangedRegionBuilder.Build(new[] { ctx });
        // Regions from a plain diff have no containing symbol (no AST).
        Assert.IsNull(allRegions[0].ContainingSymbol, "no symbol without AST");

        var coverage = BuildCoverage(allRegions, Array.Empty<string>());

        RecoveryContextResult result = RecoveryContextBuilder.Build(coverage, Group(ctx));

        Assert.AreEqual(1, result.Fragments.Count, "region without symbol should still produce a fragment");
        Assert.IsNull(result.Fragments[0].ContainingSymbol);
    }

    // -------------------------------------------------------------------------
    // Case 11 — Determinism
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case11_Determinism_IdenticalInputProducesIdenticalOutput()
    {
        string diff1 = MakeDiff("alpha.cpp", 10, "int a = 1;");
        string diff2 = MakeDiff("beta.cpp", 20, "int b = 2;");
        var ctxA = Ctx("alpha.cpp", diff1);
        var ctxB = Ctx("beta.cpp", diff2);
        var group = Group(ctxA, ctxB);
        var allRegions = ChangedRegionBuilder.Build(new[] { ctxA, ctxB });
        var coverage = BuildCoverage(allRegions, Array.Empty<string>());

        RecoveryContextResult r1 = RecoveryContextBuilder.Build(coverage, group);
        RecoveryContextResult r2 = RecoveryContextBuilder.Build(coverage, group);

        Assert.AreEqual(r1.Fragments.Count, r2.Fragments.Count);
        for (int i = 0; i < r1.Fragments.Count; i++)
        {
            Assert.AreEqual(r1.Fragments[i].FilePath, r2.Fragments[i].FilePath);
            Assert.AreEqual(r1.Fragments[i].StartLine, r2.Fragments[i].StartLine);
            Assert.AreEqual(r1.Fragments[i].DiffText, r2.Fragments[i].DiffText);
        }
    }

    // -------------------------------------------------------------------------
    // Case 8 — Budget pressure trims later fragments, preserves earlier ones
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case8_BudgetExceeded_EarlierFragmentsPreserved()
    {
        // Create many large hunks to exceed budget.
        string bigLine = new string('x', 1600); // ~400 tokens each
        string diff = new System.Text.StringBuilder()
            .AppendLine("--- a/big.cpp")
            .AppendLine("+++ b/big.cpp")
            .AppendLine("@@ -10,1 +10,1 @@")
            .AppendLine($"+{bigLine}")
            .AppendLine("@@ -200,1 +200,1 @@")
            .AppendLine($"+{bigLine}")
            .AppendLine("@@ -400,1 +400,1 @@")
            .AppendLine($"+{bigLine}")
            .AppendLine("@@ -600,1 +600,1 @@")
            .AppendLine($"+{bigLine}")
            .AppendLine("@@ -800,1 +800,1 @@")
            .AppendLine($"+{bigLine}")
            .AppendLine("@@ -1000,1 +1000,1 @@")
            .AppendLine($"+{bigLine}")
            .ToString();

        var ctx = Ctx("big.cpp", diff);
        var allRegions = ChangedRegionBuilder.Build(new[] { ctx });
        Assert.IsTrue(allRegions.Count >= 5, "should have multiple regions");

        var coverage = BuildCoverage(allRegions, Array.Empty<string>());

        // Very small budget forces trimming.
        RecoveryContextResult result = RecoveryContextBuilder.Build(coverage, Group(ctx), budgetTokens: 800);

        Assert.IsTrue(result.Fragments.Count >= 1, "at least one fragment preserved");
        Assert.IsTrue(result.Fragments.Count < allRegions.Count, "some fragments trimmed");
        // First fragment should be from the earliest hunk (line 10).
        Assert.AreEqual(10, result.Fragments[0].StartLine, "first (earliest) fragment preserved");
    }

    // -------------------------------------------------------------------------
    // Multi-file: fragments from correct files only
    // -------------------------------------------------------------------------

    [TestMethod]
    public void MultiFile_OnlyFilesWithUnreportedRegions_Included()
    {
        string diff1 = MakeDiff("reported.cpp", 10, "int x = 1;");
        string diff2 = MakeDiff("unreported.cpp", 20, "int y = 2;");
        var ctxR = Ctx("reported.cpp", diff1);
        var ctxU = Ctx("unreported.cpp", diff2);
        var allRegions = ChangedRegionBuilder.Build(new[] { ctxR, ctxU });

        // Sort: reported.cpp comes first alphabetically, then unreported.cpp.
        var reportedRegions = allRegions.Where(r => r.FilePath.Contains("reported") && !r.FilePath.Contains("un")).ToList();
        var unreportedRegions = allRegions.Where(r => r.FilePath.Contains("unreported")).ToList();

        var coverage = BuildCoverage(allRegions, reportedRegions.Select(r => r.RegionId));
        Assert.AreEqual(1, coverage.UnreportedRegionIds.Count);

        RecoveryContextResult result = RecoveryContextBuilder.Build(coverage, Group(ctxR, ctxU));

        Assert.AreEqual(1, result.Fragments.Count, "only unreported.cpp should produce a fragment");
        Assert.IsTrue(result.Fragments[0].FilePath.Contains("unreported"), "fragment should be from unreported.cpp");
    }

    // -------------------------------------------------------------------------
    // ExcludedReportedRegionCount
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ExcludedCount_ReflectsNumberOfReportedRegions()
    {
        string diff = MakeDiffTwoHunks("foo.cpp",
            10, new[] { "reported line;" },
            50, new[] { "unreported line;" });
        var ctx = Ctx("foo.cpp", diff);
        var allRegions = ChangedRegionBuilder.Build(new[] { ctx });

        var coverage = BuildCoverage(allRegions, new[] { allRegions[0].RegionId });

        RecoveryContextResult result = RecoveryContextBuilder.Build(coverage, Group(ctx));

        Assert.AreEqual(1, result.ExcludedReportedRegionCount);
        Assert.AreEqual(1, result.TargetRegionCount);
    }

    // -------------------------------------------------------------------------
    // Missing ExpandedDiff → no crash, empty result for that file
    // -------------------------------------------------------------------------

    [TestMethod]
    public void MissingExpandedDiff_NoFragmentForThatFile()
    {
        var ctxNoExpanded = new ReviewContext
        {
            Path = "nodiff.cpp",
            Filename = "nodiff.cpp",
            Diff = string.Empty,
            ChangedFile = string.Empty,
            ExpandedDiff = string.Empty,
        };

        string diff = MakeDiff("other.cpp", 10, "int x = 1;");
        var ctxOther = Ctx("other.cpp", diff);
        var allRegions = ChangedRegionBuilder.Build(new[] { ctxOther });

        // Manually add a region for nodiff.cpp with no diff.
        var fakeRegion = new ChangedRegion("r99", "nodiff.cpp", 5, 5, new[] { 5 }.ToList<int>(), null);
        var allWithFake = allRegions.Concat(new[] { fakeRegion }).ToList();
        var coverage = BuildCoverage(allWithFake, Array.Empty<string>());

        RecoveryContextResult result = RecoveryContextBuilder.Build(
            coverage, Group(ctxNoExpanded, ctxOther));

        // Should get a fragment for other.cpp but not for nodiff.cpp.
        Assert.IsTrue(result.Fragments.All(f => !f.FilePath.Contains("nodiff")),
            "file with no ExpandedDiff should produce no fragments");
    }
}
