using PRReviewAgent.Prompt.Turn1;
using PRReviewAgent.Services;
using System.Text;

namespace PRReviewAgent.Test;

[TestClass]
public class TestTurn1Context
{
    // -----------------------------------------------------------------------
    // Turn1SourceFormatter.ParseModifiedLines
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ParseModifiedLines_SingleHunk()
    {
        string diff = "@@ -1,3 +1,4 @@\n context\n+added line\n context\n-removed\n context\n";
        HashSet<int> lines = Turn1SourceFormatter.ParseModifiedLines(diff);
        // "+added line" is at new-file line 2 (after " context" at line 1)
        Assert.IsTrue(lines.Contains(2), "Expected line 2 to be in modified lines");
        // removed line contributes no new-file number
        Assert.IsFalse(lines.Contains(4), "Removed line should not appear in new-file lines");
    }

    // -----------------------------------------------------------------------
    // ParseChangedRanges: consecutive lines → one range
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ParseChangedRanges_ConsecutiveLines()
    {
        // Lines 3, 4, 5 all added consecutively
        string diff = "@@ -1,2 +1,5 @@\n context\n context\n+line3\n+line4\n+line5\n";
        List<(int Start, int End)> ranges = Turn1SourceFormatter.ParseChangedRanges(diff);
        Assert.AreEqual(1, ranges.Count, "Consecutive lines should form one range");
        Assert.AreEqual(3, ranges[0].Start);
        Assert.AreEqual(5, ranges[0].End);
    }

    // -----------------------------------------------------------------------
    // ParseChangedRanges: discontiguous lines → separate ranges
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ParseChangedRanges_DiscontiguousLines()
    {
        // Lines 1 and 3 are changed, line 2 is context
        string diff = "@@ -1,3 +1,3 @@\n+line1\n context\n+line3\n";
        List<(int Start, int End)> ranges = Turn1SourceFormatter.ParseChangedRanges(diff);
        Assert.AreEqual(2, ranges.Count, "Discontiguous lines should form two ranges");
        Assert.AreEqual(1, ranges[0].Start);
        Assert.AreEqual(1, ranges[0].End);
        Assert.AreEqual(3, ranges[1].Start);
        Assert.AreEqual(3, ranges[1].End);
    }

    // -----------------------------------------------------------------------
    // AnnotateSource: marks changed region
    // -----------------------------------------------------------------------

    [TestMethod]
    public void AnnotateSource_MarksChangedRegion()
    {
        string source = "line1\nline2\nline3\n";
        List<(int Start, int End)> ranges = new List<(int Start, int End)> { (2, 2) };
        string result = Turn1SourceFormatter.AnnotateSource(source, ranges);
        Assert.IsTrue(result.Contains(Turn1SourceFormatter.ChangeBeginMarker), "Should contain begin marker");
        Assert.IsTrue(result.Contains(Turn1SourceFormatter.ChangeEndMarker), "Should contain end marker");
        Assert.IsTrue(result.Contains("line2"), "Should contain the changed line");
        // line1 and line3 should be outside the markers
        int beginIdx = result.IndexOf(Turn1SourceFormatter.ChangeBeginMarker);
        int endIdx = result.IndexOf(Turn1SourceFormatter.ChangeEndMarker);
        int line1Idx = result.IndexOf("line1");
        int line3Idx = result.IndexOf("line3");
        Assert.IsTrue(line1Idx < beginIdx, "line1 should appear before the begin marker");
        Assert.IsTrue(line3Idx > endIdx, "line3 should appear after the end marker");
    }

    // -----------------------------------------------------------------------
    // AnnotateSource: no changes → returns source unchanged
    // -----------------------------------------------------------------------

    [TestMethod]
    public void AnnotateSource_NoChanges_ReturnsSameSource()
    {
        string source = "line1\nline2\nline3\n";
        List<(int Start, int End)> ranges = new List<(int Start, int End)>();
        string result = Turn1SourceFormatter.AnnotateSource(source, ranges);
        Assert.AreEqual(source, result, "Empty ranges should return source unchanged");
    }

    // -----------------------------------------------------------------------
    // FormatDiffSummary: shows - and + lines, omits context
    // -----------------------------------------------------------------------

    [TestMethod]
    public void FormatDiffSummary_ShowsRemovedAndAdded()
    {
        string diff = "@@ -1,3 +1,3 @@ void Foo()\n context\n-old line\n+new line\n context\n";
        string summary = Turn1SourceFormatter.FormatDiffSummary(diff);
        Assert.IsFalse(string.IsNullOrEmpty(summary), "Summary should not be empty");
        Assert.IsTrue(summary.Contains("-old line"), "Should include removed line");
        Assert.IsTrue(summary.Contains("+new line"), "Should include added line");
        Assert.IsFalse(summary.Contains(" context"), "Should not include context lines");
    }

