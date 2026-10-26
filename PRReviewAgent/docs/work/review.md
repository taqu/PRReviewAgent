# Phase 7 — Add Conditional Recovery Execution

## Objective

Make Recovery Detection conditional so the second `thinking off` detection pass runs only when it has a meaningful amount of remaining review work.

Phases 4–6 established:

```text
Primary Detection
    ↓
Coverage Resolution
    ↓
Focused Recovery Context
    ↓
Recovery Detection
    ↓
Candidate Union
    ↓
Existing Turn 2
```

Until now, Recovery has been executed whenever unreported changed regions remain.

Phase 7 should introduce a deterministic **Recovery Execution Policy** that decides whether a Recovery LLM call is worth making.

The primary objective is:

```text
Preserve most of the recall gain from Recovery
while reducing unnecessary Recovery latency and token usage.
```

This phase changes only:

```text
whether Recovery Detection is executed
```

Do not change:

```text
Primary Detection
semantic grouping
group budgets
coverage mapping
Recovery context construction
Recovery prompt
candidate schema
candidate union
Turn 2
thinking configuration
```

---

# Background

`thinking off` is the production-oriented baseline because `thinking on` is approximately an order of magnitude slower.

The Recovery pass intentionally spends another inexpensive non-thinking inference call on changed regions that did not produce Primary findings.

However, not every group benefits equally from a second pass.

Examples where Recovery may have little value:

```text
- only one very small unreported region remains
- Primary already reported nearly all changed regions
- Recovery context would contain very little meaningful changed code
- the group contains no remaining changed expressions
```

Running Recovery blindly in every eligible group wastes latency.

Phase 7 should make this decision deterministic and measurable.

---

# Existing Pipeline

Current conceptual pipeline:

```text
Review Group
    ↓
Primary Detection
    ↓
Coverage Resolver
    ↓
Focused Recovery Context
    ↓
Recovery Detection
    ↓
Candidate Union
    ↓
Turn 2
```

Target pipeline:

```text
Review Group
    ↓
Primary Detection
    ↓
Coverage Resolver
    ↓
Recovery Execution Policy
    │
    ├── run
    │     ↓
    │   Focused Recovery Context
    │     ↓
    │   Recovery Detection
    │
    └── skip
          ↓
    Candidate Union / Primary candidates
          ↓
       Existing Turn 2
```

The policy must not call an LLM.

---

# 1. Introduce a Recovery Execution Policy

Add one small deterministic component responsible for deciding:

```text
Run Recovery?
```

Possible naming:

```text
RecoveryExecutionPolicy
RecoveryPolicy
ShouldRunRecovery(...)
```

Use naming consistent with the existing codebase.

The decision result should contain at least:

```text
Run / Skip
Reason
```

Conceptually:

```csharp
RecoveryDecision
{
    bool ShouldRun;
    RecoverySkipReason? SkipReason;
}
```

Do not over-engineer the abstraction.

---

# 2. Keep the Initial Policy Conservative

The first conditional policy should be intentionally simple.

Do not attempt to predict whether a changed region contains a bug.

The policy should operate only on structural information already known to the system.

Use signals such as:

```text
unreported changed-region count
amount of unreported changed code
Recovery context size
whether any actual Recovery target remains
```

Do not use semantic bug-likelihood scoring.

---

# 3. Mandatory Skip: No Unreported Regions

Preserve the existing behavior:

```text
if unreported_region_count == 0:
    skip Recovery
```

Reason:

```text
no_remaining_regions
```

No Recovery prompt should be constructed.

No Recovery LLM call should occur.

---

# 4. Mandatory Skip: No Recoverable Target Context

It is possible for coverage to contain an unreported region while Recovery context construction produces no valid review target.

For example:

```text
deleted or malformed location
unresolvable context
unsupported diff representation
```

If there is no usable Recovery target:

```text
skip Recovery
```

with a reason such as:

```text
no_recoverable_context
```

Do not issue an empty or nearly empty LLM request.

---

# 5. Add a Minimum Recovery Work Threshold

Introduce a configurable minimum amount of remaining changed code required to justify another LLM call.

Prefer using one simple structural measure initially.

Recommended candidates:

```text
unreported changed lines
```

or:

```text
unreported changed regions
```

For example, conceptually:

```text
if unreported_region_count < MinimumRecoveryRegions:
    skip Recovery
```

or:

```text
if unreported_changed_lines < MinimumRecoveryChangedLines:
    skip Recovery
```

Do not hard-code several overlapping thresholds unless needed.

Start with the smallest policy that can be benchmarked clearly.

---

# 6. Prefer Changed-Code Amount Over Total Context Size

Do not decide solely from Recovery prompt token size.

A large containing function may create a large Recovery context even when only one tiny changed expression remains.

The important quantity is the amount of remaining changed review work.

Preferred signals:

```text
number of unreported regions
number of unreported changed lines
```

Supporting-context size may be used as an additional metric, but should not be the primary indication that Recovery is useful.

---

# 7. Do Not Use Candidate Count Alone

Do not implement a rule such as:

```text
if primary_candidate_count < 5:
    run Recovery
```

by itself.

Candidate count does not describe coverage.

For example:

```text
10 changed regions
1 Primary candidate
```

and:

```text
1 changed region
1 Primary candidate
```

have very different remaining work.

Use Phase 3 coverage information instead.

---

# 8. Coverage Ratio May Be Recorded, But Use Carefully

Calculate:

```text
reported_region_ratio =
    reported_regions / total_changed_regions
```

and:

```text
unreported_region_ratio =
    unreported_regions / total_changed_regions
```

for diagnostics and benchmarking.

These values may later become policy signals.

However, do not create an aggressive rule such as:

```text
if reported_ratio > 80%:
    skip
```

without benchmark evidence.

One remaining region may still contain an important issue.

For the initial Phase 7 implementation, prefer absolute minimum-work rules.

---

# 9. Never Use Confidence as the Sole Trigger

Primary candidates contain:

```text
high
medium
```

confidence.

Do not infer:

```text
all Primary candidates are high confidence
→ Recovery unnecessary
```

Confidence describes reported candidates, not unreported code.

Recovery eligibility should primarily depend on remaining changed regions.

---

# 10. Do Not Infer Bug Probability

The policy must never use rules such as:

```text
cross() is risky
→ run Recovery

simple assignment
→ skip Recovery

pointer code
→ run Recovery
```

That would mix review heuristics into execution policy.

Phase 7 is about cost control, not bug classification.

---

# 11. Add a Maximum Recovery Context Guard

Recovery context already has a budget from Phase 5.

Preserve that budget.

If Recovery context cannot be safely constructed within the existing hard limit:

```text
do not issue an oversized call
```

Use existing deterministic splitting behavior where already supported.

If no valid bounded Recovery request can be produced, skip with an explicit reason.

Do not silently truncate target changed regions.

---

# 12. Preserve One-Recovery-Pass Maximum

Phase 7 does not introduce adaptive loops.

Per review group:

```text
Primary Detection:
exactly once

Recovery Detection:
zero or one time
```

Never:

```text
Primary
→ Recovery
→ Recovery again
```

regardless of how many unreported regions remain afterward.

---

# 13. Make the Policy Configurable

Keep policy thresholds centralized.

For example:

```text
MinimumRecoveryRegionCount
```

or:

```text
MinimumRecoveryChangedLines
```

Use existing configuration infrastructure if available.

Do not scatter numeric literals throughout the pipeline.

Avoid introducing a large new configuration subsystem.

---

# 14. Add an Explicit Benchmark Override

Because Recovery effectiveness is still being evaluated, provide a simple way to force Recovery behavior during benchmarks.

Preferred conceptual modes:

```text
Auto
Always
Never
```

For example:

```text
RecoveryMode.Auto
RecoveryMode.Always
RecoveryMode.Never
```

The exact representation should follow existing configuration conventions.

Purpose:

```text
Always
→ reproduce Phase 6 behavior

Auto
→ test Phase 7 policy

Never
→ measure Primary-only baseline
```

This is valuable for controlled benchmark comparisons.

Do not expose this as a user-facing product feature unless the current architecture naturally does so.

---

# 15. Default Production-Oriented Mode

After implementation, the normal Phase 7 behavior should use:

```text
Auto
```

for benchmark experiments unless existing configuration requires otherwise.

`Always` exists to preserve the previous benchmark condition.

`Never` exists to measure Primary-only performance.

---

# 16. Recovery Prompt Must Remain Unchanged

Do not modify the Phase 6 Recovery prompt.

The Phase 7 experiment must isolate:

```text
conditional execution policy
```

from:

```text
prompt quality
```

Do not add wording about why Recovery was triggered.

The model does not need to know the policy decision.

---

# 17. Recovery Context Must Remain Unchanged

If Recovery runs, its context must be constructed exactly as in Phase 5.

Do not alter:

```text
target regions
fragment selection
supporting context
context budget
reported-region exclusion
```

during this task.

A Phase 7 `Always` run should behave equivalently to Phase 6.

---

# 18. Primary Detection Must Remain Unchanged

Do not modify:

```text
Primary prompt
Primary context
Primary candidate limit
Primary candidate schema
```

Primary Detection remains the control stage.

---

# 19. Candidate Union Behavior

If Recovery runs:

```text
Primary
+
Recovery
→ existing candidate union
```

If Recovery is skipped:

```text
Primary
→ Turn 2
```

Do not create an empty synthetic Recovery result unless required mechanically.

Turn 2 should behave exactly as if Recovery had returned zero candidates.

---

# 20. Recovery Skip Must Not Affect Coverage Semantics

If Recovery is skipped:

```text
unreported regions remain unreported
```

Do not mark them as:

```text
reviewed
safe
resolved
accepted
```

Coverage must continue representing facts only.

Example:

```text
Primary reported: 6
Remaining: 4
Recovery skipped
```

Final coverage should still show:

```text
4 unreported regions
```

---

# 21. Record the Decision

For every review group, record:

```text
Recovery mode
Recovery decision
decision reason
```

Example:

```json
{
  "recovery": {
    "mode": "auto",
    "executed": false,
    "decision_reason": "below_minimum_remaining_work"
  }
}
```

If Recovery executes:

```json
{
  "recovery": {
    "mode": "auto",
    "executed": true,
    "decision_reason": "remaining_work_above_threshold"
  }
}
```

Use stable machine-readable reason values.

---

# 22. Suggested Decision Reasons

Keep the reason set small.

Possible values:

```text
forced_always
disabled
no_remaining_regions
no_recoverable_context
below_minimum_remaining_work
eligible
recovery_failure
```

Use equivalent names consistent with project style.

Do not create dozens of highly specific reasons.

---

# 23. Add Policy Metrics

Record:

```text
total groups
groups eligible for Recovery
groups where Recovery executed
groups where Recovery skipped
```

Also record skip reasons.

Example run-level summary:

```json
{
  "recovery_policy": {
    "groups_total": 8,
    "executed": 3,
    "skipped": 5,
    "skip_reasons": {
      "no_remaining_regions": 3,
      "below_minimum_remaining_work": 2
    }
  }
}
```

---

# 24. Measure Saved Cost

Add derived structural metrics where convenient:

```text
Recovery calls avoided
Recovery input tokens avoided
Recovery latency avoided
```

Do not fabricate hypothetical latency if the skipped call was never executed.

For exact benchmark comparisons, use separate `Always` and `Auto` runs.

If estimating avoided tokens from already-built context is easy and deterministic, record it as an estimate with clear naming.

Otherwise omit it.

---

# 25. Preserve Existing Timing Semantics

When Recovery is skipped:

```text
recovery_duration_ms = 0
```

or:

```text
null
```

according to existing metric conventions.

Also record:

```text
recovery_executed = false
```

so zero duration is not ambiguous.

Do not include policy evaluation or benchmark file writing inside model-inference timing.

---

# 26. Policy Evaluation Should Be Cheap

The Recovery policy should use data already available after Phase 3/5.

Do not perform:

```text
new AST traversal
repository-wide search
extra parsing
model calls
embedding computation
```

solely to make the Recovery decision.

Policy overhead should be negligible relative to LLM inference.

---

# 27. Apply Policy Per Review Group

Recovery eligibility should be evaluated independently for each final review group.

Example:

```text
Group A
Primary finds all changed regions
→ skip Recovery

Group B
many unreported regions remain
→ run Recovery

Group C
only trivial amount of remaining changed code
→ policy may skip
```

Do not use one MR-wide yes/no decision unless the existing architecture requires it.

This allows Recovery cost to scale with actual remaining work.

---

# 28. Preserve C/C++ and Non-C/C++ Grouping Policy

The execution policy operates after grouping.

It must not care whether a group came from:

```text
C/C++ semantic grouping
```

or:

```text
non-C/C++ per-file grouping
```

The same Recovery eligibility logic may apply to both.

Do not introduce new cross-file behavior for non-C/C++ languages.

---

# 29. Do Not Treat Large Groups as Automatically Eligible

A group can be large while Primary has already reported all changed regions.

Likewise, a small group may still contain several unreported changes.

Eligibility should depend on:

```text
remaining changed work
```

not original group size alone.

---

# 30. Start With One Tunable Threshold

For the first Phase 7 implementation, prefer only one optional minimum-work threshold in addition to mandatory skip conditions.

For example:

```text
MinimumRecoveryChangedRegionCount
```

or:

```text
MinimumRecoveryChangedLineCount
```

Do not immediately combine:

```text
region count
line count
token count
coverage percentage
candidate count
symbol count
```

into a complex score.

The benchmark must remain interpretable.

---

# 31. Recommended Initial Policy

A simple first implementation may be:

```text
if mode == Never:
    skip

if mode == Always:
    run if valid Recovery context exists

if no unreported regions:
    skip

build/inspect Recovery targets

if no valid targets:
    skip

if remaining changed work < configured minimum:
    skip

otherwise:
    run
```

Keep this logic explicit and easy to test.

---

# 32. Do Not Optimize the Threshold During Implementation

Choose a reasonable initial threshold or expose it as configuration.

Do not repeatedly tune it against the current seeded benchmark during the same coding task.

Phase 7 should create the mechanism.

Benchmarking should determine the best policy afterward.

Avoid overfitting to the current ten seeded changes.

---

# 33. Tests

Add focused tests.

## Case 1 — No Remaining Regions

Input:

```text
unreported regions = 0
mode = Auto
```

Expected:

```text
Recovery skipped
reason = no_remaining_regions
```

---

## Case 2 — Remaining Work Above Threshold

Input:

```text
unreported regions > minimum
valid Recovery context exists
```

Expected:

```text
Recovery executed
```

---

## Case 3 — Remaining Work Below Threshold

Input:

```text
remaining work < configured minimum
```

Expected:

```text
Recovery skipped
reason = below_minimum_remaining_work
```

---

## Case 4 — Force Always

Input:

```text
mode = Always
unreported regions exist
valid context exists
```

Expected:

```text
Recovery executes regardless of minimum-work threshold
```

Mandatory safety/context constraints may still apply.

---

## Case 5 — Force Never

Input:

```text
mode = Never
```

Expected:

```text
Recovery never executes
```

Primary candidates continue to Turn 2 normally.

---

## Case 6 — No Recoverable Context

Input:

```text
unreported region exists
Recovery Context Builder produces no valid target
```

Expected:

```text
Recovery skipped
reason = no_recoverable_context
```

---

## Case 7 — Skip Preserves Coverage

Input:

```text
4 unreported regions
Recovery skipped
```

Expected:

```text
4 regions remain unreported
```

No synthetic coverage changes.

---

## Case 8 — Skip Preserves Turn 2 Behavior

Primary returns:

```text
5 candidates
```

Recovery is skipped.

Expected:

```text
Turn 2 receives exactly those 5 candidates.
```

---

## Case 9 — Always Matches Phase 6 Path

With:

```text
mode = Always
```

and valid remaining regions:

Expected behavior should match the Phase 6 execution path:

```text
Primary
→ Recovery
→ Candidate Union
→ Turn 2
```

---

## Case 10 — Determinism

Identical:

```text
coverage
configuration
Recovery context metadata
```

must produce the same Recovery decision and reason.

---

# 34. Benchmark Plan

Compare three conditions.

## A. Primary Only

```text
RecoveryMode = Never
thinking off
```

