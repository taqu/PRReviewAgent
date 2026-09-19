# Phase 1 — Optimize Turn 1 Prompt for Non-Thinking Review

## Objective

Improve the review quality of the existing **Turn 1 with thinking disabled** without changing the current review pipeline architecture.

The current two-turn pipeline must remain unchanged.

```text
Turn 1: Detection / Evidence Construction
        thinking OFF
            ↓
Turn 2: Validation / Selection / Formatting
```

The goal of this phase is to make the non-thinking Turn 1 behave closer to the existing thinking-enabled Turn 1 by providing clearer review heuristics and comparison instructions.

Do **not** introduce a new verification turn or change the existing Turn 2 behavior in this phase.

---

## Background

The existing thinking-enabled Turn 1 currently provides the best review quality.

In the benchmark case, it detected all three intentionally introduced bugs:

```text
1. Width/height mismatch in Environment::sample
2. Reversed cross-product operands in traceMesh
3. Width/height mismatch in renderSphere
```

The existing thinking-disabled Turn 1 detected two of the three issues.

The previous attempt to simplify Turn 1 into lightweight candidate discovery reduced review quality significantly.

Therefore, this phase must preserve the original Turn 1 responsibilities.

---

# Requirements

## 1. Keep the Existing Two-Turn Architecture

Do not change the execution pipeline.

The architecture must remain:

```text
BuildTurn1()
    ↓
IssuesResponse
    ↓
BuildTurn2()
    ↓
Final review
```

Do not add:

```text
Verification Turn
CandidateResponse
VerifiedIssue
BuildTurn3()
```

Those changes are explicitly out of scope for this phase.

---

## 2. Preserve the Existing Turn 1 Output Structure

Turn 1 must continue producing complete review candidates.

Each issue should continue to contain:

```text
location
problem
evidence
impact
suggested_fix
confidence
```

Example:

```json
{
  "issues": [
    {
      "location": "renderer.cpp: Environment::sample",
      "problem": "Incorrect column index derivation using height instead of width",
      "evidence": "The column index is derived using SampleHeight although the row-major layout uses SampleWidth as the horizontal dimension.",
      "impact": "Environment sampling may be incorrect when width and height differ.",
      "suggested_fix": "Use SampleWidth when deriving the column index.",
      "confidence": "high"
    }
  ]
}
```

Do not replace this structure with a lightweight candidate format such as:

```text
location
category
hypothesis
trigger
verify_symbols
```

The model should still be required to reason far enough to explain why the suspected issue is actually a bug.

---

# 3. Treat Evidence Construction as Part of Detection

The prompt must explicitly instruct the model that discovering an issue is not enough.

For every reported issue, the model should attempt to establish:

```text
What changed?
Why is it incorrect?
Under what condition does it matter?
What behavior does it affect?
What concrete code change would correct it?
```

The model should use the process of constructing evidence and a fix as a way to validate its own suspicion.

Do not encourage speculative candidates that cannot be supported by the visible code.

---

# 4. Add Explicit Comparison Heuristics

Non-thinking models should not be expected to independently discover all useful comparison strategies.

Add explicit instructions to actively compare changed code against nearby and related code.

The prompt should instruct the model to check, when applicable:

```text
- width vs height
- row vs column
- source vs destination
- lhs vs rhs
- old behavior vs new behavior
- changed implementation vs sibling implementations
- initialization vs cleanup
- acquire vs release
- add vs remove
- increment vs decrement
- caller expectations vs callee behavior
```

These are review heuristics, not mandatory bug patterns.

The model must only report an issue when the code provides sufficient evidence.

---

# 5. Add Special Attention for Swapped or Reversed Values

Add an explicit review rule for accidental substitutions and operand reversals.

The model should closely inspect changed expressions for:

```text
- width / height swaps
- row / column swaps
- x / y / z swaps
- source / destination swaps
- begin / end swaps
- min / max swaps
- numerator / denominator swaps
- reversed function arguments
- reversed subtraction operands
- reversed cross-product operands
- sign inversion
- comparison direction changes
```

Example:

```cpp
cross(normal, tangent)
```

changing to:

```cpp
cross(tangent, normal)
```

must not be treated as equivalent merely because the same symbols remain present.

The model should consider whether operand order is semantically significant.

---

# 6. Pay Special Attention to Dimension and Index Arithmetic

For changed indexing or dimension-related expressions, explicitly check consistency.

Examples include:

```cpp
row * width + column
index % width
index / width
buffer[y * stride + x]
```

The model should inspect whether:

```text
- row stride uses the horizontal dimension
- modulo uses the correct dimension
- division and modulo use consistent dimensions
- width and height are not accidentally interchanged
- sibling code uses a different convention
```

The model should use nearby implementations as supporting evidence when available.

For example:

```cpp
renderSphere:
row * settings.height + column
```

versus:

```cpp
renderMesh:
row * settings.width + column
```

is a meaningful inconsistency and should be investigated.

---

# 7. Inspect Order-Sensitive Operations

Explicitly instruct the model to recognize that some operations are not commutative.

Pay particular attention to changes involving:

```text
cross(a, b)
subtract(a, b)
divide(a, b)
matrix multiplication
quaternion multiplication
comparisons
range boundaries
ordered arguments to APIs
```

For these expressions:

```text
f(a, b)
```

and:

```text
f(b, a)
```

must not be assumed equivalent.

The model should determine whether reversing operands changes semantics.

---

# 8. Compare Against Sibling Implementations

When multiple functions implement similar behavior, use them as consistency references.

Examples:

```text
renderSphere vs renderMesh
loadTexture vs loadEnvironment
createResource vs recreateResource
encodeFoo vs encodeBar
```

The model should look for differences involving:

```text
indexing
resource handling
bounds checks
error handling
argument order
initialization
cleanup
```

A sibling implementation is supporting evidence, not absolute proof.

The model must still explain why the differing behavior is incorrect.

---

# 9. Prefer Concrete Changed-Code Evidence

Prioritize issues directly supported by changed code.

Good:

```text
The changed expression now uses SampleHeight for the column modulo,
while the same function later normalizes the column using SampleWidth.
```

Weak:

```text
The sampler state might become inconsistent depending on how callers use it.
```

Avoid reporting speculative issues that require assumptions not established by the available code.

---

# 10. Confidence Is Mandatory

Every issue must include:

```json
"confidence": "high"
```

Allowed values:

```text
high
medium
low
```

Use the following interpretation.

### High

The changed code itself, or a direct comparison with visible related code, establishes the problem.

Example:

```text
row * height
```

where the image row stride clearly uses width elsewhere.

### Medium

The issue is strongly suggested, but some semantic assumption is required.

### Low

The issue depends on missing context or speculative assumptions.

Low-confidence speculative findings should generally not be emitted unless there is concrete evidence that makes them useful for Turn 2 validation.

---

# 11. Do Not Increase Candidate Volume Aggressively

The objective is not to produce more candidates.

The objective is:

```text
higher recall
without increasing false positives
```

Prefer a small number of well-supported findings over many weak suspicions.

Turn 1 should not emit an issue merely because a changed expression looks unusual.

---

# 12. Keep Suggested Fixes Minimal

`suggested_fix` remains required because producing a plausible correction helps validate the issue.

However, the fix should be minimal.

Good:

```text
Change `index % SampleHeight` to `index % SampleWidth`.
```

Avoid:

```text
Refactor the entire sampling subsystem and introduce a new abstraction.
```

The suggested fix should directly address the identified defect.

---

# 13. No AST Pipeline Changes in This Phase

Do not add:

```text
AST-selected verification context
candidate-driven context expansion
semantic grouping
new context resolver
```

Do not remove any existing AST/context behavior unless required for the prompt change itself.

AST/context improvements belong to later phases.

---

# 14. Do Not Modify Turn 2

Turn 2 should remain unchanged during this phase.

This is important for benchmark isolation.

The experiment must answer:

```text
Does improving the Turn 1 prompt alone improve thinking-off recall?
```

Changing both turns at once would make the result difficult to interpret.

---

# Implementation Scope

The expected implementation should primarily modify:

```text
review1.en.md
```

Only make supporting code changes if they are required to preserve or enforce the Turn 1 output schema.

Do not perform unrelated refactoring.

---

# Benchmark

After implementation, run the same benchmark used for the current baseline.

Compare:

```text
A. Old Turn 1 — thinking ON
B. Old Turn 1 — thinking OFF
C. Phase 1 Turn 1 — thinking OFF
```

Record at minimum:

```text
true positives
false positives
false negatives
Turn 1 latency
Turn 1 input tokens
Turn 1 output tokens
total review latency
```

For the existing three-bug benchmark, the target is:

```text
Environment::sample    detected
traceMesh              detected
renderSphere           detected

false positives        0
```

The important regression case is the reversed bitangent calculation:

```cpp
cross(T, normal)
```

The Phase 1 prompt should increase the likelihood that the non-thinking model identifies this operand-order change.

---

# Acceptance Criteria

Phase 1 is successful if:

```text
1. The existing two-turn architecture is unchanged.

2. Turn 1 still outputs:
   location
   problem
   evidence
   impact
   suggested_fix
   confidence

3. Turn 2 behavior is unchanged.

4. The thinking-off benchmark does not regress on the two bugs
   already detected by the previous thinking-off baseline.

5. The thinking-off model detects the previously missed
   order-sensitive bug more reliably.

6. False-positive count does not increase materially.

7. Latency remains substantially below the thinking-enabled baseline.
```

---

# Non-Goals

Do not implement any of the following in Phase 1:

```text
three-turn review pipeline
verification-specific LLM turn
CandidateIssue / VerifiedIssue split
AST context selector
full-file context redesign
semantic grouping
candidate-driven context expansion
dynamic context retrieval
major Turn 2 redesign
```

These should only be considered after the Phase 1 benchmark is complete.

---

# Design Principle

The central principle of this phase is:

```text
Do not replace thinking with more LLM stages.

Instead, make the important review comparisons explicit enough
that a non-thinking model can perform the same analysis directly.
```

The existing thinking-enabled prompt remains the quality baseline.

Phase 1 should preserve the reasoning structure that already works while making the critical comparison steps less dependent on hidden model reasoning.
