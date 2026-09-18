# Phase 4 — Optimize Turn 1 Input with Full Changed Files and Diff Anchors

## Objective

Optimize the input provided to **Turn 1: Candidate Discovery**.

Phase 3 established the three-stage review pipeline:

```text
Turn 1: Candidate Discovery
    ↓
AST-driven Verification Context Resolution
    ↓
Turn 2: Candidate Verification
    ↓
Turn 3: Finalization
```

Phase 4 should improve Turn 1 by changing what source context it receives.

The new default strategy should be:

```text
full changed files
+
explicit diff anchors
+
small semantic summary
```

instead of relying heavily on expanded diffs or large AST-generated structured context.

The goal is to give the non-thinking model enough real source context to understand local invariants while keeping its attention strongly anchored to the actual changes.

---

# Primary Design Principle

Turn 1 should see:

> complete changed source files as context, but review only the changed code and its direct consequences.

The full file provides context.

The diff markers provide attention anchors.

The semantic summary provides navigation hints.

These three responsibilities must remain separate:

```text
Full source
    = context

Diff markers
    = attention anchor

Semantic summary
    = lightweight navigation
```

Do not treat AST JSON as a substitute for source code.

---

# Current Problem

The existing pipeline may provide combinations of:

```text
Diff
ExpandedDiff
AST JSON
Pair file context
```

This can make Turn 1 depend on pre-compressed representations of the code.

That creates several problems:

* important surrounding invariants may be omitted;
* macro or conditional context may be lost;
* ownership and lifetime relationships may be difficult to reconstruct;
* class state can be separated from changed methods;
* the LLM must interpret AST structure before reviewing the code;
* expanded context logic may become more complex than simply showing the actual file.

With a large local context window, this tradeoff is no longer desirable for changed files.

---

# Target Turn 1 Input

Turn 1 should receive approximately:

```text
Merge request title
Merge request description

Learned rules / applicable review rules

File group summary

Changed symbols summary

Dependency / relationship summary

================================
FILE: include/foo.h
================================

<full source with change annotations>

================================
FILE: src/foo.cpp
================================

<full source with change annotations>

================================
FILE: src/worker.cpp
================================

<full source with change annotations>
```

Do not append large raw AST JSON after these files.

---

# Scope

Implement:

* full changed-file source input for Turn 1;
* explicit change annotations inside full-file source;
* lightweight semantic summaries;
* input budgeting;
* deterministic file ordering;
* prompt formatting changes;
* compatibility with Candidate Discovery;
* tests and metrics.

Do not change:

* Verification Context architecture;
* Turn 2 responsibilities;
* Turn 3 responsibilities;
* severity policy;
* semantic grouping;
* RAG architecture;
* recursive AST traversal.

---

# Changed Files Only

The default full-file strategy applies to files actually changed by the merge request.

For example:

```text
include/foo.h       changed
src/foo.cpp         changed
src/worker.cpp      changed
src/resource.cpp    unchanged dependency
```

Turn 1 should normally receive full contents of:

```text
include/foo.h
src/foo.cpp
src/worker.cpp
```

Do not automatically include the entire unchanged:

```text
src/resource.cpp
```

That kind of related source belongs primarily to Turn 2 Verification Context Resolution.

---

# Pair Files

A pair file should be treated differently depending on whether it is changed.

## Changed pair file

If both are changed:

```text
foo.h
foo.cpp
```

include both in full.

## Unchanged pair file

If:

```text
foo.cpp changed
foo.h unchanged
```

do not automatically include the entire header solely because it is a pair.

Instead, prefer a compact semantic summary or relevant declaration.

Turn 2 can retrieve detailed declarations if a candidate requires verification.

An exception is allowed for very small headers when including the complete file is cheaper and clearer than extracting declarations.

Keep this deterministic and bounded.

---

# Diff Anchors

Do not send a plain full file without identifying changed regions.

The model must be able to distinguish:

```text
changed code
```

from:

```text
unchanged context
```

Use explicit, unambiguous annotations.

Do not rely on normal source characters such as `+` or `-` alone if they could be confused with valid C/C++ syntax.

Preferred design:

```text
/* REVIEW_CHANGE_BEGIN */

<changed/current source>

/* REVIEW_CHANGE_END */
```

with metadata describing removed lines separately where necessary.

Alternatively, use a line-oriented representation such as:

```text
  unchanged line
+ changed/additional current line
  unchanged line
```

only if this representation is already reliable in the current implementation.

The important requirement is that annotations must not make current source semantics ambiguous.

---

# Added and Modified Code

For current-file contents, highlight added or modified current lines.

Example:

```cpp
void Foo::Open()
{
    Initialize();

    /* REVIEW_CHANGE_BEGIN */
    resource_ = Resource::Create();
    return resource_.get();
    /* REVIEW_CHANGE_END */
}
```

The complete function remains visible.

Only the changed region is marked.

---

# Deleted Code

Deleted code is not present in the current source file, but it can be important for understanding regressions.

Preserve deleted code separately.

Preferred representation:

```text
[REMOVED CODE near Foo::Open]

- return ResourceHandle(resource_);
+ return resource_.get();
```

or:

```text
[CHANGE HISTORY]
Symbol: Foo::Open

Before:
    return ResourceHandle(resource_);

After:
    return resource_.get();
```

Do not splice deleted lines into current source in a way that makes the file syntactically invalid or confusing.

The model should be able to distinguish:

```text
current source
```

from:

```text
previous source
```

clearly.

---

# Preferred Representation

Prefer a two-part representation for each changed file:

```text
[FILE]
full current source with changed current lines marked

[DIFF SUMMARY]
only relevant old/new hunks
```

Example:

```text
================================
FILE: src/foo.cpp
================================

<complete current source with markers>

--------------------------------
DIFF
--------------------------------

@@ Foo::Open @@
- return ResourceHandle(resource_);
+ return resource_.get();
```

This provides:

* syntactically coherent current code;
* historical change information;
* clear attention anchors.

---

# Do Not Duplicate Excessively

Avoid sending:

```text
full file
+
entire git diff
+
expanded diff
+
AST JSON
+
full pair file
```

for every changed file.

That wastes context and dilutes attention.

After Phase 4, the intended hierarchy is:

```text
full changed source
small diff summary
small semantic summary
```

Everything else should be added only when justified.

---

# Semantic Summary

Turn 1 should receive a compact summary generated from existing AST / semantic information.

Suggested structure:

```text
[SEMANTIC SUMMARY]

Changed symbols:
- Foo::Open
- Foo::Close
- Worker::Run

Direct relationships:
- Worker::Run -> Foo::Open
- Foo::Open -> Resource::Create
- Foo::Open reads/writes Foo::resource_

Related declarations:
- Foo declared in include/foo.h
```

Keep this small.

The semantic summary is not a serialized AST.

---

# Semantic Summary Content

Include only high-value information such as:

```text
changed symbols
declaration / definition relationships
direct calls involving changed symbols
referenced fields
directly relevant types
header / source relationships
```

Do not include:

```text
full AST nodes
source ranges for every node
all imports
all references
all call graph edges
all inheritance information
all unrelated declarations
```

The model can read the full changed file.

Do not narrate what it can already see.

---

# AST Role After Phase 4

The intended AST responsibilities become:

```text
Turn 1:
generate lightweight semantic summary

Turn 2:
select targeted verification source context
```

AST should no longer dominate Turn 1 input.

Conceptually:

```text
                AST
                 │
        ┌────────┴─────────┐
        │                  │
        ▼                  ▼
small Turn 1 summary   Turn 2 context resolver
```

This distinction is important.

---

# Remove Large AST Context from Turn 1

If Turn 1 currently receives something like:

```text
ReviewContext.AstJson
```

directly in the prompt, stop including the large raw representation by default.

Do not necessarily remove `AstJson` from the data model yet.

It may still be required by:

* learned-rule retrieval;
* debugging;
* existing analytics;
* other internal features.

The change in this phase is specifically:

> do not expose large raw AST JSON to Candidate Discovery unless explicitly required as a fallback.

---

# Full File Source Acquisition

The existing review pipeline already fetches the complete changed file contents.

Reuse those contents.

Do not introduce redundant repository requests.

Prefer:

```text
ReviewContext.ChangedFile
```

