# Phase 5 — Validate and Harden the SLM Rule Extraction Pipeline

## Goal

Validate the rule-extraction pipeline implemented in Phases 1–4 against realistic code changes.

Do not add major new architecture or rule-learning features in this phase.

The objective is to verify that the current design is good enough as an auxiliary feature for the main code-review system.

The system does not need perfect rule extraction.

The required standard is:

```text
- clearly useful changes should often produce reasonable rules
- ambiguous changes should safely produce UNKNOWN
- unrelated AST/context should not strongly influence the result
- failures must not affect the main code-review pipeline
```

Prefer stability and low false-positive behavior over aggressive extraction.

---

## Scope

Validate the complete pipeline:

```text
Changed File
    |
    v
Source Language Detection
    |
    v
Expanded Diff
    +
Compact Relevant Context
    |
    v
RuleExtractionSubAgent
    |
    v
Sub LLM
    |
    v
Conservative Rule Extraction
    |
    +-- Valid Rule -> existing persistence path
    |
    +-- UNKNOWN -> discard
    |
    +-- Invalid Output -> discard
```

Do not redesign this pipeline unless testing exposes a clear defect.

---

## Preserve Model Separation

The architecture from Phase 1 must remain unchanged.

Main code review:

```text
Main LLM
http://192.168.128.152:9090
```

Rule extraction:

```text
Sub LLM
http://192.168.128.152:9080
```

Verify that rule extraction never falls back to the main LLM.

A Sub LLM failure must only disable or skip the affected rule-extraction operation.

---

## Primary Validation Target

Use the prepared Python test project as the main end-to-end validation target.

Python is useful here because it verifies that the pipeline is no longer dependent on C/C++ assumptions.

Test realistic small commits rather than artificial prompt-only inputs where possible.

Recommended categories are described below.

---

## Test Category 1 — Clear Reusable Rule

Create changes where a reusable engineering rule is clearly demonstrated.

Examples include:

```python
# Before
user.save()

# After
if user is not None:
    user.save()
```

Expected result:

A rule related to checking an optional value before using it is acceptable.

Exact wording is not important.

The result should be:

```text
specific
reasonable
supported by the change
reusable
```

---

## Test Category 2 — Resource Management

Example:

```python
# Before
f = open(path)
data = f.read()
f.close()
```

```python
# After
with open(path) as f:
    data = f.read()
```

Expected result:

A rule about using deterministic/context-managed resource cleanup is acceptable.

The model should not invent unrelated project policies.

---

## Test Category 3 — Error Handling

Example:

```python
# Before
value = int(text)
```

```python
# After
try:
    value = int(text)
except ValueError:
    value = default_value
```

Expected result:

A rule about handling expected conversion failure is acceptable.

Do not require exact wording.

---

## Test Category 4 — Non-Rule Change

Use changes that should not normally become project engineering rules.

Example:

```python
# Before
title = "foo"
```

```python
# After
title = "bar"
```

Expected result:

```text
UNKNOWN
```

or rejection by the existing conservative validator.

The system must not invent rules such as:

```text
Use "bar" instead of "foo".
```

or:

```text
Prefer descriptive titles.
```

unless such a conclusion is actually supported by the change.

---

## Test Category 5 — Refactoring Without a Clear Policy

Use a simple local refactoring that does not clearly establish a reusable engineering rule.

Examples:

* renaming a local variable
* moving a statement without changing semantics
* formatting-only changes
* trivial literal changes
* comment changes

Expected behavior:

Prefer UNKNOWN.

These cases are important because the system should not learn from every merged change.

---

## Test Category 6 — Context Noise

Construct a source file containing:

```text
many unrelated imports
many unrelated functions
many unrelated classes or symbols
one small changed function
```

Verify that the SubAgent receives only the compact context selected in Phase 3.

The resulting rule must be based on the actual changed code.

Unrelated symbols or imports must not become the basis of the extracted rule.

---

## Test Category 7 — Missing Semantic Information

Create or identify a case where AST/symbol resolution cannot provide useful semantic context.

Expected behavior:

```text
Expanded Diff
    |
    v
RuleExtractionSubAgent
```

should still work.

The system may return either:

```text
a reasonable rule
```

or:

```text
UNKNOWN
```

Both are acceptable.

Do not fall back to sending the complete AST.

---

## Test Category 8 — Multiple Changes in One File

Test a file containing more than one changed region.

Observe whether each existing extraction unit remains understandable to the SubAgent.

Do not introduce sophisticated change clustering in this phase.

If the current pipeline produces an ambiguous extraction unit, UNKNOWN is acceptable.

Document the limitation rather than introducing a large redesign.

---

## Cross-Language Regression

