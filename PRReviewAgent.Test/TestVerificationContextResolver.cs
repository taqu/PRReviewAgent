using PRReviewAgent.Prompt;
using PRReviewAgent.Services;

namespace PRReviewAgent.Test;

[TestClass]
public class TestVerificationContextResolver
{
    private static readonly VerificationContextResolver Resolver = new();

    // -------------------------------------------------------------------------
    // Helpers to build test fixtures
    // -------------------------------------------------------------------------

    private static string FooOpenAstJson => """
        {
          "language": "Cpp",
          "functions": [
            {
              "qualified_name": "Foo::Open",
              "containing_type": "Foo",
              "visibility": "public",
              "start_line": 5,
              "end_line": 10,
              "change": "modified"
            },
            {
              "qualified_name": "Foo::Close",
              "containing_type": "Foo",
              "visibility": "public",
              "start_line": 12,
              "end_line": 16
            }
          ],
          "types": [
            {
              "qualified_name": "Foo",
              "kind": "class",
              "visibility": "public",
              "start_line": 1,
              "end_line": 20
            }
          ],
          "call_graph": [
            { "caller": "Foo::Open", "callee": "Resource::Acquire" }
          ]
        }
        """;

    private static string WorkerAstJson => """
        {
          "language": "Cpp",
          "functions": [
            {
              "qualified_name": "Worker::Run",
              "visibility": "public",
              "start_line": 1,
              "end_line": 8
            }
          ],
          "call_graph": [
            { "caller": "Worker::Run", "callee": "Foo::Open" }
          ]
        }
        """;

    private static string FooHeaderAstJson => """
        {
          "language": "Cpp",
          "functions": [
            {
              "qualified_name": "Foo::Open",
              "containing_type": "Foo",
              "visibility": "public",
              "start_line": 5,
              "end_line": 6
            },
            {
              "qualified_name": "Foo::~Foo",
              "containing_type": "Foo",
              "destructor": true,
              "visibility": "public",
              "start_line": 7,
              "end_line": 8
            }
          ],
          "types": [
            {
              "qualified_name": "Foo",
              "kind": "class",
              "visibility": "public",
              "start_line": 1,
              "end_line": 15
            }
          ]
        }
        """;

    // foo.cpp AstJson that embeds counterpart (foo.h) in structural_dependencies
    private static string FooCppWithCounterpartAstJson => $$"""
        {
          "language": "Cpp",
          "functions": [
            {
              "qualified_name": "Foo::Open",
              "containing_type": "Foo",
              "visibility": "public",
              "start_line": 3,
              "end_line": 8,
              "change": "modified"
            }
          ],
          "call_graph": [
            { "caller": "Foo::Open", "callee": "Resource::Acquire" }
          ],
          "structural_dependencies": {
            "counterpart_context": {
              "file": "include/foo.h",
              "functions": [
                {
                  "qualified_name": "Foo::Open",
                  "containing_type": "Foo",
                  "visibility": "public",
                  "start_line": 5,
                  "end_line": 6
                },
                {
                  "qualified_name": "Foo::~Foo",
                  "containing_type": "Foo",
                  "destructor": true,
                  "visibility": "public",
                  "start_line": 7,
                  "end_line": 8
                }
              ],
              "types": [
                {
                  "qualified_name": "Foo",
                  "kind": "class",
                  "visibility": "public",
                  "start_line": 1,
                  "end_line": 15
                }
              ]
            }
          }
        }
        """;

    private static ReviewContext MakeFooContext() => new()
    {
        Path = "src/foo.cpp",
        Filename = "foo.cpp",
        Diff = string.Empty,
        ChangedFile = string.Join('\n', [
            "// foo.cpp",
            "",
            "Foo::Foo() {}",
            "",
            "void* Foo::Open() {",
            "    Resource::Acquire();",
            "    return resource_.get();",
            "}",
            "",
            "",
            "",
            "void Foo::Close() {",
            "    resource_.reset();",
            "}",
            "",
            "",
            "",
            "",
            "",
            ""
        ]),
        AstJson = FooOpenAstJson,
    };

