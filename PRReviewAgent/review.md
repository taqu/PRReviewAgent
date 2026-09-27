# Phase 4 — Add Recovery Detection for Unreported Changed Regions

## Objective

Add a second non-thinking detection pass that reviews changed regions not associated with findings from the primary Turn 1 pass.

The purpose is to recover correctness issues missed by the first detection pass while keeping the existing final selection/finalization behavior.

The target pipeline is:

```text
Review Group
    ↓
Turn 1A: Primary Detection
thinking off
    ↓
Coverage Resolver
    ↓
Unreported Changed Regions
    ↓
Turn 1B: Recovery Detection
thinking off
    ↓
Candidate Union
    ↓
Existing Turn 2
Selection / Dedupe / Severity / Formatting
    ↓
Final Review
```

This is **not** a verification pipeline.

Do not use the Recovery pass to validate or reject candidates from Turn 1A.

Its only purpose is:

```text
Find additional review candidates
from changed regions not already associated
with Primary Detection findings.
```

---

# Background

The current `thinking off` review is much faster than `thinking on`, but detection is not perfectly stable.

A second inexpensive non-thinking detection pass may improve recall while remaining substantially faster than enabling thinking.

Previous work already provides:

```text
Phase 1:
C/C++ semantic grouping

Phase 2:
budgeted deterministic C/C++ group splitting

Phase 3:
changed-region tracking
candidate-to-region mapping
reported/unreported changed-region sets
```

Phase 4 should use these existing components.

Do not redesign them unless a small mechanical change is required.

---

# 1. Preserve the Primary Detection Pass

The current Turn 1 remains the primary detection pass.

Conceptually rename it internally if useful:

```text
Primary Detection
```

but do not change its prompt or output schema in this phase.

The current Turn 1 constraints remain unchanged:

```text
Maximum 10 candidates
confidence must be high or medium
do not output low-confidence candidates
do not assign severity
JSON only
```

Do not modify `review1.en.md` for the Primary Detection pass unless needed only to separate template loading from the new Recovery template.

Primary benchmark behavior must remain comparable to the previous phase.

---

# 2. Add a Separate Recovery Detection Pass

After Primary Detection and coverage resolution, run a second LLM detection pass.

This pass must use:

```text
thinking off
```

through the same external/model configuration used by the existing non-thinking review.

Do not enable thinking specifically for Recovery Detection.

The system may not know whether thinking is externally enabled or disabled.

Do not add logic that attempts to infer reasoning mode.

---

# 3. Recovery Detection Reviews Only Unreported Changed Regions

Use the Phase 3 coverage result.

Recovery input should be constructed from:

```text
unreported changed regions
+
the source context needed to understand those regions
```

Do not treat the entire original review group as equally important again.

Primary objective:

```text
redirect model attention toward changes
that did not produce findings in Turn 1A
```

---

# 4. "Unreported" Does Not Mean "Bug"

Preserve the Phase 3 terminology.

An unreported changed region means only:

```text
Primary Detection did not emit a candidate
mapped to this changed region.
```

It does not mean:

```text
Primary Detection failed.
```

The Recovery prompt must not tell the model that the remaining regions contain bugs.

Do not write instructions such as:

```text
Find the bugs missed by the first pass.
```

Prefer:

```text
Review the remaining changed regions for any additional
high- or medium-confidence correctness issues.
```

This avoids artificially increasing false positives.

---

# 5. Use a Dedicated Recovery Prompt

Create a separate template, for example:

```text
review1-recovery.en.md
```

or a name consistent with the current project conventions.

Do not reuse the full Primary Detection prompt blindly if a smaller Recovery prompt is clearer.

However, preserve the same candidate output schema so Primary and Recovery candidates can be combined directly.

Expected output shape remains conceptually:

```json
{
  "issues": [
    {
      "location": "...",
      "problem": "...",
      "evidence": "...",
      "impact": "...",
      "suggested_fix": "...",
      "confidence": "high"
    }
  ]
}
```

Do not introduce:

```text
CandidateIssue
VerifiedIssue
valid
verification_status
severity
```

into Recovery Detection.

Recovery produces normal detection candidates.

---

# 6. Suggested Recovery Prompt Responsibilities

The Recovery prompt should clearly state:

```text
- Review only the provided remaining changed regions.
- Look for additional correctness issues.
- Do not repeat already reported findings.
- Report only issues supported by the provided code.
- Emit only high- or medium-confidence candidates.
- Do not invent bugs because a region was not previously reported.
- Do not assign severity.
- Output JSON only.
```

Keep the prompt focused.

Do not add a large new review heuristic checklist in this phase.

The experiment should primarily test:

```text
second-pass attention reallocation
```

rather than prompt engineering.

---

# 7. Provide Already-Reported Findings as Exclusion Context

Recovery Detection needs enough information to avoid rediscovering the same issues.

Provide a compact list of already-reported findings.

For example:

```text
Already reported findings:

- src/renderer.cpp:323 sampleFloat
  Scalar texture sampling mirrors the U coordinate.

- src/renderer.cpp:597 hitTriangle
  Interpolated U uses vertex V components.
```

Do not include full evidence, impact, and suggested fix unless required.

The purpose of this list is exclusion, not re-evaluation.

Keep it token-efficient.

---

# 8. Prefer Excluding Reported Regions From Recovery Source Context

Where practical, do not include already-reported changed regions as primary Recovery review targets.

For example, if the original group contains:

```text
r0 reported
r1 unreported
r2 reported
r3 unreported
```

the Recovery prompt should focus on:

```text
r1
r3
```

rather than repeating all four changed regions.

However, surrounding unchanged source context may still be included when required to understand `r1` or `r3`.

Do not remove necessary local context merely because it contains nearby reported code.

---

# 9. Preserve Source Context Around Remaining Regions

Recovery Detection should not receive isolated changed lines with no surrounding code.

For each unreported region, provide enough source context to understand:

```text
containing function / method
relevant nearby expressions
local variables
control flow
```

Reuse existing review context extraction logic where possible.

Do not implement repository-wide dependency expansion in this phase.

The intended context is:

```text
remaining changed region
+
local source context already available
```

not:

```text
remaining region
+
all callers
+
all callees
+
all dependency files
```

---

# 10. Preserve C/C++ Semantic Group Context Where Useful

For C/C++, Phase 1 and Phase 2 may place related files or symbols in the same review group.

Recovery Detection should retain useful semantic-group context when it helps interpret the remaining region.

Example:

```text
renderSphere changed region is unreported

Related renderMesh implementation is already present
in the semantic review group
```

It may remain available as supporting context.

Do not reduce Recovery context so aggressively that useful comparisons introduced by semantic grouping disappear.

---

# 11. Non-C/C++ Files Remain Per-File

Preserve the existing language policy.

```text
C/C++:
semantic grouped / budgeted context

other languages:
one changed file per review group
```

Recovery Detection should operate on whatever final review groups Phase 2 produces.

Do not introduce new cross-file grouping for non-C/C++ languages.

---

# 12. Skip Recovery When Nothing Remains

If all changed regions are already reported by Primary Detection:

```text
unreported_region_count == 0
```

do not call the Recovery model.

Proceed directly to the existing Turn 2.

Record that Recovery Detection was skipped.

Example:

```text
recovery_status = "skipped_no_remaining_regions"
```

Use naming consistent with the existing metrics/logging system.

---

# 13. Recovery Candidate Limit

Recovery Detection should also have a candidate limit.

Prefer keeping:

```text
Maximum 10 candidates
```

unless the existing candidate limit is naturally configurable per turn.

Do not increase the maximum merely because this is a second pass.

The intent is to recover a small number of additional high-confidence findings.

---

# 14. Combine Primary and Recovery Candidates

After Recovery Detection:

```text
Primary Candidates
       +
Recovery Candidates
       ↓
Candidate Union
```

Pass the combined candidate set into the existing Turn 2.

Do not run Primary candidates through a separate verification stage.

Do not create a new finalization turn.

---

# 15. Preserve Candidate Provenance Internally

Internally record whether each candidate came from:

```text
primary
recovery
```

For example:

```csharp
DetectionSource.Primary
DetectionSource.Recovery
```

or equivalent.

This metadata is for:

```text
benchmarking
diagnostics
deduplication analysis
```

Do not expose it in the final GitLab review.

Do not require the LLM to emit it.

---

# 16. Deduplicate Obvious Primary/Recovery Duplicates

Recovery should be instructed not to repeat findings, but duplicates may still occur.