Run lightweight tests for all supported language groups:

```text
C
C++
C#
Python
Rust
```

The tests do not need equivalent semantic depth for every language.

At minimum verify:

```text
language is detected correctly
prompt contains the correct language
compact context is generated
SubAgent is invoked
valid JSON can be parsed
UNKNOWN works
main LLM is not used
```

---

## C and C++ Regression

Keep existing C/C++ behavior functional.

Use a few simple cases such as:

```text
null/pointer guard
resource cleanup
bounds/error check
const-correctness change
```

Do not attempt to validate advanced C++ semantics comprehensively.

This feature does not need to replace the main 26B reviewer.

---

## C# Smoke Tests

Use simple cases such as:

```text
null check
using/IDisposable
async/await correction
exception handling
```

Verify that C# is not interpreted using C++ terminology.

---

## Rust Smoke Tests

Use simple cases such as:

```text
Option handling
Result handling
ownership/borrowing-related correction
resource lifetime improvement
```

Verify that Rust input does not trigger C/C++-specific rule wording.

---

## Evaluate Rule Quality Conservatively

For each extracted rule, classify it manually or in tests using a small set of categories.

Recommended categories:

```text
GOOD
ACCEPTABLE
UNKNOWN
UNSUPPORTED
GENERIC
```

Definitions:

### GOOD

The rule clearly captures the semantic lesson of the change and is reusable.

### ACCEPTABLE

The rule is not perfect, but it is directionally correct, supported by the change, and unlikely to harm future reviews.

### UNKNOWN

No useful rule was extracted.

This is not a failure for ambiguous changes.

### UNSUPPORTED

The rule contains a conclusion that is not supported by the provided change or context.

This is the most important failure category.

### GENERIC

The rule is technically harmless but too vague to be useful.

Examples:

```text
Handle errors properly.
Use safer code.
Follow best practices.
```

---

## Success Priority

Use the following priority when judging the system:

```text
1. Minimize UNSUPPORTED rules
2. Avoid obviously GENERIC rules
3. Preserve reasonable GOOD/ACCEPTABLE extraction
4. Extraction coverage is secondary
```

Do not optimize for extracting the maximum number of rules.

A high UNKNOWN rate is acceptable if the remaining learned rules are reliable.

---

## Simple Metrics

Add or use lightweight diagnostics to measure:

```text
total extraction attempts
valid rule count
UNKNOWN count
invalid response count
validator rejection count
SubAgent failure count
```

If practical during test runs, also record manual classifications:

```text
GOOD
ACCEPTABLE
UNSUPPORTED
GENERIC
```

Do not build a production analytics system in this phase.

Simple test output or development logging is sufficient.

---

## Input Size Diagnostics

Since Phase 3 intentionally reduced context, record enough information during validation to verify that reduction is working.

Useful values include:

```text
expanded diff length
compact structural-context length
number of selected symbols
number of selected dependencies
total prompt length
```

Do not log complete source code in normal application logs.

Development/test-only diagnostic output is acceptable.

---

## Compare Compact Context Against Previous Behavior

If the previous full-AST extraction path is still easy to invoke in tests, perform a small offline comparison.

For a representative set of changes compare:

```text
Old:
Expanded Diff + Full AST/Dependencies

New:
Expanded Diff + Compact Relevant Context
```

Check:

```text
input size
response validity
rule quality
unsupported-rule behavior
```

This comparison is optional.

Do not retain a permanent dual production path solely for benchmarking.

The production path should remain the compact Phase 3 design.

---

## Do Not Benchmark for Maximum Model Quality

Do not turn Phase 5 into a large model benchmark.

The SubAgent is intentionally an SLM/offloaded model.

Do not require it to match the main review model.

A result is good enough when:

```text
the rule is directionally correct
the rule is supported by the change
the rule is not misleading
```

The main 26B model remains responsible for actual code-review judgment.

---

## Failure Isolation Tests

Explicitly verify the following cases:

### Sub LLM Unavailable

Stop or make the Sub LLM endpoint unreachable.

Expected:

```text
rule extraction fails/skips
main application continues
main reviewer remains usable
```

No fallback to the main LLM is allowed.

---

### Timeout

Simulate or force a SubAgent timeout.

Expected:

```text
timeout is logged
rule extraction is skipped
MR processing continues
```

---

### Invalid JSON

Return malformed output from a mocked SubAgent.

Expected:

```text
no database write
no embedding generation
no MR failure
```

---

### Empty Response

Expected:

```text
skip extraction safely
```

---

### UNKNOWN

Expected:

```text
valid no-rule result
no embedding
no persistence
```

---

## Persistence Regression

For a valid extracted rule, verify the existing downstream flow still works:

```text
RuleExtractionResult
    |
    v
LearnedRule
    |
    v
Embedding
    |
    v
RuleRepository
```

Do not change the database schema.

Do not redesign existing lifecycle behavior.

---

## Prompt Inspection

Inspect generated prompts for representative:

```text
C++
Python
C#
Rust
```

cases.

Verify that:

```text
the correct programming language is explicit
expanded changed code appears before auxiliary context
full AST dumps are absent
unrelated symbol lists are absent
the prompt asks for the smallest supported reusable rule
the prompt allows UNKNOWN
the prompt prohibits unsupported project-policy inference
```

---

## Fix Only Clear Problems

During validation, make small corrections when a clear issue is found.

Acceptable examples:

```text
incorrect language mapping
obvious prompt-format bug
unbounded context list
UNKNOWN accidentally persisted
SubAgent routing bug
full AST accidentally still included
generic validator false implementation bug
```

Do not respond to isolated imperfect model output with major architectural changes.

The SLM is not expected to produce ideal wording for every change.

---

## Avoid Overfitting the Prompt

Do not keep adding special-case instructions for every failed test.

For example, avoid accumulating rules such as:

```text
If Python uses X, always say Y.
If Rust uses Z, never say Q.
```

unless there is a broad, repeatable issue.

The prompt should remain short and general.

---

## No Production Model Fallback

Do not add logic such as:

```text
E4B returns UNKNOWN
    |
    v
send the same extraction to 26B
```

UNKNOWN is an acceptable final result.

The goal of offloading would be defeated if difficult rule extraction automatically escalated to the main reviewer.

---

## Recommended Acceptance Threshold

This phase does not require a strict statistical benchmark.

For a modest representative test set, the implementation is acceptable when:

```text
- clear changes usually produce GOOD or ACCEPTABLE rules
- ambiguous/trivial changes commonly produce UNKNOWN
- unsupported rules are uncommon
- no severe cross-language confusion is observed
- SubAgent failures remain isolated
```

If a few weak rules remain but they are not dangerously misleading, do not over-engineer the system.

---

## Non-Goals

Do not implement the following in Phase 5:

* new model routing architecture
* fallback to the 26B model
* automatic model selection
* rule candidate lifecycle redesign
* multi-MR rule promotion
* new database schema
* model-generated confidence
* evidence arrays
* another validation LLM
* agent voting
* repository-wide reasoning
* deeper AST traversal
* full Python type analysis
* full C++ semantic analysis
* per-language SubAgents
* per-language models
* production analytics infrastructure
* large-scale benchmark tooling

Phase 5 is a validation and hardening phase.

---

## Expected Final Architecture

After Phase 5, the project-adaptation path should remain simple:

```text
Merged Change
      |
      v
Language Detection
      |
      v
Full Local AST Analysis
      |
      v
Conservative Context Selection
      |
      +-- Expanded Diff
      +-- Changed Structure
      +-- Directly Relevant Symbols
      +-- Relevant Dependency Changes
      |
      v
RuleExtractionSubAgent
      |
      v
Sub LLM
      |
      +-- Supported Rule
      |       |
      |       v
      |    Validate
      |       |
      |       v
      |    Persist
      |
      +-- UNKNOWN
      |       |
      |       v
      |    Discard
      |
      +-- Invalid/Failure
              |
              v
           Discard
```

The main review model remains separate:

```text
Code Review
    |
    v
Main LLM
```

---

## Acceptance Criteria

Phase 5 is complete when all of the following are true:

1. The complete Phase 1–4 rule-extraction pipeline works end-to-end.

2. Python has been validated using the prepared test project.

3. C and C++ existing behavior has been regression-tested.

4. C# and Rust have at least basic smoke-test coverage.

5. Clear reusable changes can produce reasonable learned rules.

6. Trivial or ambiguous changes can safely produce UNKNOWN.

7. Full AST and unrelated file-level context are not accidentally sent to the SubAgent.

8. Rule extraction always uses the configured Sub LLM.

9. The main review LLM is never used as a rule-extraction fallback.

10. Sub LLM connection failure, timeout, empty output, invalid JSON, and UNKNOWN are all handled safely.

11. Unsupported or clearly generic rules are uncommon in the representative test set.

12. Valid rules continue through the existing embedding and persistence path.

13. Rule extraction failures cannot break MR processing.

14. No major new rule-learning architecture has been introduced solely to improve benchmark performance.

15. The implementation is considered acceptable even if some useful rules are missed.

The final objective is not perfect automatic policy discovery.

The objective is a lightweight, conservative project-adaptation feature that provides some useful project-specific knowledge without adding significant risk or cost to the main code-review system.
