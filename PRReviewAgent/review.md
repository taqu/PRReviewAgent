# Phase 3 — Add a Dedicated Non-Thinking Verification Turn

## Objective

Introduce a dedicated **Verification Turn** between Candidate Discovery and Finalization.

The review pipeline should move from:

```text
Turn 1: Candidate Discovery
    ↓
Turn 2: Final Selection / Formatting
```

to:

```text
Turn 1: Candidate Discovery
    ↓
AST-driven Verification Context Resolution
    ↓
Turn 2: Candidate Verification
    ↓
Turn 3: Finalization
```

All three LLM turns should be able to run without thinking/reasoning mode.

The purpose of this phase is not to increase the amount of reasoning performed by a single model invocation.

Instead, split the review task into narrow stages that are individually simple and deterministic enough for non-thinking inference.

---

# Prerequisites

Phase 3 assumes the previous phases have established:

1. Turn 1 as a lightweight Candidate Discovery stage.
2. A candidate representation containing concepts such as:

```text
candidate_id
location
category
hypothesis
trigger
verify_symbols
```

3. A deterministic AST-backed component similar to:

```text
VerificationContextResolver
```

4. A structured output similar to:

```text
VerificationContext
```

containing candidate-specific source code.

5. A formatter capable of converting that context into compact LLM-readable text.

Do not reimplement those components unless integration requires small fixes.

---

# Target Architecture

The review execution should become:

```text
Merge Request
    ↓
Changed files
    ↓
AST / semantic analysis
    ↓
File grouping
    ↓
Turn 1: Candidate Discovery
    ↓
CandidateIssue[]
    ↓
VerificationContextResolver
    ↓
VerificationContext[]
    ↓
Turn 2: Verification
    ↓
VerifiedIssue[]
    ↓
Turn 3: Finalization
    ↓
Final GitLab review
```

Responsibilities must remain clearly separated.

---

# Turn Responsibilities

## Turn 1 — Candidate Discovery

Turn 1 remains responsible only for identifying suspicious changed code.

It should answer:

> What should be checked?

Do not move verification logic back into Turn 1.

---

## Turn 2 — Verification

Turn 2 answers:

> Does this candidate actually represent a sufficiently supported issue?

It should inspect the candidate together with targeted source context selected by the application.

Turn 2 must not search broadly for additional issues.

It must only verify candidates discovered by Turn 1.

---

## Turn 3 — Finalization

Turn 3 answers:

> Which verified issues should be reported, at what severity, and how should they be presented?

Turn 3 applies:

* project-specific reporting policy;
* final filtering;
* deduplication;
* severity assignment;
* language selection;
* final formatting.

Turn 3 should not inspect source code.

---

# Template Layout

Change the template structure from approximately:

```text
Templates
├── review1.en.md
├── review2.en.md
└── review2.ja.md
```

to:

```text
Templates
├── review1.en.md
├── review2.en.md
├── review3.en.md
└── review3.ja.md
```

Responsibilities:

```text
review1.en.md
    Candidate Discovery

review2.en.md
    Candidate Verification

review3.en.md
    Finalization in English

review3.ja.md
    Finalization in Japanese
```

The internal reasoning turns should remain English-only.

Only the finalization template needs language-specific variants.

---

# New `review2.en.md`

Create a new verification prompt.

Its role must be narrowly defined.

The model receives:

```text
Candidate
+
Candidate-specific verification context
```

and decides whether the candidate is valid.

The prompt should explicitly prohibit broad issue discovery.

---

# Verification Prompt Goal

The goal should be approximately:

> Determine whether the supplied candidate is supported by the supplied source context.

The model must decide:

1. Whether the candidate is valid.
2. What concrete source evidence supports or rejects it.
3. Under what execution or state conditions it occurs.
4. What practical consequence follows.
5. Whether the candidate should proceed to Finalization.

Do not assign Critical / Major / Minor severity.

Do not apply final project-specific reporting policy.

Do not generate final review prose.

---

# Verification Input

Each verification request should contain one candidate or a small bounded batch of closely related candidates.

Preferred input structure:

```text
[Candidate]

ID: c0
Location: src/foo.cpp: Foo::Open
Category: lifetime
Hypothesis:
Foo::Open may expose an object whose lifetime is shorter than the returned pointer.

Changed-code trigger:
Foo::Open now returns resource_.get().

[Verification Context]

[Changed Scope]
FILE: src/foo.cpp
SYMBOL: Foo::Open

<source>

[Containing Type]
FILE: include/foo.h
SYMBOL: Foo

<source>

[Field]
FILE: include/foo.h
SYMBOL: Foo::resource_

<source>

[Direct Caller]
FILE: src/worker.cpp
SYMBOL: Worker::Run

<source>
```