Before existing Turn 2, optionally remove only obvious duplicates.

Use conservative deterministic matching such as:

```text
same canonical file
+
same changed region
+
same or overlapping location
```

Do not implement complex semantic deduplication.

If duplicate status is ambiguous:

```text
keep both
```

and allow the existing Turn 2 deduplication/selection logic to resolve them.

Avoid losing real findings through aggressive pre-filtering.

---

# 17. Do Not Deduplicate Merely by Function Name

Example:

```text
traceMesh:
roughness UV swap

traceMesh:
incorrect environment lighting direction
```

These are separate issues in the same function.

Do not treat them as duplicates simply because:

```text
same file
same symbol
```

Use changed-region identity or concrete source overlap where possible.

---

# 18. Existing Turn 2 Remains the Final Stage

Turn 2 should receive the union:

```text
Primary candidates
+
Recovery candidates
```

Then continue performing its existing responsibilities:

```text
candidate selection
false-positive rejection
deduplication
project policy
severity
language
final formatting
```

Do not rewrite Turn 2 in Phase 4.

Do not remove validation behavior from Turn 2.

---

# 19. Handle Combined Candidate Count Carefully

Primary may return up to 10 candidates and Recovery may return additional candidates.

Therefore the combined set may exceed 10.

Do not silently truncate the combined set before Turn 2 unless an existing hard system limit requires it.

For example:

```text
Primary: 8
Recovery: 4
Combined: 12
```

Turn 2 should receive all 12 if the current architecture supports it.

The `Maximum 10 candidates` constraint applies to each Detection output, not necessarily to the candidate union.

If there is an existing Turn 2 input limit, use the existing behavior and document it.

Do not introduce an arbitrary new top-10 ranking mechanism in this phase.

---

# 20. Do Not Ask Recovery to Verify Primary Findings

The Recovery prompt must not contain instructions such as:

```text
Check whether the previous findings are valid.
Reject incorrect findings.
Confirm the first pass.
```

That recreates the previously unsuccessful Verification architecture.

Recovery is additive:

```text
Primary Detection
+
Additional Detection
```

not:

```text
Detection
→ Verification
```

---

# 21. Preserve Changed-Region Coverage After Recovery

Run coverage mapping for Recovery candidates as well.

After Recovery, the system should be able to record:

```text
regions reported by Primary
regions newly reported by Recovery
regions still unreported after Recovery
```

For example:

```text
10 changed regions

Primary:
6 reported

Recovery:
2 additional reported

Remaining:
2 unreported
```

Do not use this final remaining set for another pass in Phase 4.

Exactly one Recovery pass should be added.

---

# 22. No Recursive Recovery

Do not implement:

```text
Primary
→ Recovery 1
→ Recovery 2
→ Recovery 3
```

even if unreported regions remain.

Phase 4 adds exactly:

```text
one primary detection pass
+
one recovery detection pass
```

Recursive or adaptive passes belong to a separate experiment.

---

# 23. Add Recovery Metrics

Extend existing recorder/benchmark metrics.

Record at minimum:

## Primary Detection

```text
duration_ms
input_tokens
output_tokens
candidate_count
reported_region_count
```

## Recovery Detection

```text
executed / skipped
duration_ms
input_tokens
output_tokens
candidate_count
newly_reported_region_count
duplicate_candidate_count
```

## Combined

```text
primary_candidate_count
recovery_candidate_count
combined_candidate_count
final_turn2_finding_count
```

## Overall

```text
total_duration_ms
total_input_tokens
total_output_tokens
```

Reuse existing timing/token measurement infrastructure.

---

# 24. Important Recovery Benchmark Metrics

The key Phase 4 metrics are:

```text
Recovery Additional TP
Recovery FP
Recovery Duplicate Count
Recovery Additional Latency
```

The application does not need to automatically know TP/FP if benchmark scoring is external.

At minimum, persist enough information to calculate them afterward.

Especially useful:

```text
candidate source = primary/recovery
candidate location
mapped changed region
```

---

# 25. Extend Benchmark Logs

A benchmark run should make the stages easy to inspect.

Suggested output:

```text
benchmark-logs/<review_run_id>/
    metadata.json
    summary.json

    primary-turn1.json
    recovery-turn1.json
    turn2.txt

    coverage-primary.json
    coverage-recovery.json
```