or equivalent already-fetched source data.

Do not re-fetch each file while building the prompt.

---

# File Ordering

Use deterministic ordering.

Recommended order inside a file group:

1. public/header declarations;
2. primary implementation;
3. additional changed implementation files;
4. tests if changed;
5. other changed files.

Within a category, sort by normalized path.

If the current grouping already provides a stable meaningful order, preserve it.

The same merge request should produce the same Turn 1 input ordering.

---

# Header / Implementation Presentation

When a header and implementation are both changed, keep them adjacent.

Example:

```text
FILE: include/foo.h

...

FILE: src/foo.cpp

...
```

This helps the model compare:

* declaration;
* implementation;
* API contract;
* ownership;
* method signatures.

Do not separate related pair files unnecessarily.

---

# File Path Metadata

Every source section must include the complete repository-relative path.

Use:

```text
src/render/image.cpp
```

not:

```text
image.cpp
```

This reduces ambiguity when identical filenames exist in different modules.

---

# Line Numbers

If practical, include stable source line numbers.

Example:

```text
  118 | void Foo::Open()
  119 | {
+ 120 |     resource_ = Resource::Create();
  121 | }
```

Line numbers can improve candidate location accuracy.

However:

* do not make line-number generation expensive;
* do not corrupt source indentation;
* do not require the model to use line numbers if symbol names are more reliable.

Symbol-based locations should remain preferred when available.

---

# Prompt Instructions

Update `review1.en.md` so that the model understands the new context format.

The prompt should state clearly:

```text
The full current contents of changed files are provided.

Changed regions are explicitly marked.

Review changed code and its direct consequences.

Use unchanged code only to understand:
- invariants
- ownership
- lifetime
- control flow
- state
- API contracts
- synchronization
- relevant surrounding behavior

Do not report unrelated pre-existing problems found in unchanged code.
```

This instruction is critical.

Full files must not cause Turn 1 to become a general legacy-code reviewer.

---

# Candidate Scope

A candidate must still originate from the change.

Allowed:

```text
changed code creates a bug
changed code exposes an existing latent bug
changed code violates an existing invariant visible elsewhere in the file
changed API breaks unchanged callers
```

Not allowed:

```text
unrelated bug discovered elsewhere in the full file
old style issue
pre-existing complexity
unrelated cleanup opportunity
```

---

# Attention Priority

Tell Turn 1 to inspect in this order:

```text
1. changed lines / changed regions
2. containing function or type
3. directly affected state or contract
4. nearby unchanged code necessary to validate the suspicion
5. semantic summary when additional orientation is useful
```

Do not encourage broad scanning of every unchanged function.

---

# Context Budget

Even with a 256K-capable model, bound the input.

Do not treat maximum model context as the target prompt size.

Introduce configuration such as:

```text
MaxTurn1SourceChars
MaxTurn1SourceTokens
MaxFilesPerGroup
```

Use existing token estimation if available.

Otherwise, a deterministic character budget is acceptable temporarily.

---

# Budget Priority

When the file group exceeds the Turn 1 budget, preserve content in this order:

```text
1. files containing changed lines
2. changed header / implementation pairs
3. files with the largest or most significant changes
4. changed functions / types
5. remaining unchanged portions of very large changed files
```

Do not silently drop changed regions.

Every changed hunk in a reviewable file must remain represented.

---

# Large File Fallback

For very large changed files, full-file inclusion may be wasteful.

Introduce a fallback.

For example:

```text
if file <= FullFileThreshold:
    include full file
else:
    include:
        complete changed semantic scopes
        surrounding class/type context
        diff hunks
        relevant file-level declarations
```

The threshold should be configurable.

Do not hard-code a model-specific magic value unless existing configuration conventions make that appropriate.

---

# Large File Rule

A large file should never be reduced to:

```text
diff only
```

if doing so removes the containing function or type required for understanding the change.

Minimum large-file context should include:

```text
complete changed function/method
or
complete changed type declaration
```

plus relevant local context.

---

# Multiple Changed Scopes

If a large file has several unrelated changed functions:

```text
Foo::Open
Foo::Close
Foo::Reset
```

include each complete changed scope.