    // -----------------------------------------------------------------------
    // FormatDiffSummary: null/empty → empty
    // -----------------------------------------------------------------------

    [TestMethod]
    public void FormatDiffSummary_Empty_WhenNoDiff()
    {
        Assert.AreEqual(string.Empty, Turn1SourceFormatter.FormatDiffSummary(null));
        Assert.AreEqual(string.Empty, Turn1SourceFormatter.FormatDiffSummary(string.Empty));
    }

    // -----------------------------------------------------------------------
    // FormatFileSection: header contains path
    // -----------------------------------------------------------------------

    [TestMethod]
    public void FormatFileSection_Header_ContainsPath()
    {
        string path = "src/foo.cpp";
        string source = "int main() {}";
        string result = Turn1SourceFormatter.FormatFileSection(path, source, null, false);
        Assert.IsTrue(result.Contains(path), "Output should contain the file path");
        Assert.IsTrue(result.Contains("FILE:"), "Output should contain FILE: label");
    }

    // -----------------------------------------------------------------------
    // FormatFileSection: new file uses NEW FILE label
    // -----------------------------------------------------------------------

    [TestMethod]
    public void FormatFileSection_NewFile_MarkedAsNew()
    {
        string diff = "--- /dev/null\n+++ b/foo.cpp\n@@ -0,0 +1,1 @@\n+int x = 0;\n";
        string result = Turn1SourceFormatter.FormatFileSection("foo.cpp", "int x = 0;", diff, isNew: true);
        Assert.IsTrue(result.Contains("NEW FILE:"), "New file should use NEW FILE label");
        Assert.IsFalse(result.Contains("FILE: foo.cpp\n") && !result.Contains("NEW FILE:"),
            "Should not use plain FILE: label for new files");
    }

    // -----------------------------------------------------------------------
    // Turn1ContextBuilder.Build: contains file section header
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Turn1ContextBuilder_Build_ContainsFileHeader()
    {
        ReviewContext ctx = new ReviewContext
        {
            Path = "src/example.cpp",
            Filename = "example.cpp",
            Diff = "@@ -1,2 +1,3 @@\n line1\n+line2\n line3\n",
            ChangedFile = "line1\nline2\nline3\n",
        };

        string result = Turn1ContextBuilder.Build(new[] { ctx });
        Assert.IsTrue(result.Contains("src/example.cpp"), "Should contain file path");
        Assert.IsTrue(result.Contains("================================"), "Should contain separator");
    }

    // -----------------------------------------------------------------------
    // Turn1ContextBuilder.Build: no raw AST JSON in output
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Turn1ContextBuilder_Build_NoRawAstJson()
    {
        string astJson = """{"language":"CSharp","functions":[{"qualified_name":"Foo.Bar","start_line":1,"end_line":5,"change":"modified"}],"call_graph":[]}""";

        ReviewContext ctx = new ReviewContext
        {
            Path = "Foo.cs",
            Filename = "Foo.cs",
            Diff = "@@ -1,2 +1,3 @@\n line\n+added\n",
            ChangedFile = "public class Foo {\n    void Bar() {}\n}\n",
            AstJson = astJson,
        };

        string result = Turn1ContextBuilder.Build(new[] { ctx });
        // Raw AST JSON should NOT be present
        Assert.IsFalse(result.Contains("\"functions\":"), "Raw AST JSON 'functions' key should not appear in output");
        Assert.IsFalse(result.Contains("\"call_graph\":"), "Raw AST JSON 'call_graph' key should not appear in output");
    }

    // -----------------------------------------------------------------------
    // Turn1ContextBuilder.Build: empty ChangedFile → diff-only section
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Turn1ContextBuilder_Build_DiffOnlyWhenNoSource()
    {
        ReviewContext ctx = new ReviewContext
        {
            Path = "src/missing.cpp",
            Filename = "missing.cpp",
            Diff = "@@ -1,1 +1,2 @@\n existing\n+new line\n",
            ChangedFile = string.Empty,
        };

        string result = Turn1ContextBuilder.Build(new[] { ctx });
        Assert.IsTrue(result.Contains("Full source unavailable"), "Should indicate source unavailable");
        Assert.IsTrue(result.Contains("src/missing.cpp"), "Should contain file path");
    }

