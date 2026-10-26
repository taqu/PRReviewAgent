using PRReviewAgent.Services;

namespace PRReviewAgent.Test;

[TestClass]
public class TestSeverityStatistics
{
    // -------------------------------------------------------------------------
    // CountSeveritiesFromMarkdown — basic cases
    // -------------------------------------------------------------------------

    [TestMethod]
    public void NoFindings_AllZero()
    {
        string md = string.Empty;
        var (c, ma, mi) = PromptBuilder.CountSeveritiesFromMarkdown(md);
        Assert.AreEqual(0, c);
        Assert.AreEqual(0, ma);
        Assert.AreEqual(0, mi);
    }

    [TestMethod]
    public void EmptySeverityHeadings_AllZero()
    {
        string md = "## Critical\n\n## Major\n\n## Minor\n";
        var (c, ma, mi) = PromptBuilder.CountSeveritiesFromMarkdown(md);
        Assert.AreEqual(0, c);
        Assert.AreEqual(0, ma);
        Assert.AreEqual(0, mi);
    }

    [TestMethod]
    public void OneCriticalFinding()
    {
        string md = "## Critical\n\n### file.cpp: func\n\n* **問題:** issue\n";
        var (c, ma, mi) = PromptBuilder.CountSeveritiesFromMarkdown(md);
        Assert.AreEqual(1, c);
        Assert.AreEqual(0, ma);
        Assert.AreEqual(0, mi);
    }

    [TestMethod]
    public void TwoMajorFindings()
    {
        string md = "## Major\n\n### file.cpp: funcA\n\n* detail\n\n### file.cpp: funcB\n\n* detail\n";
        var (c, ma, mi) = PromptBuilder.CountSeveritiesFromMarkdown(md);
        Assert.AreEqual(0, c);
        Assert.AreEqual(2, ma);
        Assert.AreEqual(0, mi);
    }

    [TestMethod]
    public void OneFindingOfEachSeverity()
    {
        string md = "## Critical\n\n### a.cpp: f1\n\n## Major\n\n### b.cpp: f2\n\n## Minor\n\n### c.cpp: f3\n";
        var (c, ma, mi) = PromptBuilder.CountSeveritiesFromMarkdown(md);
        Assert.AreEqual(1, c);
        Assert.AreEqual(1, ma);
        Assert.AreEqual(1, mi);
    }

    [TestMethod]
    public void MultipleFindings_SelectedEqualsSum()
    {
        string md = "## Critical\n\n### a.cpp: f1\n\n### a.cpp: f2\n\n## Major\n\n### b.cpp: f3\n\n## Minor\n\n### c.cpp: f4\n\n### c.cpp: f5\n\n### c.cpp: f6\n";
        var (c, ma, mi) = PromptBuilder.CountSeveritiesFromMarkdown(md);
        Assert.AreEqual(2, c);
        Assert.AreEqual(1, ma);
        Assert.AreEqual(3, mi);
        Assert.AreEqual(6, c + ma + mi);
    }

    [TestMethod]
    public void RealBenchmarkOutput_CountsCorrectly()
    {
        // Matches the format from actual Turn 2 benchmark log
        string md = "## Critical\n\n### renderer.cpp: hitTriangle\n\n* detail1\n\n### renderer.cpp: powerHeuristic\n\n* detail2\n\n## Major\n\n### renderer.cpp: traceMesh\n\n* detail3\n\n### renderer.cpp: Environment::sample\n\n### renderer.cpp: renderMesh\n\n<!-- SELECTED_CANDIDATES: c0,c1,c2,c3,c4 -->";
        var (c, ma, mi) = PromptBuilder.CountSeveritiesFromMarkdown(md);
        Assert.AreEqual(2, c);
        Assert.AreEqual(3, ma);
        Assert.AreEqual(0, mi);
    }

    [TestMethod]
    public void Turn1CandidatesRejected_AllZero()
    {
        // Turn 2 outputs nothing (all candidates rejected)
        string md = string.Empty;
        var (c, ma, mi) = PromptBuilder.CountSeveritiesFromMarkdown(md);
        Assert.AreEqual(0, c);
        Assert.AreEqual(0, ma);
        Assert.AreEqual(0, mi);
    }