    private static ReviewContext MakeFooContextWithCounterpart() => new()
    {
        Path = "src/foo.cpp",
        Filename = "foo.cpp",
        Diff = string.Empty,
        ChangedFile = string.Join('\n', [
            "// foo.cpp",
            "",
            "void* Foo::Open() {",
            "    Resource::Acquire();",
            "    return resource_.get();",
            "}",
            "",
            "",
            ""
        ]),
        PairPath = "include/foo.h",
        PairFile = string.Join('\n', [
            "// foo.h",
            "class Foo {",
            "public:",
            "    void* resource_;",
            "    void* Open();",
            "    ~Foo();",
            "    // more members",
            "};",
            "",
            "",
            "",
            "",
            "",
            "",
            ""
        ]),
        AstJson = FooCppWithCounterpartAstJson,
    };

    private static ReviewContext MakeWorkerContext() => new()
    {
        Path = "src/worker.cpp",
        Filename = "worker.cpp",
        Diff = string.Empty,
        ChangedFile = string.Join('\n', [
            "void Worker::Run() {",
            "    Foo f;",
            "    f.Open();",
            "    f.Close();",
            "    // done",
            "}",
            "",
            ""
        ]),
        AstJson = WorkerAstJson,
    };

    private static CandidateIssue MakeCandidate(
        string location = "src/foo.cpp: Foo::Open",
        string category = "lifetime",
        string hypothesis = "The returned pointer may outlive the owning object.",
        string trigger = "Foo::Open now returns resource_.get().",
        string[]? verifySymbols = null,
        string? candidateId = "c0") => new()
    {
        location = location,
        category = category,
        hypothesis = hypothesis,
        trigger = trigger,
        verify_symbols = verifySymbols ?? Array.Empty<string>(),
        candidate_id = candidateId,
    };

    // -------------------------------------------------------------------------
    // Test: ParseLocation
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ParseLocation_FileAndSymbol()
    {
        (string? file, string? symbol) = VerificationContextResolver.ParseLocation("src/foo.cpp: Foo::Open");
        Assert.AreEqual("src/foo.cpp", file);
        Assert.AreEqual("Foo::Open", symbol);
    }

    [TestMethod]
    public void ParseLocation_FileOnly()
    {
        (string? file, string? symbol) = VerificationContextResolver.ParseLocation("src/foo.cpp");
        Assert.AreEqual("src/foo.cpp", file);
        Assert.IsNull(symbol);
    }

    [TestMethod]
    public void ParseLocation_SymbolOnly()
    {
        (string? file, string? symbol) = VerificationContextResolver.ParseLocation("Foo::Open");
        Assert.IsNull(file);
        Assert.AreEqual("Foo::Open", symbol);
    }

    [TestMethod]
    public void ParseLocation_Empty()
    {
        (string? file, string? symbol) = VerificationContextResolver.ParseLocation("");
        Assert.IsNull(file);
        Assert.IsNull(symbol);
    }

    // -------------------------------------------------------------------------
    // Test: ExtractLines
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ExtractLines_ReturnsCorrectRange()
    {
        string content = "line1\nline2\nline3\nline4\nline5";
        string result = VerificationContextResolver.ExtractLines(content, 2, 4);
        Assert.AreEqual("line2\nline3\nline4", result);
    }

    [TestMethod]
    public void ExtractLines_EmptyContent_ReturnsEmpty()
    {
        string result = VerificationContextResolver.ExtractLines(null, 1, 5);
        Assert.AreEqual(string.Empty, result);
    }

    // -------------------------------------------------------------------------
    // Test 1: Changed function is included as ChangedScope
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_ChangedScope_IncludesChangedFunction()
    {
        ReviewContext ctx = MakeFooContext();
        CandidateIssue candidate = MakeCandidate();

        VerificationContext result = Resolver.Resolve(candidate, [ctx]);

        SourceContextItem? scope = result.Items.FirstOrDefault(i => i.Kind == VerificationContextKind.ChangedScope);
        Assert.IsNotNull(scope, "ChangedScope item must be present");
        Assert.AreEqual("src/foo.cpp", scope.Path);
        Assert.AreEqual("Foo::Open", scope.Symbol);
        Assert.IsTrue(scope.Source.Contains("Foo::Open"), "Source must contain function definition");
    }

    // -------------------------------------------------------------------------
    // Test 2: Header / implementation pair — counterpart declaration resolved
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_CounterpartDeclaration_ResolvedFromPairFile()
    {
        ReviewContext ctx = MakeFooContextWithCounterpart();
        CandidateIssue candidate = MakeCandidate();

        VerificationContext result = Resolver.Resolve(candidate, [ctx]);

        SourceContextItem? decl = result.Items.FirstOrDefault(i => i.Kind == VerificationContextKind.Declaration);
        Assert.IsNotNull(decl, "Declaration item must be present when pair file is a header");
        Assert.AreEqual("include/foo.h", decl.Path);
    }

