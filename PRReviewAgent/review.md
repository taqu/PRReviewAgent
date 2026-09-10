# Phase 3 — Reduce Rule Extraction Context Conservatively

## Goal

Reduce the amount of AST, symbol, and dependency information sent to the rule-extraction SubAgent.

The rule-extraction model should primarily reason from the expanded Before/After diff.

AST and semantic information should be treated only as supporting context.

The goal is not maximum extraction accuracy.

The goal is to reduce irrelevant context and lower the chance that the small rule-extraction model invents a project policy from unrelated file information.

Keep this phase conservative and small.

---

## Current Behavior

The current pipeline produces:

```text
AST JSON
Expanded Diff
```

from the changed file and passes the AST context into rule extraction.

The prompt currently contains conceptually:

```text
# File Dependencies
...

# AST Context
...

# Code Diff
...
```

This means the SubAgent may receive information from the entire file that is unrelated to the actual code change.

Phase 3 must change the input model to:

```text
Expanded Diff
        +
Relevant Structural Context
        +
Small Relevant Semantic Context
```

instead of:

```text
Expanded Diff
        +
Full AST
        +
All File Symbols
        +
All Dependencies
```

---

## Core Principle

Use this principle throughout the implementation:

```text
Analyze broadly.
Send narrowly.
```

The AST subsystem may continue analyzing the entire file.

Do not weaken the parser or remove full-file analysis.

The reduction must happen only when constructing the context that is sent to the SubAgent.

Conceptually:

```text
Source File
    |
    v
Full AST Analysis
    |
    v
Symbol / Dependency Information
    |
    v
Context Selection
    |
    v
Compact RuleExtractionContext
    |
    v
RuleExtractionSubAgent
```

---

## Expanded Diff Is Primary Evidence

The expanded Before/After diff must become the primary input to rule extraction.

The prompt should be organized so that the model sees the changed code before auxiliary structural information.

Recommended order:

```text
# Language

C++

# Changed Code

<expanded Before/After diff>

# Relevant Structure

<small AST-derived context>

# Relevant Semantic Context

<only directly related symbols/dependencies>
```

Do not place full-file metadata before the changed code.

---

## Preserve Expanded Diff Generation

Do not redesign expanded diff generation in this phase.

Continue using the existing expanded diff produced by the current AST/context extraction pipeline.

The existing surrounding-block expansion should remain intact.

The purpose of Phase 3 is to reduce unrelated auxiliary context, not to shrink the expanded diff itself.

---

## Introduce a Compact Context Model

Replace raw full-AST strings in the rule-extraction boundary with a compact context representation.

A possible shape is:

```csharp
public sealed record RuleExtractionContext
{
    public required SourceLanguage Language { get; init; }

    public required string FilePath { get; init; }

    public required string ExpandedDiff { get; init; }

    public IReadOnlyList<StructuralContext> Structures { get; init; }
        = [];

    public IReadOnlyList<SymbolContext> Symbols { get; init; }
        = [];

    public IReadOnlyList<DependencyContext> Dependencies { get; init; }
        = [];
}
```

The exact types may differ based on the existing AST representation.

Avoid building an unnecessarily complex intermediate representation in this phase.

---

## Relevant Structural Context

Include only structural information that directly contains or describes the changed code.

Preferred information:

```text
changed AST node type
containing block
containing function or method
containing class / struct / module / namespace
```

Examples:

```text
changed_node: if_statement
containing_function: ProcessRequest
containing_type: RequestHandler
```

or:

```text
changed_node: with_statement
containing_function: load_config
module: config_loader
```

Do not send the entire syntax tree.

---

## Relevant Symbols

Include only symbols directly referenced by the changed code.

Examples:

```text
function/method called by changed expressions
variable or field referenced by changed expressions
type used by changed declarations
enum referenced by changed expressions
property accessed by changed expressions
```

Use direct references only in this phase.

Conceptually:

```text
changed expression
    |
    +-- directly referenced symbol
              |
              +-- declaration/signature
```

Do not recursively traverse the full symbol graph.

---

## One-Hop Limit

Use a one-hop relevance rule by default.

For example:

```text
Changed code calls Foo.Run()
        |
        v
Include Foo.Run() signature
```

Do not automatically include:

```text
Foo.Run()
    -> calls Bar()
    -> Bar() accesses Baz
    -> Baz depends on Qux
```

The SubAgent does not need an arbitrary call graph for rule extraction.

If one-hop context is insufficient for some future cases, that can be evaluated later.

---

## Relevant Dependencies

Do not include the complete dependency list.

Prefer only dependencies directly connected to the change.

Examples that may be included:

```text
include/import added by this change
include/import removed by this change
module containing a directly referenced symbol
paired source/header file directly involved in the change
```

Examples that should normally be excluded:

```text
all includes in the file
all Python imports
all Rust use declarations
all project references
all transitive dependencies
```

---

## Include/Import Handling

Changed includes/imports are stronger evidence than unchanged global dependency metadata.

For example:

```cpp
+#include "project/guard.h"
```

or:

```python
+from contextlib import closing
```

may be relevant to the rule.

An existing unrelated include/import elsewhere in the file usually is not.

Therefore:

```text
added/removed include/import
    -> include when relevant

unchanged unrelated include/import
    -> exclude
```

---

## Language-Neutral Context Selection

The selection mechanism should work across the supported languages introduced in Phase 2:

```text
C
C++
C#
Python
Rust
```

Do not build completely separate context pipelines for every language.

Use a shared conceptual model:

```text
changed node
containing scope
direct references
relevant dependency changes
```

Language-specific extractors may provide the data where required.

---

## C and C++

Relevant context may include:

```text
containing function
containing class/struct
direct function declarations
directly referenced types
directly referenced fields
changed include directives
relevant enum values
relevant macro definitions
```

Do not include every declaration from the translation unit.

---

## C#

Relevant context may include:

```text
containing method
containing class/struct/record
directly referenced method signature
directly referenced property/field
interface or base type only when directly involved
changed using directive
```

Do not include all members of the class unless they are required to describe the changed block.

---

## Python

Relevant context may include:

```text
containing function
containing class
directly called function/method names
directly referenced imported symbol
changed import statement
```

Do not attempt full static type resolution for Python solely for this feature.

Missing type information is acceptable.

The SubAgent can still extract simple reusable rules from expanded Before/After code.

---

## Rust

Relevant context may include:

```text
containing function
containing impl block
containing trait when directly relevant
directly referenced type
directly called function/method
changed use declaration
relevant enum variant
```

Do not recursively expand trait implementations or module dependencies.

---

## Context Selection Must Be Deterministic

Do not ask the SubAgent to select its own relevant AST context from a full AST dump.

Bad:

```text
Here is the full AST.
Determine which parts are relevant.
```

Preferred:

```text
Application-side AST processing
        |
        v
deterministic relevance selection
        |
        v
small context
        |
        v
SubAgent
```

The SLM should perform rule extraction, not context retrieval.

---

## Conservative Missing-Context Behavior

If useful AST or symbol information cannot be resolved, do not compensate by sending the full AST.

For example:

```text
direct declaration not found
```

should result in:

```text
omit that semantic item
```

not:

```text
send all declarations in the file
```

The expanded diff should remain sufficient for basic extraction.

This feature is auxiliary, so incomplete context is preferable to large unrelated context.

---

## Context Size Limits

Introduce simple limits to prevent accidental context growth.

Exact limits may be chosen based on the current AST representation, but the design should support bounds such as:

```text
maximum structural entries
maximum symbol entries
maximum dependency entries
maximum serialized context length
```

For example:

```text
structures <= 8
symbols <= 16
dependencies <= 8
```

These values are examples, not mandatory exact defaults.

Prefer deterministic truncation based on relevance rather than arbitrary unordered truncation.

---

## Relevance Ordering

When more context exists than the configured limit, prefer this order:

```text
1. changed AST node
2. containing function/method
3. containing type/module
4. symbols directly referenced in changed lines
5. symbols referenced in the expanded surrounding block
6. changed includes/imports
7. paired-file context
```

Drop lower-priority information first.

---

## Do Not Change Rule Semantics Yet

Phase 3 should not redesign the rule-extraction prompt into its final conservative form.

Do not add:

```text
UNKNOWN
confidence
evidence
project-policy promotion
new validation logic
```

yet.

The main behavioral change in this phase is the context supplied to the existing extraction prompt.

Minor wording changes required to rename `AST Context` to `Relevant Structure` or similar are acceptable.

---

## Prompt Construction

Update prompt construction so it no longer expects a full AST dump.

Recommended structure:

```text
<existing language-neutral extraction system prompt>

# Language
Python

# Changed Code
<expanded diff>

# Relevant Structure
<selected structural context>

# Relevant Semantic Context
<selected symbols and dependencies>
```

Omit empty sections entirely.

Do not emit placeholders such as:

```text
# Relevant Semantic Context
None
```

unless existing prompt conventions specifically require them.

---

## Keep Full AST Available Internally

Do not delete existing AST output solely because it is no longer sent to the SubAgent.

It may still be useful for:

```text
expanded diff generation
file pairing
other review features
future semantic services
debugging
```

This phase changes the rule-extraction boundary, not the entire static-analysis architecture.

---

## Logging and Diagnostics

Add lightweight diagnostics that allow context reduction to be evaluated.

Useful data:

```text
file path
language
expanded diff length
number of structural entries
number of symbol entries
number of dependency entries
serialized auxiliary-context length
```