Avoid embedding raw AST JSON unless unavoidable.

The model should reason primarily from source code.

---

# Verification Output

Introduce a dedicated structured response.

Preferred logical schema:

```json
{
  "issues": [
    {
      "candidate_id": "c0",
      "valid": true,
      "evidence": "Foo owns Resource through a unique_ptr stored in resource_. Foo::Open returns resource_.get(), while Worker::Run stores the returned raw pointer beyond the Foo lifetime.",
      "impact": "The stored pointer can become dangling after Foo is destroyed and may later be dereferenced.",
      "suggested_fix": "Preserve ownership or return a handle whose lifetime is guaranteed by the API contract.",
      "confidence": "high"
    }
  ]
}
```

The exact field names may follow existing project conventions.

Preserve at least:

```text
candidate_id
valid
evidence
impact
suggested_fix
confidence
```

---

# Invalid Candidate Output

When a candidate is not supported:

```json
{
  "issues": [
    {
      "candidate_id": "c0",
      "valid": false,
      "evidence": "The returned pointer is consumed synchronously and is not retained by any supplied caller.",
      "impact": "",
      "suggested_fix": "",
      "confidence": "high"
    }
  ]
}
```

A more compact rejected representation is acceptable if cleaner.

For example:

```json
{
  "candidate_id": "c0",
  "valid": false,
  "reason": "All supplied callers consume the pointer synchronously."
}
```

Choose one schema and use it consistently.

---

# VerifiedIssue Model

Introduce a separate model rather than overloading the Phase 1 candidate type.

Suggested concept:

```csharp
public sealed class VerifiedIssue
{
    public required string CandidateId { get; init; }

    public required bool Valid { get; init; }

    public required string Evidence { get; init; }

    public required string Impact { get; init; }

    public required string SuggestedFix { get; init; }

    public string? Confidence { get; init; }

    public string? RuleId { get; init; }
}
```

Reuse exact project naming conventions where appropriate.

The important design requirement is:

```text
CandidateIssue != VerifiedIssue
```

A discovered suspicion and a verified issue must remain distinct domain concepts.

---

# Verification Rules

The verification prompt should enforce the following rules.

## Use only supplied context

The model must not assume repository behavior outside the supplied source context.

If required evidence is absent, reject or mark the candidate unsupported.

Do not invent missing callers, contracts, ownership rules, or runtime behavior.

---

## Source code is authoritative

If the candidate hypothesis conflicts with the supplied source code:

```text
reject the candidate
```

Do not try to preserve the candidate merely because Turn 1 suggested it.

---

## Candidate identity is immutable

Turn 2 must preserve:

```text
candidate_id
```

exactly.

It must not invent new candidate IDs.

---

## No new issues

Turn 2 must not discover or return unrelated issues.

If the verification context reveals another unrelated defect, ignore it.

That issue was not discovered by Turn 1 and therefore belongs outside this verification request.

This rule is important for maintaining predictable pipeline semantics.

---

## Verify one root cause

Each candidate represents one root cause.

Do not split it into multiple issues during verification.

If the hypothesis contains multiple independent root causes because Turn 1 produced a poor candidate, reject it or validate only the explicitly represented root cause according to the selected implementation policy.

Do not silently create extra findings.

---

## Establish concrete conditions

Evidence should state relevant execution or state conditions.

Prefer:

```text
When Foo is destroyed before Worker::Run consumes the stored pointer, the pointer refers to the Resource formerly owned by Foo.
```

Avoid:

```text
This may potentially be unsafe.
```

---

## Reject unsupported hypotheses

Reject candidates when:

* required caller behavior is not established;
* ownership assumptions are unsupported;
* control flow contradicts the hypothesis;
* the changed code does not create or expose the alleged behavior;
* the issue depends only on theoretical misuse that violates the visible contract;
* AST-selected context disproves the candidate;
* supplied context is insufficient to establish a concrete issue.

Verification should reduce false positives.

---

# Confidence

Turn 2 may use:

```text
high
medium
```

Do not support low-confidence verified findings.

If confidence would be low, mark the candidate invalid.

The Verification stage should prefer:

```text
unsupported candidate
```

over:

```text
weak final finding
```

---

# Severity

Turn 2 must NOT assign:

```text
Critical
Major
Minor
```

Severity remains a Turn 3 responsibility.

This keeps verification focused on factual correctness.

---

# Project-Specific Review Policy

Do not apply final reporting policy during Verification unless that policy changes the factual validity of the candidate.

For example:

* assertion policy;
* preferred style;
* maintainability thresholds;
* reporting preferences;

should normally remain Turn 3 responsibilities.

Turn 2 asks:

> Is the issue real?

Turn 3 asks:

> Should we report it, and at what severity?

Keep that distinction explicit.

---

# BuildTurn2

Refactor or replace the current `PromptBuilder.BuildTurn2()`.

After Phase 3, it should build the Verification prompt.

Conceptually:

```csharp
string promptTurn2 = PromptBuilder.BuildTurn2(
    reviewRequest,
    candidate,
    verificationContext,
    stringBuilder);
```

Exact API shape may differ.

Do not pass all candidate groups and all source code into every verification call.

Use candidate-specific context.

---

# Candidate Execution Strategy

Initially prefer **one verification request per candidate**.

Conceptually:

```text
for each candidate
    resolve verification context
    run Turn 2
```

This gives:

* simpler prompts;
* easier attribution;
* clean candidate IDs;
* easier metrics;
* less cross-candidate interference;
* better failure isolation.

However, latency may become an issue when many candidates are produced.

Design the implementation so that bounded batching can be added later.

Do not implement complicated batching in this phase unless the codebase already makes it trivial.

---

# Parallel Verification

Do not introduce uncontrolled parallel LLM calls.

If the existing runtime has a safe bounded-concurrency abstraction, using limited candidate verification concurrency is acceptable.

Otherwise keep verification sequential in Phase 3.

Correctness and observability are more important than maximum throughput in the first implementation.

A later phase can optimize parallelism after metrics are available.

---

# Verification Context Resolution

For every candidate:

```text
CandidateIssue
    ↓
VerificationContextResolver
    ↓
VerificationContext
    ↓
BuildTurn2
```

If context resolution fails completely:

* log the failure;
* do not fabricate verification;
* treat the candidate as unverified/rejected;
* continue with other candidates.

A single broken candidate must not abort the entire file-group review.

---

# Context Truncation

If:

```text
VerificationContext.Truncated == true
```

the Verification prompt should be informed that supplied context may be incomplete.

However, do not encourage speculation.

The correct behavior is:

> If the supplied context is insufficient to verify the candidate, reject it rather than infer missing facts.

---

# Existing `review2.en.md`

The current `review2.en.md` already performs final validation, policy filtering, severity assignment, deduplication, and formatting.

Move or adapt this content into:

```text
review3.en.md
```

Do not simply duplicate it.

After migration:

```text
review2.en.md = Verification
review3.en.md = Finalization
```

---

# Japanese Template Migration

Rename or adapt:

```text
review2.ja.md
```

to:

```text
review3.ja.md
```

It should remain a finalization template.

Do not create:

```text
review2.ja.md
```

for Verification unless there is a concrete product requirement.

Verification should remain English-only to keep internal behavior consistent across output languages.

---

# Turn 3 Input

Turn 3 must receive only verified issues.

Conceptually:

```text
Verified issues:
- c0
- c3
- c7
```

Candidates with:

```text
valid = false
```

must not be included.

Turn 3 should not receive raw VerificationContext source code.

This keeps the final turn small and cheap.

---

# Turn 3 Responsibilities

Turn 3 should:

1. Recheck the recorded verified evidence for internal consistency.
2. Apply project-specific review policy.
3. Reject findings that are real but not appropriate to report.
4. Deduplicate findings with the same root cause.
5. Assign severity.
6. Format the final review.
7. Use the requested output language.

Turn 3 must not:

* inspect source code;
* retrieve more context;
* discover new issues;
* invent evidence;
* strengthen weak verification results.

---

# Turn 3 Prompt

Adapt the old final-selection prompt so that it says:

```text
You are given Verified Issues produced by a dedicated verification stage.
```

rather than:

```text
You are given Issue Candidates produced by a previous analysis stage.
```

The prompt should trust neither the severity nor final reporting decision because those have not yet been assigned.

But it may rely on the recorded factual evidence from Verification.

---

# Severity Assignment

Keep severity assignment only in Turn 3.