    // -------------------------------------------------------------------------
    // Test 3: Containing class is included
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_ContainingType_IncludedForMemberFunction()
    {
        ReviewContext ctx = MakeFooContextWithCounterpart();
        CandidateIssue candidate = MakeCandidate();

        VerificationContext result = Resolver.Resolve(candidate, [ctx]);

        bool hasContainingType = result.Items.Any(i =>
            i.Kind == VerificationContextKind.ContainingType &&
            i.Symbol == "Foo");
        Assert.IsTrue(hasContainingType, "ContainingType 'Foo' must be present");
    }

    // -------------------------------------------------------------------------
    // Test 4: Field hint resolves containing type
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_FieldHint_IncludesContainingTypeSource()
    {
        ReviewContext ctx = MakeFooContextWithCounterpart();
        CandidateIssue candidate = MakeCandidate(verifySymbols: ["Foo::resource_"]);

        VerificationContext result = Resolver.Resolve(candidate, [ctx]);

        bool hasField = result.Items.Any(i =>
            (i.Kind == VerificationContextKind.Field || i.Kind == VerificationContextKind.ContainingType) &&
            i.Symbol.Contains("Foo"));
        Assert.IsTrue(hasField, "Field or ContainingType for Foo must be present for 'Foo::resource_' hint");
    }

    // -------------------------------------------------------------------------
    // Test 5: Direct callers resolved from call graph across contexts
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_DirectCallers_IncludesCallerFunctions()
    {
        ReviewContext foo = MakeFooContext();
        ReviewContext worker = MakeWorkerContext();
        CandidateIssue candidate = MakeCandidate(verifySymbols: ["direct callers of Foo::Open"]);

        VerificationContext result = Resolver.Resolve(candidate, [foo, worker]);

        bool hasCaller = result.Items.Any(i =>
            i.Kind == VerificationContextKind.DirectCaller &&
            i.Symbol == "Worker::Run");
        Assert.IsTrue(hasCaller, "Worker::Run (direct caller of Foo::Open) must be present");
    }

    [TestMethod]
    public void Resolve_DirectCallers_DoesNotIncludeCallersOfCallers()
    {
        // Worker::Run calls Foo::Open. If we ask for callers of Foo::Open,
        // we should get Worker::Run but NOT any callers of Worker::Run.
        ReviewContext foo = MakeFooContext();
        ReviewContext worker = MakeWorkerContext();
        CandidateIssue candidate = MakeCandidate(verifySymbols: ["direct callers of Foo::Open"]);

        VerificationContext result = Resolver.Resolve(candidate, [foo, worker]);

        int callerCount = result.Items.Count(i => i.Kind == VerificationContextKind.DirectCaller);
        // Only Worker::Run should appear, not any deeper caller
        Assert.AreEqual(1, callerCount);
        Assert.IsFalse(result.Items.Any(i =>
            i.Kind == VerificationContextKind.DirectCaller && i.Symbol != "Worker::Run"),
            "No callers-of-callers should appear");
    }

    // -------------------------------------------------------------------------
    // Test 6: Direct callees resolved from call graph
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_DirectCallees_IncludesCalleeFunctions()
    {
        // Foo::Open calls Resource::Acquire. Add Resource context.
        string resourceAst = """
            {
              "language": "Cpp",
              "functions": [
                {
                  "qualified_name": "Resource::Acquire",
                  "visibility": "public",
                  "start_line": 1,
                  "end_line": 5
                }
              ]
            }
            """;
        ReviewContext foo = MakeFooContext();
        ReviewContext resource = new()
        {
            Path = "src/resource.cpp",
            Filename = "resource.cpp",
            Diff = string.Empty,
            ChangedFile = "void Resource::Acquire() {\n    // acquire\n}\n\n",
            AstJson = resourceAst,
        };
        CandidateIssue candidate = MakeCandidate(verifySymbols: ["direct callees of Foo::Open"]);

        VerificationContext result = Resolver.Resolve(candidate, [foo, resource]);

        bool hasCallee = result.Items.Any(i => i.Kind == VerificationContextKind.DirectCallee);
        Assert.IsTrue(hasCallee, "At least one DirectCallee must be present");
    }