Do not log complete source code or complete AST payloads in normal logs.

Example:

```text
Rule extraction context for {Path}: structures={Structures}, symbols={Symbols}, dependencies={Dependencies}
```

---

## Tests

Add focused tests for the context-selection layer.

### Expanded Diff Preservation

Verify that the expanded diff sent to the SubAgent is unchanged from the existing extraction pipeline.

---

### Full AST Exclusion

Given a file containing many unrelated declarations, verify that unrelated AST nodes are not included in the SubAgent prompt.

Example:

```text
file:
    FunctionA
    FunctionB
    FunctionC
    ChangedFunction
    FunctionD
```

If only `ChangedFunction` is modified, the prompt should not contain unrelated `FunctionA`, `FunctionB`, `FunctionC`, or `FunctionD` AST details unless directly referenced.

---

### Direct Symbol Selection

Given:

```cpp
if (resource)
    resource->Run();
```

verify that directly relevant information about `resource` and/or `Run()` may be included.

Verify that unrelated symbols from the file are excluded.

---

### Dependency Filtering

Given many includes/imports, verify that only directly relevant or changed dependencies are included.

---

### Python

Use the Python test project from Phase 2.

Create or reuse a file containing:

```text
multiple functions
multiple imports
one changed function
```

Verify that rule extraction receives:

```text
expanded changed block
containing function
directly relevant symbols/imports
```

but not the complete module symbol list.

---

### C/C++ Regression

Verify that C/C++ rule extraction still receives enough structural context to produce results for existing simple test cases.

---

### C# and Rust Smoke Tests

If test fixtures already exist, verify that compact context can be produced without exceptions.

Do not require sophisticated language-specific semantic resolution in this phase.

---

## Comparison Diagnostics

If practical, retain an internal test/debug mechanism that can compare:

```text
Old input:
expanded diff + full AST

New input:
expanded diff + compact relevant context
```

Compare at least:

```text
serialized input size
SubAgent response validity
basic rule quality
```

Do not build a large benchmark framework for this phase.

A small set of representative fixtures is sufficient.

---

## Failure Behavior

Context-selection failure must not break MR processing.

If semantic context generation fails:

```text
Expanded Diff
    |
    v
RuleExtractionSubAgent
```

should still be allowed to run with minimal context when possible.

Preferred degradation:

```text
expanded diff + semantic context
        ↓ failure
expanded diff only
```

Do not fall back to:

```text
expanded diff + full AST
```

because that would defeat the conservative input policy.

---

## Non-Goals

Do not implement the following in Phase 3:

* final conservative rule-extraction prompt
* `UNKNOWN` support
* confidence scoring
* evidence fields
* new JSON schema
* rule candidate lifecycle
* rule clustering redesign
* project-policy promotion
* multi-agent validation
* model benchmarking
* per-language models
* deeper than necessary call-graph traversal
* repository-wide semantic analysis
* full Python type inference
* full C++ semantic resolution
* main review-agent changes

Keep the phase focused on context reduction.

---

## Expected Architecture

After Phase 3:

```text
Changed File
    |
    v
Full AST / Structural Analysis
    |
    +----------------------+
    |                      |
    |                 internal use
    |
    v
Context Selector
    |
    +-- expanded diff
    +-- containing scope
    +-- changed AST information
    +-- direct symbols
    +-- relevant changed dependencies
    |
    v
RuleExtractionContext
    |
    v
RuleExtractionSubAgent
    |
    v
Sub LLM
```

The SubAgent should no longer receive a complete file-level AST dump by default.

---

## Acceptance Criteria

Phase 3 is complete when all of the following are true:

1. Expanded diff is the primary rule-extraction input.

2. Full-file AST data is no longer passed directly to the SubAgent by default.

3. Full symbol lists are no longer passed to the SubAgent by default.

4. Full include/import/dependency lists are no longer passed to the SubAgent by default.

5. The application selects relevant context before invoking the SubAgent.

6. Context includes the changed structural area and directly related symbols when available.

7. Context selection uses a conservative one-hop strategy.

8. Missing semantic information does not cause fallback to full-AST input.

9. C, C++, C#, Python, and Rust use the same conceptual context-selection pipeline.

10. Existing expanded diff behavior remains unchanged.

11. The rule JSON schema remains unchanged.

12. The Phase 1 SubAgent routing remains unchanged.

13. The Phase 2 language detection remains unchanged.

14. Python tests demonstrate that unrelated module symbols and imports are excluded.

15. Existing C/C++ extraction continues to function.

16. Context-selection failures degrade to expanded-diff-only extraction rather than failing the MR task.

Keep the implementation conservative.

This rule-learning system is an auxiliary code-review feature. It does not need complete semantic understanding of every change. Prefer small, relevant, explainable input over broad context that may mislead the SubAgent.