Use the current benchmark layout if one already exists; do not rename everything unnecessarily.

The essential requirement is that Primary and Recovery outputs can be distinguished.

---

# 26. Recovery Timing Must Exclude File Logging

Preserve timing semantics.

Measure:

```text
model inference
```

the same way as existing Turn timings.

Do not include benchmark file serialization inside the inference timer.

Example:

```text
start recovery timer
invoke model
stop recovery timer

then write log files
```

---

# 27. Recovery Failure Must Not Destroy Primary Results

If Primary Detection succeeds but Recovery fails:

```text
keep Primary candidates
```

and continue to Turn 2 if practical.

Recovery is an enhancement, not a requirement for completing the review.

Example behavior:

```text
Primary success
Recovery failure
    ↓
log warning
    ↓
Turn 2 with Primary candidates only
```

Record the failure in benchmark metadata.

Do not fail the entire review solely because the optional Recovery pass failed unless existing error-handling architecture makes continuation impossible.

---

# 28. Turn 2 Should Still Run If Recovery Returns Zero Candidates

Example:

```text
Primary candidates: 6
Recovery candidates: 0
```

Expected:

```text
Turn 2 receives the 6 Primary candidates.
```

This is a normal result.

Do not interpret zero Recovery candidates as a failure.

---

# 29. Do Not Change Semantic Grouping

Phase 4 must preserve:

```text
Phase 1 semantic grouping
Phase 2 group budgets
Phase 2 deterministic splitting
Phase 3 region tracking
```

Do not tune grouping thresholds while evaluating Recovery Detection.

Otherwise it will be impossible to isolate the effect of the second pass.

---

# 30. Do Not Change Review Heuristics

Do not add large new rules such as:

```text
check atan2 argument ordering
check cross-product handedness
check U/V swaps
check width/height
```

in this phase.

Those may be valuable later, but Phase 4 should test whether:

```text
a second attention pass alone
```

recovers additional findings.

Keep the Recovery prompt generic and correctness-focused.

---

# 31. Suggested Recovery Prompt

Create a concise template based on the following intent:

```text
You are performing a second-pass code review.

The first detection pass already reported some issues.
Do not repeat those findings.

Review only the remaining changed regions and their provided
source context for additional correctness issues.

A remaining changed region is not necessarily incorrect.
Report an issue only when the provided code gives sufficient
evidence that the change can cause incorrect behavior.

Only report high- or medium-confidence issues.

For every reported issue, provide:
- location
- problem
- evidence
- impact
- suggested_fix
- confidence

Do not assign severity.
Do not write the final review.
Output JSON only.
```

Adapt terminology and exact formatting to match the current prompt conventions.

Do not include benchmark-specific bug information.

---

# 32. Tests

Add focused tests.

## Case 1 — No Unreported Regions

Primary coverage:

```text
all changed regions reported
```

Expected:

```text
Recovery LLM call is skipped.
Existing Turn 2 runs normally.
```

---

## Case 2 — Remaining Regions Exist

Primary:

```text
r0 reported
r1 unreported
r2 reported
r3 unreported
```

Expected Recovery target:

```text
r1
r3
```

---

## Case 3 — Recovery Adds a New Candidate

Primary candidate maps to:

```text
r0
```

Recovery candidate maps to:

```text
r2
```

Expected:

```text
combined candidates contain both
```

and Turn 2 receives both.

---

## Case 4 — Recovery Repeats Primary Candidate

Primary:

```text
r0 issue A
```

Recovery:

```text
r0 issue A
```

Expected:

```text
obvious duplicate may be removed
or safely passed to existing Turn 2 deduplication
```

Do not create two final findings.

---

## Case 5 — Same Function, Different Regions

Changed regions:

```text
r0 traceMesh:679
r1 traceMesh:691
r2 traceMesh:721
```

Primary reports:

```text
r2
```

Recovery reports:

```text
r0
```

Expected:

```text
both candidates preserved
```

They are not duplicates merely because both belong to `traceMesh`.

---

## Case 6 — Recovery Returns Zero Issues

Expected:

```text
Primary candidates continue unchanged to Turn 2.
```

---

## Case 7 — Recovery Fails

Expected where practical:

```text
warning logged
Primary candidates preserved
Turn 2 still runs
benchmark records recovery failure
```

---