Do not include only the first changed region because the context budget was consumed earlier.

Allocate budget across changed scopes fairly.

---

# Removed Files

For deleted files, there is no current source.

Represent them using the previous file contents or deletion diff.

Example:

```text
================================
DELETED FILE: src/legacy.cpp
================================

<previous source or bounded relevant deleted scopes>
```

Candidate Discovery should be able to detect removal-related compatibility or behavior issues.

Use existing diff/repository data where available.

Do not invent current contents.

---

# Renamed Files

Represent rename metadata explicitly.

Example:

```text
RENAMED:
src/old_name.cpp
→
src/new_name.cpp
```

If source contents are changed as well, include the new full file contents and the relevant diff.

---

# New Files

For newly added files:

```text
full current source
```

is sufficient.

Mark:

```text
NEW FILE
```

clearly.

There is no need for a redundant before-state section.

---

# Binary / Generated / Unsupported Files

Preserve existing filtering rules.

Do not include:

* binary files;
* generated files excluded by policy;
* unsupported extensions;
* irrelevant assets.

Do not expand Phase 4 into file-target policy redesign.

---

# Tests

Add tests for the new Turn 1 source formatting.

At minimum cover:

## Full changed file

A small changed source file is included in full.

## Change markers

Changed current lines are clearly identifiable.

## Deleted lines

Removed code is represented separately from current source.

## Header and source

Changed header and implementation are both included and ordered together.

## Unchanged pair

An unchanged pair file is not automatically included in full.

## Semantic summary

Changed symbols and direct high-value relationships are included.

## AST JSON removal

Large raw AST JSON is absent from the default Turn 1 prompt.

## Large file fallback

A file above the configured threshold produces complete changed semantic scopes rather than full-file content.

## Multiple changed scopes

All changed scopes remain represented under truncation.

## Deterministic ordering

Identical input produces identical file ordering.

## New file

New-file content is represented correctly.

## Deleted file

Deleted-file context is represented without pretending current source exists.

## Rename

Rename metadata is preserved.

---

# Prompt Tests

Verify that `review1.en.md` clearly instructs the model to:

```text
review the change
use unchanged code as context
not review unrelated legacy code
not perform final verification
not assign severity
not produce final review prose
```

---

# Candidate Location Accuracy

Use the new full-source representation to improve locations where possible.

Prefer:

```text
repository/path.cpp: Namespace::Class::Method
```

over vague locations.

If line numbers are available, they may supplement but should not replace semantic locations.

---

# Metrics

Extend Turn 1 diagnostics with:

```text
full file count
partial large-file count
source characters
estimated source tokens
diff characters
semantic-summary characters
number of changed scopes
number of truncated files
```

This should make it possible to correlate:

```text
input size
    ↓
Turn 1 latency
    ↓
candidate quality
```

---

# Benchmark

Compare the Phase 3 input strategy against Phase 4.

Measure:

```text
Turn 1 input tokens
Turn 1 latency
Turn 1 output tokens
candidate count
candidate → verified survival rate
final finding count
overall review latency
```

Quality evaluation should focus on whether full-file context improves detection of:

```text
ownership issues
lifetime issues
class invariants
state transitions
macro / conditional behavior
constructor / destructor relationships
local API consistency
error paths
synchronization
```

---

# Expected Outcomes

A successful Phase 4 should reduce situations where Turn 1 misses an issue because the relevant code was omitted by context extraction.

At the same time, it should not significantly increase:

```text
unrelated findings
legacy-code findings
speculative candidates
```

If unrelated candidate generation increases, tighten prompt scope before reducing source context.

---

# Do Not Optimize for Maximum Context Usage

Do not aim to fill the model's available context window.

For example:

```text
256K available
```

does NOT imply:

```text
target 256K input
```

Use only context that materially helps review.

The large context window should remove pressure to aggressively compress changed files, not justify dumping the repository.

---

# RAG Compatibility

Keep learned-rule retrieval unchanged in this phase.

If it currently uses AST JSON internally, preserve that.

The Turn 1 prompt may still include selected learned rules.

Do not include the entire RAG query context in the review prompt.

---

# Verification Compatibility