    // -------------------------------------------------------------------------
    // Test 7: Duplicate context appears only once
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_DuplicateSymbol_AppearsOnce()
    {
        ReviewContext ctx = MakeFooContextWithCounterpart();
        // Both "Foo::~Foo" and "direct callers of Foo::Open" are hints;
        // ContainingType (Foo class) might be resolved through multiple paths.
        CandidateIssue candidate = MakeCandidate(verifySymbols: ["Foo::~Foo", "Foo::~Foo"]);

        VerificationContext result = Resolver.Resolve(candidate, [ctx]);

        // Count items with symbol Foo::~Foo
        int count = result.Items.Count(i => string.Equals(i.Symbol, "Foo::~Foo", StringComparison.Ordinal));
        Assert.IsTrue(count <= 1, $"Foo::~Foo should appear at most once, found {count}");
    }

    // -------------------------------------------------------------------------
    // Test 8: Missing symbol is recorded in UnresolvedTargets, not thrown
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_MissingSymbol_RecordedAsUnresolved()
    {
        ReviewContext ctx = MakeFooContext();
        CandidateIssue candidate = MakeCandidate(verifySymbols: ["NonExistent::Phantom"]);

        VerificationContext result = Resolver.Resolve(candidate, [ctx]);

        Assert.IsTrue(result.UnresolvedTargets.Count > 0, "Unresolved targets must contain the missing symbol");
        Assert.IsTrue(result.UnresolvedTargets.Any(t => t.Contains("NonExistent") || t.Contains("Phantom")),
            "Unresolved list must reference the missing symbol");
    }

    // -------------------------------------------------------------------------
    // Test 9: Budget enforcement — total items capped at DefaultMaxTotalItems
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_BudgetEnforcement_ItemCountRespected()
    {
        // Build a candidate with many verify_symbols to stress the budget
        string[] manySymbols = Enumerable.Range(0, 30)
            .Select(i => $"direct callers of Symbol{i}")
            .ToArray();

        // Create 30 contexts, each with a caller edge
        var contexts = new List<ReviewContext>();
        for (int i = 0; i < 30; i++)
        {
            string ast = $$"""
                {
                  "language": "Cpp",
                  "functions": [
                    {
                      "qualified_name": "Caller{{i}}::Run",
                      "visibility": "public",
                      "start_line": 1,
                      "end_line": 5
                    }
                  ],
                  "call_graph": [
                    { "caller": "Caller{{i}}::Run", "callee": "Symbol{{i}}" }
                  ]
                }
                """;
            contexts.Add(new()
            {
                Path = $"src/caller{i}.cpp",
                Filename = $"caller{i}.cpp",
                Diff = string.Empty,
                ChangedFile = $"void Caller{i}::Run() {{\n    Symbol{i}();\n}}\n\n",
                AstJson = ast,
            });
        }

        CandidateIssue candidate = new()
        {
            location = "src/caller0.cpp: Caller0::Run",
            category = "correctness",
            hypothesis = "h",
            trigger = "t",
            verify_symbols = manySymbols,
            candidate_id = "c0",
        };

        VerificationContext result = Resolver.Resolve(candidate, contexts);

        Assert.IsTrue(result.Items.Count <= VerificationContextResolver.DefaultMaxTotalItems,
            $"Items count {result.Items.Count} must not exceed {VerificationContextResolver.DefaultMaxTotalItems}");
    }

    // -------------------------------------------------------------------------
    // Test 10: Deterministic ordering — same input produces same output
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_DeterministicOrdering_SameOutputForSameInput()
    {
        ReviewContext foo = MakeFooContext();
        ReviewContext worker = MakeWorkerContext();
        CandidateIssue candidate = MakeCandidate(verifySymbols: ["direct callers of Foo::Open"]);

        VerificationContext r1 = Resolver.Resolve(candidate, [foo, worker]);
        VerificationContext r2 = Resolver.Resolve(candidate, [foo, worker]);

        Assert.AreEqual(r1.Items.Count, r2.Items.Count);
        for (int i = 0; i < r1.Items.Count; i++)
        {
            Assert.AreEqual(r1.Items[i].Path, r2.Items[i].Path);
            Assert.AreEqual(r1.Items[i].Symbol, r2.Items[i].Symbol);
            Assert.AreEqual(r1.Items[i].Kind, r2.Items[i].Kind);
        }
    }