## Case 8 — Coverage After Recovery

Primary reports:

```text
r0
r2
```

Recovery reports:

```text
r1
```

Expected final coverage:

```text
reported:
r0
r1
r2

remaining:
other regions only
```

---

## Case 9 — Combined Candidate Count Above 10

Primary:

```text
8 candidates
```

Recovery:

```text
5 candidates
```

Expected:

```text
13 combined candidates
```

unless a pre-existing hard Turn 2 limit requires another behavior.

Do not silently discard candidates solely because Turn 1 has a 10-candidate output limit.

---

# 33. Benchmark Plan

Compare at least:

```text
A. Phase 3 baseline
   Semantic grouping
   One Detection pass
   thinking off

B. Phase 4
   Same semantic grouping
   Primary + Recovery Detection
   thinking off
```

Do not change model, prompts for Primary, grouping, or benchmark corpus between A and B.

Run multiple trials.

Measure:

```text
Primary detections
Recovery additional detections
Recovery duplicates
false positives
final findings

Primary latency
Recovery latency
Turn 2 latency
total latency

input tokens
output tokens
```

---

# 34. Primary Experiment Question

Phase 4 should answer:

```text
Can one additional thinking-off detection pass recover
meaningful issues missed by Primary Detection while remaining
far faster than thinking-on review?
```

Do not optimize Recovery based on benchmark results during the same implementation.

First establish the clean baseline.

---

# 35. Success Criteria

Phase 4 is successful if:

1. The existing Primary Detection behavior remains unchanged.

2. Exactly one Recovery Detection pass is added.

3. Recovery targets unreported changed regions from Phase 3.

4. Already-reported findings are provided only as exclusion context.

5. Recovery does not verify or reject Primary findings.

6. Recovery uses the same candidate schema as Primary.

7. Primary and Recovery candidates are combined before existing Turn 2.

8. Same-function but different-region findings are preserved.

9. Obvious duplicate candidates can be identified conservatively.

10. Turn 2 remains otherwise unchanged.

11. Recovery is skipped when no unreported regions remain.

12. Recovery failure does not discard Primary candidates where continuation is possible.

13. Recovery candidates are mapped back to changed regions.

14. Primary vs Recovery provenance is logged.

15. Benchmark logs expose per-pass timing, token use, candidates, and coverage.

16. Semantic grouping and group budgets are unchanged.

17. No recursive Recovery passes are added.

18. No thinking-specific detection logic is added.

---

# Non-Goals

Do not implement:

```text
Verification Turn
candidate validity scoring
recursive Recovery passes
adaptive multi-pass loops
thinking fallback
thinking-on escalation
new semantic grouping rules
group-budget tuning
AST caller/callee source expansion
repository-wide context retrieval
large prompt heuristic changes
automatic TP/FP scoring
severity changes
new finalization stage
```

These are separate experiments.

---

# Suggested Implementation Order

Implement in this order:

```text
1. Keep the current Primary Turn 1 unchanged.

2. Reuse Phase 3 ReviewCoverage after Primary Detection.

3. Add a Recovery prompt template.

4. Build Recovery input from unreported changed regions.

5. Add compact already-reported exclusion context.

6. Invoke exactly one Recovery Detection pass.

7. Deserialize Recovery output into the existing candidate model.

8. Map Recovery candidates to changed regions.

9. Add Primary/Recovery provenance metadata.

10. Conservatively handle obvious duplicates.

11. Union Primary and Recovery candidates.

12. Pass the union into the existing Turn 2.

13. Add per-pass metrics and benchmark logs.

14. Add failure/skip handling.

15. Add focused tests.

16. Run existing tests.

17. Benchmark Phase 3 vs Phase 4 using thinking off.
```

Do not begin adaptive Recovery or thinking fallback as part of this task.

---

# Design Principle

The central principle of Phase 4 is:

```text
Do not spend more reasoning on the same findings.

Spend another cheap non-thinking pass
on the changed code that did not produce findings the first time.
```

The intended architecture is:

```text
Primary Detection
        ↓
What produced findings?
        ↓
Remaining changed regions
        ↓
Recovery Detection
        ↓
Candidate union
        ↓
Existing final selection
```

Recovery is additive, conservative, and attention-focused.

It must improve the opportunity to discover missed issues without recreating the expensive `thinking on` path.