Do not weaken Turn 2 because Turn 1 now receives more source.

The architecture remains:

```text
Turn 1:
discover suspicion

Turn 2:
verify suspicion with targeted source

Turn 3:
decide reporting policy and severity
```

Full changed files in Turn 1 do not eliminate the need for Verification.

---

# Avoid Cross-Stage Leakage

Turn 1 must not start producing:

```text
detailed verification evidence
final severity
final review wording
```

simply because it now has more source code.

Keep the Phase 1 Candidate Discovery contract unchanged.

Only the input representation changes.

---

# Suggested Internal Components

A clean implementation may introduce components similar to:

```text
Turn1ContextBuilder
    ├── ChangedFileFormatter
    ├── DiffFormatter
    ├── SemanticSummaryBuilder
    └── Turn1ContextBudget
```

Exact naming is flexible.

Avoid putting all formatting and budget logic directly inside `PromptBuilder.BuildTurn1()`.

The PromptBuilder should compose already-prepared context rather than become a source-analysis subsystem.

---

# Separation of Responsibilities

Preferred design:

```text
AST / semantic analysis
    ↓
SemanticSummaryBuilder

ReviewContext + Diff
    ↓
ChangedFileFormatter

Both
    ↓
Turn1ContextBuilder

Result
    ↓
PromptBuilder.BuildTurn1()
```

This keeps source preparation testable independently from prompt composition.

---

# Migration Strategy

Recommended implementation order:

```text
1. Add Turn1ContextBuilder.
2. Add changed-file full-source formatting.
3. Add safe diff/change annotations.
4. Add lightweight SemanticSummaryBuilder.
5. Switch BuildTurn1() to the new context representation.
6. Remove raw AST JSON from the default Turn 1 prompt.
7. Add context budgeting.
8. Add large-file fallback.
9. Add metrics.
10. Update tests.
11. Benchmark against Phase 3.
```

Keep each step buildable where practical.

---

# Compatibility Requirements

Preserve:

```text
CandidateIssue schema
candidate IDs
VerificationContextResolver
Turn 2 Verification
Turn 3 Finalization
learned-rule tracking
GitLab output behavior
file-grouping behavior
review metrics
```

Do not introduce schema churn unless required by context formatting.

---

# Do Not Implement Yet

Explicitly exclude:

```text
semantic file grouping redesign
repository-wide full-file context
automatic dependency-file inclusion
multi-hop relationship expansion
dynamic LLM context requests
verification batching optimization
parallel verification optimization
RAG redesign
severity redesign
new review categories
```

These belong to later phases.

---

# Acceptance Criteria

Phase 4 is complete when:

1. Turn 1 receives full current contents of normal-sized changed files.
2. Changed regions are explicitly and unambiguously marked.
3. Deleted code is represented separately from current source.
4. Changed header / implementation pairs are shown together.
5. Unchanged dependency files are not automatically included in full.
6. Turn 1 receives a compact semantic summary.
7. Large raw AST JSON is removed from the default Turn 1 prompt.
8. AST remains available internally for semantic summaries and Verification.
9. Full-file input remains bounded by configurable limits.
10. Large changed files use semantic-scope fallback.
11. Every changed region remains represented after budgeting.
12. File ordering is deterministic.
13. Candidate Discovery responsibilities are unchanged.
14. Turn 2 Verification behavior is unchanged.
15. Turn 3 Finalization behavior is unchanged.
16. Learned-rule behavior remains functional.
17. Turn 1 context-size metrics are available.
18. New, deleted, and renamed files are handled correctly.
19. Relevant tests pass.
20. The project builds successfully.

---

# Implementation Principle

Do not compress source code merely because AST can describe it.

For Candidate Discovery, prefer giving the model the actual changed program.

Use AST to orient the model, not replace the program.

The intended Phase 4 architecture is:

```text
Changed files
    │
    ├── full current source
    ├── explicit change anchors
    └── small semantic summary
            │
            ▼
     Turn 1 Discovery
            │
            ▼
        Candidates
            │
            ▼
    AST-driven targeted context
            │
            ▼
     Turn 2 Verification
```

The core principle is:

> Use the large context window to preserve source semantics, while using diff markers to preserve attention.