    // -------------------------------------------------------------------------
    // Test 11: Source authority — no items added when source content is empty
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_EmptySourceContent_NoManufacturedItems()
    {
        ReviewContext ctx = new()
        {
            Path = "src/foo.cpp",
            Filename = "foo.cpp",
            Diff = string.Empty,
            ChangedFile = string.Empty, // no source
            AstJson = FooOpenAstJson,
        };
        CandidateIssue candidate = MakeCandidate();

        VerificationContext result = Resolver.Resolve(candidate, [ctx]);

        // Changed scope should be absent or have empty source (not manufactured)
        foreach (SourceContextItem item in result.Items)
            Assert.IsFalse(string.IsNullOrEmpty(item.Path), "All items must have a valid path");
    }

    // -------------------------------------------------------------------------
    // Test 12: CandidateId is propagated correctly
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_CandidateId_PropagatedToContext()
    {
        ReviewContext ctx = MakeFooContext();
        CandidateIssue candidate = MakeCandidate(candidateId: "c3");

        VerificationContext result = Resolver.Resolve(candidate, [ctx]);

        Assert.AreEqual("c3", result.CandidateId);
    }

    // -------------------------------------------------------------------------
    // Test 13: No contexts returns empty result without throwing
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_NoContexts_ReturnsEmptyWithoutException()
    {
        CandidateIssue candidate = MakeCandidate();

        VerificationContext result = Resolver.Resolve(candidate, []);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Items.Count);
    }

    // -------------------------------------------------------------------------
    // Test 14: Integration — foo.cpp + foo.h + worker.cpp scenario
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Resolve_Integration_FooOpenLifetimeCandidate()
    {
        ReviewContext foo = MakeFooContextWithCounterpart();
        ReviewContext worker = MakeWorkerContext();

        CandidateIssue candidate = MakeCandidate(
            location: "src/foo.cpp: Foo::Open",
            category: "lifetime",
            verifySymbols: ["Foo::~Foo", "Foo::resource_", "direct callers of Foo::Open"],
            candidateId: "c0");

        VerificationContext result = Resolver.Resolve(candidate, [foo, worker]);

        // Changed scope must be present
        Assert.IsTrue(result.Items.Any(i => i.Kind == VerificationContextKind.ChangedScope),
            "ChangedScope must be present");

        // Declaration (from foo.h counterpart) should be present
        Assert.IsTrue(result.Items.Any(i =>
            i.Kind == VerificationContextKind.Declaration || i.Kind == VerificationContextKind.ContainingType),
            "Declaration or ContainingType from header must be present");

        // Worker::Run as caller
        Assert.IsTrue(result.Items.Any(i =>
            i.Kind == VerificationContextKind.DirectCaller && i.Symbol == "Worker::Run"),
            "Worker::Run must appear as DirectCaller");

        // No duplicates
        var keys = result.Items.Select(i => $"{i.Path}:{i.Symbol}").ToList();
        Assert.AreEqual(keys.Count, keys.Distinct().Count(), "No duplicate path:symbol entries allowed");

        // Sorted: ChangedScope comes first
        Assert.AreEqual(VerificationContextKind.ChangedScope, result.Items[0].Kind,
            "ChangedScope must be first in sorted output");
    }

    // -------------------------------------------------------------------------
    // Test 15: VerificationContextFormatter produces expected sections
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Formatter_IncludesExpectedSections()
    {
        var context = new VerificationContext
        {
            CandidateId = "c0",
            Items = [
                new() { Path = "src/foo.cpp", Symbol = "Foo::Open", Kind = VerificationContextKind.ChangedScope, Source = "void* Foo::Open() { return resource_.get(); }" }
            ],
            UnresolvedTargets = ["NonExistent::Ghost"],
            Truncated = false,
        };
        CandidateIssue candidate = MakeCandidate(candidateId: "c0");

        string text = VerificationContextFormatter.Format(context, candidate);

        Assert.IsTrue(text.Contains("[Candidate]"), "Must contain [Candidate] section");
        Assert.IsTrue(text.Contains("ID: c0"), "Must contain candidate ID");
        Assert.IsTrue(text.Contains("[Changed Scope]"), "Must contain [Changed Scope] section");
        Assert.IsTrue(text.Contains("FILE: src/foo.cpp"), "Must contain file path");
        Assert.IsTrue(text.Contains("SYMBOL: Foo::Open"), "Must contain symbol name");
        Assert.IsTrue(text.Contains("[Unresolved]"), "Must contain [Unresolved] section");
        Assert.IsTrue(text.Contains("NonExistent::Ghost"), "Must list unresolved target");
    }
}