    // -----------------------------------------------------------------------
    // SemanticSummaryBuilder: null/empty AST → empty string
    // -----------------------------------------------------------------------

    [TestMethod]
    public void SemanticSummaryBuilder_Empty_WhenNoAst()
    {
        ReviewContext ctx = new ReviewContext
        {
            Path = "foo.cpp",
            Filename = "foo.cpp",
            Diff = "@@ -1,1 +1,1 @@\n-old\n+new\n",
            ChangedFile = "int x = 1;",
            AstJson = null,
        };

        string result = SemanticSummaryBuilder.Build(new[] { ctx });
        Assert.AreEqual(string.Empty, result, "No AST should produce empty semantic summary");
    }

    // -----------------------------------------------------------------------
    // SemanticSummaryBuilder: changed symbols appear in output
    // -----------------------------------------------------------------------

    [TestMethod]
    public void SemanticSummaryBuilder_ListsChangedSymbols()
    {
        string astJson = """
            {
              "language": "Cpp",
              "functions": [
                {
                  "qualified_name": "Foo.Open",
                  "signature": "Foo::Open()",
                  "start_line": 1,
                  "end_line": 5,
                  "change": "modified",
                  "visibility": "public"
                },
                {
                  "qualified_name": "Foo.Close",
                  "signature": "Foo::Close()",
                  "start_line": 7,
                  "end_line": 10,
                  "change": "added",
                  "visibility": "public"
                }
              ]
            }
            """;

        ReviewContext ctx = new ReviewContext
        {
            Path = "foo.cpp",
            Filename = "foo.cpp",
            Diff = "@@ -1,1 +1,1 @@\n+int x;\n",
            ChangedFile = "void Open() {} void Close() {}",
            AstJson = astJson,
        };

        string result = SemanticSummaryBuilder.Build(new[] { ctx });
        Assert.IsFalse(string.IsNullOrEmpty(result), "Should produce non-empty summary");
        Assert.IsTrue(result.Contains("[SEMANTIC SUMMARY]"), "Should contain section header");
        Assert.IsTrue(result.Contains("Foo::Open()"), "Should list Open symbol");
        Assert.IsTrue(result.Contains("modified"), "Should list change type");
        Assert.IsTrue(result.Contains("Foo::Close()"), "Should list Close symbol");
        Assert.IsTrue(result.Contains("added"), "Should list added change type");
    }

    // -----------------------------------------------------------------------
    // PromptBuilder.BuildTurn1: contains template (ReviewRulesTurn1)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BuildTurn1_ContainsTemplate()
    {
        ReviewRequest request = new ReviewRequest
        {
            ReviewRulesTurn1 = "# REVIEW RULES TURN 1\nBe thorough.",
            MergeRequestTitle = "Test PR",
        };

        FileGroup fileGroup = new FileGroup { Topic = "test" };
        fileGroup.ReviewContexts.Add(new ReviewContext
        {
            Path = "a.cs",
            Filename = "a.cs",
            Diff = "@@ -1,1 +1,2 @@\n line\n+added\n",
            ChangedFile = "class A {}",
        });

        StringBuilder sb = new StringBuilder();
        string result = PRReviewAgent.Services.PromptBuilder.BuildTurn1(request, fileGroup, sb);

        Assert.IsTrue(result.Contains("# REVIEW RULES TURN 1"), "Should contain ReviewRulesTurn1 content");
        Assert.IsTrue(result.Contains("Be thorough."), "Should contain ReviewRulesTurn1 text");
    }

    // -----------------------------------------------------------------------
    // PromptBuilder.BuildTurn1: no raw AST JSON keys in output
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BuildTurn1_NoRawAstJson()
    {
        string astJson = """{"language":"CSharp","functions":[{"qualified_name":"A.Method","start_line":1,"end_line":3,"change":"modified","visibility":"public"}]}""";

        ReviewRequest request = new ReviewRequest
        {
            ReviewRulesTurn1 = "Review rules.",
        };

        FileGroup fileGroup = new FileGroup { Topic = "test" };
        fileGroup.ReviewContexts.Add(new ReviewContext
        {
            Path = "A.cs",
            Filename = "A.cs",
            Diff = "@@ -1,1 +1,2 @@\n line\n+added\n",
            ChangedFile = "class A { void Method() {} }",
            AstJson = astJson,
        });

        StringBuilder sb = new StringBuilder();
        string result = PRReviewAgent.Services.PromptBuilder.BuildTurn1(request, fileGroup, sb);

        Assert.IsFalse(result.Contains("\"functions\":"), "Raw AST JSON functions key must not appear");
    }
}