Suggested categories remain:

```text
Critical
Major
Minor
```

Preserve current project definitions unless there is an independent reason to modify them.

Do not change severity policy as part of Phase 3.

This phase should isolate architectural changes from review-policy changes.

---

# Learned Rule Attribution

Preserve rule attribution across all stages.

Conceptually:

```text
CandidateIssue.RuleId
    ↓
VerifiedIssue.RuleId
    ↓
Final finding attribution
```

A rejected candidate must not count as a produced final finding.

Maintain existing tracking semantics such as:

```text
ProducedCandidate
ProducedFinalFinding
```

Add a verification-level state if useful, for example:

```text
ProducedVerifiedIssue
```

or equivalent.

If adding a persistent field requires a large schema migration, defer that persistence change and record verification metrics through existing turn/execution records.

Do not break current learned-rule analytics.

---

# Execution Recorder

Extend review turn types from:

```text
Detection
Selection
```

to:

```text
Detection
Verification
Finalization
```

Rename `Selection` to `Finalization` if practical and migration-safe.

If persisted enum values or database compatibility make renaming risky, preserve the stored value temporarily but update semantic naming in new code where possible.

Do not perform unsafe migrations solely for naming cleanliness.

---

# Verification Metrics

Record for Turn 2:

```text
input tokens
output tokens
duration
candidate id
valid / invalid
context item count
context size
context truncated
```

At file-group or execution level also record:

```text
candidate count
verified count
rejected count
```

This phase should make it possible to answer:

> How many Turn 1 candidates survive factual verification?

---

# Finalization Metrics

Record:

```text
verified issue count
final selected count
Critical count
Major count
Minor count
Turn 3 latency
Turn 3 input tokens
Turn 3 output tokens
```

Preserve existing final finding metrics.

---

# Candidate / Rule Tracking

Current semantics should become:

```text
Turn 1 candidate
    → ProducedCandidate

Turn 2 valid candidate
    → Verified

Turn 3 reported finding
    → ProducedFinalFinding
```

Where persistence supports it, track all three.

At minimum preserve existing candidate and final-finding tracking.

---

# Failure Isolation

Each verification candidate should be independently recoverable.

If one candidate verification call throws:

```text
log failure
mark candidate unverified
continue
```

Do not abort the entire file group unless infrastructure-level failure makes further processing impossible.

Similarly:

```text
Candidate c0 verification fails
Candidate c1 succeeds
Candidate c2 succeeds
```

should still allow c1 and c2 to reach Turn 3.

---

# No Candidate Case

If Turn 1 returns no candidates:

```text
skip Turn 2
skip Turn 3
```

and preserve existing "No issues found" / no-review behavior.

Do not invoke empty LLM turns.

---

# No Verified Issue Case

If Turn 1 finds candidates but Turn 2 rejects all of them:

```text
skip Turn 3
```

Return the existing no-issues result.

Do not invoke Finalization merely to produce:

```text
No issues found
```

unless existing output architecture strictly requires it.

Prefer deterministic application-side handling.

---

# Prompt Builder Structure

The prompt builder should clearly expose:

```text
BuildTurn1(...)
BuildTurn2(...)
BuildTurn3(...)
```

Responsibilities:

```text
BuildTurn1:
Candidate Discovery

BuildTurn2:
Candidate + VerificationContext

BuildTurn3:
Verified issues + learned/project policy
```

Avoid one generic builder containing stage-specific branches where possible.

Keep stage contracts explicit.

---

# Review Request Model

Reassess whether fields such as:

```text
ReviewRulesTurn1
ReviewRulesTurn2
```

should become:

```text
ReviewRulesTurn1
ReviewRulesTurn2
ReviewRulesTurn3
```

or clearer names such as:

```text
DetectionTemplate
VerificationTemplate
FinalizationTemplate
```

Prefer semantic names if changing them does not cause excessive churn.

Do not prioritize naming refactors over functional integration.

---

# Finalization Language

Only Turn 3 should depend on the selected review language.

Example:

```text
English output:
review3.en.md

Japanese output:
review3.ja.md
```

Turn 1 and Turn 2 remain:

```text
English internal representation
```

This helps keep candidate and verification schemas language-independent.

---

# JSON Parsing

Turn 1 and Turn 2 should both use structured JSON output.

Turn 3 may continue producing final Markdown/text.

Use the existing typed JSON execution path where available, for example conceptually:

```text
RunJsonWithUsageAsync<T>()
```

Do not parse Verification output through fragile free-form text processing.

---

# Verification Prompt Constraints

The new `review2.en.md` should explicitly state:

```text
Output JSON only.
Do not assign severity.
Do not write the final review.
Do not discover new issues.
Do not inspect anything outside the supplied context.
Reject candidates that cannot be established from the supplied context.
```

Keep the prompt shorter than the old combined validation/finalization prompt.

The narrow role should reduce inference latency and improve consistency.

---

# Suggested `review2.en.md` Structure

Use approximately:

```text
Role
Goal
Input Authority
Verification Rules
Evidence Requirements
Rejection Rules
Output Schema
Output Constraints
Candidate
Verification Context
```

Avoid large sections about:

```text
final formatting
severity policy
project reporting policy
deduplication across unrelated candidates
```

Those belong to Turn 3.

---

# Turn 3 Deduplication

Deduplication belongs in Turn 3 because independently verified candidates can still represent the same root cause.

Example:

```text
c0: declaration contract mismatch
c3: implementation behavior caused by the same mismatch
```

Turn 3 may merge them.

Do not merge independent root causes merely because they occur in the same function.

---

# Existing File Grouping

Do not change grouping in Phase 3.

Preserve the existing grouping behavior.

The purpose of this phase is to measure the impact of adding Verification.

Do not mix semantic grouping changes into the experiment.

---

# AST Changes

Do not expand AST capabilities beyond what the Verification Context Resolver needs.

Do not reintroduce broad AST JSON into Turn 2.

Target:

```text
AST / semantic index
    ↓
resolver
    ↓
source code
    ↓
Turn 2
```

not:

```text
AST JSON
    ↓
Turn 2
```

---

# RAG / Learned Rules

Preserve existing rule retrieval behavior in Phase 3.

Do not redesign the rule-search query.

Candidate Discovery may continue to receive learned rules according to existing behavior.

Finalization should continue applying project-specific review policy.

Verification should remain primarily factual.

If a learned rule encodes a factual invariant required to validate a candidate, pass it only if the existing architecture already makes that information available cleanly.

Do not create a second RAG pipeline for Verification in this phase.

---

# Performance Considerations

The purpose of the 3-turn design is to replace one expensive thinking-heavy stage with multiple short non-thinking stages.

Do not allow the new Verification stage to become another broad reasoning prompt.

Keep:

```text
one candidate
+
small targeted source context
+
one verification decision
```

as the default unit of work.

The expected architecture is:

```text
cheap broad scan
    ↓
small targeted checks
    ↓
tiny final policy/formatting pass
```

---

# Benchmark

Compare at least:

```text
Baseline:
Thinking Turn 1
+ existing Turn 2

New:
Non-thinking Turn 1
+ non-thinking Verification
+ non-thinking Finalization
```

Measure:

```text
total review latency
Turn 1 latency
Turn 2 verification latency
Turn 3 latency
total input tokens
total output tokens
candidate count
verified count
final finding count
```

Also measure:

```text
candidate → verified survival rate
verified → final survival rate
```

---

# Quality Evaluation

Use representative real C/C++ merge requests.

Manually evaluate:

## Recall

Did Turn 1 still discover important defects?

## Verification precision

Did Turn 2 correctly reject speculative candidates?

## Final precision

Did Turn 3 avoid weak or policy-inappropriate findings?

## Evidence quality

Are verified findings supported by concrete source facts?

## Latency

Is the complete non-thinking pipeline faster than the previous thinking-based pipeline?

---

# Expected Diagnostic Patterns

Useful metrics may reveal:

### High candidate count + low verification survival

Turn 1 is too permissive.

Example:

```text
20 candidates
2 verified
```

Tighten Candidate Discovery.

---

### Low candidate count + high survival

Turn 1 may be too conservative.

Example:

```text
2 candidates
2 verified
```

Check for recall loss.

---

### High verification survival + low final survival

Project policy / reporting threshold is doing substantial filtering.

This may be expected, but inspect whether policy should move earlier.

Do not move it during Phase 3 unless clearly necessary.

---

### Verification consumes most latency

The context or per-candidate execution strategy is too expensive.

Optimize later through:

```text
candidate batching
bounded parallelism
smaller context
candidate caps
```

Do not prematurely redesign the architecture.

---

# Tests

Add or update tests covering the complete 3-stage flow.

At minimum:

## Candidate survives verification