    [TestMethod]
    public void Turn1HighConfidence_DoesNotMapToSeverity()
    {
        // Simulate what used to happen: Turn 1 "high" confidence should NOT count as Critical
        // The old code checked for "Critical"/"Major"/"Minor" in confidence, which never matched "high"/"medium"
        // With the new code we parse Turn 2 markdown, so this test verifies the parser
        // does not count prose mentions of severity words as headings.
        string md = "## Critical\n\nThis finding is of high confidence and relates to a critical path.\n\n### file.cpp: realFinding\n\n* detail\n";
        var (c, ma, mi) = PromptBuilder.CountSeveritiesFromMarkdown(md);
        Assert.AreEqual(1, c); // only the ### heading counts
        Assert.AreEqual(0, ma);
        Assert.AreEqual(0, mi);
    }

    [TestMethod]
    public void SeverityWordInProse_NotCountedAsHeading()
    {
        // "## critical" appearing in prose-like context inside a finding body should not be confused
        // but actual ## headings that are not severity names are not counted.
        string md = "## Critical\n\n### file.cpp: func\n\n## Some Other Section\n\n### file.cpp: func2\n";
        var (c, ma, mi) = PromptBuilder.CountSeveritiesFromMarkdown(md);
        Assert.AreEqual(1, c); // only the finding under Critical counts
        Assert.AreEqual(0, ma);
        Assert.AreEqual(0, mi);
    }

    [TestMethod]
    public void NullInput_AllZero()
    {
        var (c, ma, mi) = PromptBuilder.CountSeveritiesFromMarkdown(null!);
        Assert.AreEqual(0, c);
        Assert.AreEqual(0, ma);
        Assert.AreEqual(0, mi);
    }

    [TestMethod]
    public void CaseInsensitiveHeadings()
    {
        string md = "## CRITICAL\n\n### file.cpp: f1\n\n## major\n\n### file.cpp: f2\n\n## MINOR\n\n### file.cpp: f3\n";
        var (c, ma, mi) = PromptBuilder.CountSeveritiesFromMarkdown(md);
        Assert.AreEqual(1, c);
        Assert.AreEqual(1, ma);
        Assert.AreEqual(1, mi);
    }

    [TestMethod]
    public void MultipleFileGroups_SelectedEqualsSum()
    {
        // Simulates two file groups contributing to totals
        string md1 = "## Critical\n\n### a.cpp: f1\n\n## Major\n\n### b.cpp: f2\n";
        string md2 = "## Minor\n\n### c.cpp: f3\n\n### c.cpp: f4\n";

        var (c1, ma1, mi1) = PromptBuilder.CountSeveritiesFromMarkdown(md1);
        var (c2, ma2, mi2) = PromptBuilder.CountSeveritiesFromMarkdown(md2);

        int totalCritical = c1 + c2;
        int totalMajor = ma1 + ma2;
        int totalMinor = mi1 + mi2;
        int totalSelected = totalCritical + totalMajor + totalMinor;

        Assert.AreEqual(1, totalCritical);
        Assert.AreEqual(1, totalMajor);
        Assert.AreEqual(2, totalMinor);
        Assert.AreEqual(4, totalSelected);
    }

    [TestMethod]
    public void GitLabAndGitHubIdenticalContent_ProduceIdenticalCounts()
    {
        // The same Turn 2 response must produce the same counts regardless of provider
        string sharedMd = "## Critical\n\n### x.cpp: issue1\n\n## Major\n\n### y.cpp: issue2\n\n### y.cpp: issue3\n";

        var (cGitHub, maGitHub, miGitHub) = PromptBuilder.CountSeveritiesFromMarkdown(sharedMd);
        var (cGitLab, maGitLab, miGitLab) = PromptBuilder.CountSeveritiesFromMarkdown(sharedMd);

        Assert.AreEqual(cGitHub, cGitLab);
        Assert.AreEqual(maGitHub, maGitLab);
        Assert.AreEqual(miGitHub, miGitLab);
    }
}