Measures:

```text
minimum latency baseline
```

---

## B. Always Recovery

```text
RecoveryMode = Always
thinking off
```

Equivalent to the Phase 6-style behavior.

Measures:

```text
maximum Recovery recall opportunity
```

---

## C. Conditional Recovery

```text
RecoveryMode = Auto
thinking off
```

Measures:

```text
latency / quality tradeoff
```

Keep identical:

```text
model
benchmark corpus
grouping
group budgets
Primary prompt
Recovery context
Recovery prompt
candidate schema
Turn 2
```

---

# 35. Benchmark Metrics

Measure at least:

```text
code-provable issue detection frequency
false positives
final findings

Recovery calls per review
Recovery candidate count
Recovery additional findings
Recovery duplicate count

Primary latency
Recovery latency
Turn 2 latency
total latency

input tokens
output tokens
```

For Auto specifically:

```text
Recovery execution rate
skip reasons
```

---

# 36. Key Comparisons

The most useful comparisons are:

```text
Never vs Always
→ maximum quality benefit of Recovery
```

```text
Auto vs Always
→ quality lost by skipping some Recovery calls
```

```text
Auto vs Never
→ quality gained for added latency
```

This makes the tradeoff measurable.

---

# 37. Success Criteria

Phase 7 is successful if:

1. Recovery execution can be controlled by a deterministic policy.

2. Recovery supports `Auto`, `Always`, and `Never` behavior or equivalent benchmark controls.

3. No remaining regions always skips Recovery in Auto mode.

4. Empty/unusable Recovery context does not trigger an LLM call.

5. One simple minimum-work threshold can be configured.

6. The policy does not attempt to predict whether code is buggy.

7. Candidate confidence is not used as the sole eligibility signal.

8. Primary Detection remains unchanged.

9. Recovery context construction remains unchanged.

10. Recovery prompt remains unchanged.

11. Candidate Union remains unchanged.

12. Turn 2 remains unchanged.

13. Skipping Recovery does not alter coverage facts.

14. Recovery decisions and reasons are recorded.

15. Benchmark logs expose Recovery execution rate and skip reasons.

16. Policy evaluation adds negligible overhead.

17. At most one Recovery pass runs per review group.

18. `Always` mode provides a stable comparison with the Phase 6 behavior.

19. `Never` mode provides a Primary-only comparison.

20. `Auto` can be benchmarked independently for latency/quality tradeoff.

---

# Non-Goals

Do not implement:

```text
bug-likelihood scoring
risk scoring
candidate ranking
semantic risk classification
thinking fallback
thinking-on escalation
third detection pass
recursive Recovery
adaptive prompt selection
language-specific Recovery policy
new Recovery heuristics
new context retrieval
caller/callee expansion
semantic grouping changes
group-budget tuning
Turn 2 redesign
automatic TP/FP scoring
```

These are separate experiments.

---

# Suggested Implementation Order

Implement in this order:

```text
1. Identify the point immediately before Recovery invocation.

2. Add a small Recovery Execution Policy abstraction.

3. Add Auto / Always / Never behavior.

4. Preserve the existing no-remaining-regions skip.

5. Add no-valid-Recovery-context handling.

6. Add one configurable minimum-work threshold.

7. Integrate the policy per review group.

8. Ensure skipped Recovery passes Primary candidates directly
   to the existing Turn 2 path.

9. Preserve coverage state when Recovery is skipped.

10. Add stable decision-reason logging.

11. Extend benchmark summary metadata.

12. Add focused tests.

13. Run existing tests.

14. Benchmark Never vs Always vs Auto using thinking off.
```

Do not tune other review stages during this task.

---

# Design Principle

The central principle of Phase 7 is:

```text
A second non-thinking pass is cheap compared with thinking-on,
but it is not free.
```

Recovery should run when there is meaningful remaining review work, not simply because a second pass exists.

The intended production direction is:

```text
Primary Detection
    ↓
Is there enough remaining changed code
to justify another review pass?
    │
    ├── yes → Recovery Detection
    │
    └── no  → skip
    ↓
Existing Turn 2
```

Keep the decision structural, deterministic, inexpensive, and easy to benchmark.