```text
Turn 1:
c0

Turn 2:
c0 valid

Turn 3:
c0 reported
```

---

## Candidate rejected by verification

```text
Turn 1:
c0

Turn 2:
c0 invalid

Turn 3:
c0 absent
```

---

## Mixed candidates

```text
Turn 1:
c0, c1, c2

Turn 2:
c0 valid
c1 invalid
c2 valid

Turn 3 input:
c0, c2 only
```

---

## No candidates

Turn 2 and Turn 3 are not executed.

---

## All candidates rejected

Turn 3 is skipped.

---

## Verification failure

One failed candidate does not prevent unrelated candidates from completing.

---

## Candidate ID preservation

`candidate_id` remains stable through all stages.

---

## Rule attribution preservation

A rule-linked candidate keeps its attribution through verification and finalization.

---

## No severity in Turn 2

Verification output must not contain final severity.

---

## No new issue in Turn 2

Verification cannot introduce an issue absent from Turn 1.

---

## Turn 3 receives no source code

Finalization operates only on verified findings and applicable policy.

---

## Language behavior

English and Japanese final review output use the correct Turn 3 template.

Internal candidate and verification output remains English.

---

# Migration Strategy

Implement incrementally.

Recommended order:

```text
1. Add VerifiedIssue response types.
2. Add new review2.en.md.
3. Move existing review2.en.md behavior to review3.en.md.
4. Move review2.ja.md to review3.ja.md.
5. Add BuildTurn3().
6. Change BuildTurn2() to Verification.
7. Integrate VerificationContextResolver.
8. Execute Turn 2 per candidate.
9. Filter invalid candidates.
10. Pass verified candidates to Turn 3.
11. Add Verification recorder metrics.
12. Update tests.
13. Disable thinking for the complete pipeline.
14. Benchmark against the previous pipeline.
```

Keep commits or implementation steps independently testable where practical.

---

# Compatibility

Preserve:

* GitLab comment behavior;
* review execution status reporting;
* learned-rule retrieval;
* rule attribution;
* existing file grouping;
* existing AST extraction;
* project-specific policy;
* final English/Japanese output behavior;
* execution failure handling.

The architectural change should remain internal to review generation.

---

# Do Not Implement Yet

Explicitly exclude:

```text
semantic file grouping redesign
repository-wide verification
multi-hop AST traversal
automatic LLM tool use
dynamic recursive context retrieval
parallel verification optimization
candidate batching optimization
Turn 1 full-file architecture redesign
RAG redesign
severity policy redesign
new learned-rule lifecycle design
```

These belong to later phases.

---

# Acceptance Criteria

Phase 3 is complete when all of the following are true:

1. The review pipeline contains three explicit stages:

   * Candidate Discovery
   * Verification
   * Finalization

2. `review2.en.md` is dedicated to Verification.

3. `review3.en.md` and `review3.ja.md` are dedicated to Finalization.

4. Verification consumes candidate-specific source context from the AST-driven resolver.

5. Verification does not receive broad raw AST JSON as its primary context.

6. Verification cannot discover new issues.

7. Verification returns typed structured output.

8. Verification preserves candidate IDs.

9. Invalid candidates are removed before Finalization.

10. Turn 2 does not assign Critical / Major / Minor severity.

11. Turn 3 receives only verified findings.

12. Turn 3 remains responsible for project policy, deduplication, severity, and formatting.

13. Turn 3 does not inspect source code.

14. No-candidate and no-verified-issue paths avoid unnecessary LLM calls.

15. Candidate verification failures are isolated.

16. Learned-rule attribution remains functional.

17. Detection, Verification, and Finalization latency/token metrics are recorded.

18. Existing GitLab review behavior remains functional.

19. English and Japanese final review output remain supported.

20. The full pipeline can run with thinking disabled.

21. Relevant unit and integration tests pass.

22. The project builds successfully.

---

# Implementation Principle

Do not compensate for removing thinking mode by asking one turn to reason more deeply.

Distribute responsibility.

The intended pipeline is:

```text
Turn 1
Find something suspicious.

        ↓

Application / AST
Provide exactly the context needed to check it.

        ↓

Turn 2
Determine whether it is actually true.

        ↓

Turn 3
Determine whether and how it should be reported.
```

The core Phase 3 design principle is:

> Discovery, factual verification, and review policy are three different tasks and should not compete for attention inside the same LLM invocation.

Keep each turn small, explicit, and independently observable.
