# Phase 8 — Add Adaptive End-to-End Review Execution Policy

## Objective

Introduce an adaptive execution policy that selects an appropriate Verification strategy based on the actual review workload.

The current architecture supports:

```text
Sequential single-candidate Verification
Sequential batched Verification
Bounded parallel batched Verification
```

Phase 8 should stop treating one fixed execution configuration as optimal for every merge request.

The goal is:

> Select the cheapest review execution strategy that preserves review quality for the current workload.

The decision should be deterministic, observable, configurable, and based on already available workload metrics.

Do not introduce model-based or LLM-based scheduling decisions.

---

# Current Architecture

The review pipeline is approximately:

```text
Semantic review groups
    ↓
Turn 1: Candidate Discovery
    ↓
CandidateIssue[]
    ↓
bounded VerificationContext resolution
    ↓
VerificationBatchBuilder
    ↓
Verification execution
    ↓
VerifiedIssue[]
    ↓
Turn 3: Finalization
```

Phase 7 made Verification execution configurable using concepts such as:

```text
MaxCandidatesPerBatch
MaxVerificationBatchTokens
MaxConcurrentVerificationBatches
```

Phase 8 should introduce a policy layer above these execution settings.

---

# Primary Design Principle

Do not optimize one request in isolation.

Optimize the complete review.

The policy should consider:

```text
candidate count
candidate context size
group count
group context size
expected Verification call count
local inference concurrency behavior
historical measured execution cost
```

The policy should then choose an execution plan.

---

# Target Architecture

Introduce:

```text
Review workload
    ↓
ExecutionPolicy
    ↓
ExecutionPlan
    ↓
VerificationExecutor
```

Conceptually:

```text
ReviewWorkloadProfile
    ↓
VerificationExecutionPlanner
    ↓
VerificationExecutionPlan
```

The executor should not contain adaptive policy logic itself.

---

# Scope

Implement:

* workload profiling;
* execution-plan model;
* deterministic execution planner;
* rule-based adaptive mode selection;
* batch-size selection;
* concurrency selection;
* context-aware safeguards;
* configuration overrides;
* execution-plan logging;
* policy metrics;
* benchmarks.

Do not implement:

* reinforcement learning;
* LLM-based scheduling;
* online self-modifying policy;
* dynamic model switching;
* dynamic reasoning-mode switching;
* repository-wide scheduling;
* quality prediction using an LLM;
* adaptive severity policy.

---

# Part 1 — Workload Profile

Introduce a compact workload representation.

Suggested structure:

```csharp
public sealed class ReviewWorkloadProfile
{
    public int GroupCount { get; init; }

    public int CandidateCount { get; init; }

    public int EstimatedVerificationTokensTotal { get; init; }

    public int EstimatedVerificationTokensMax { get; init; }

    public int EstimatedVerificationTokensAverage { get; init; }

    public int LargeCandidateCount { get; init; }

    public int TruncatedContextCount { get; init; }
}
```

Exact fields may differ.

Only include values that can influence execution strategy.

---

# Candidate-Level Cost Estimate

For each candidate, estimate:

```text
Verification context input size
+
fixed prompt overhead
+
expected output allowance
```

Do not attempt to predict exact generation length.

A conservative estimate is sufficient.

---

# Group-Level Profile

Where useful, preserve group-local information:

```text
group candidate count
group total verification context size
largest candidate context
```

This matters because Phase 7 batching should remain inside semantic groups.

---

# Part 2 — Execution Plan

Introduce an explicit execution-plan model.

Example:

```csharp
public sealed class VerificationExecutionPlan
{
    public required VerificationExecutionMode Mode { get; init; }

    public int MaxCandidatesPerBatch { get; init; }

    public int MaxBatchInputTokens { get; init; }

    public int MaxConcurrentBatches { get; init; }

    public string Reason { get; init; }
}
```

Possible modes:

```text
SequentialSingle
SequentialBatch
ParallelSingle
ParallelBatch
```

If `ParallelSingle` is not useful in the current architecture, it may be omitted.

Keep the model minimal.

---

# Plan Must Be Immutable During Execution

Once a plan is selected for a review execution, do not continuously change it based on each batch completion.

Phase 8 should use:

```text
plan once
execute plan
measure result
```

not:

```text
continuously adapt while running
```

This keeps behavior reproducible.

More dynamic scheduling can be considered later if needed.

---

# Part 3 — Planner

Introduce a component such as:

```text
VerificationExecutionPlanner
```

Responsibilities:

```text
read workload profile
read configured limits
select mode
select batch size
select concurrency
produce explanation
```

It must not:

```text
call the LLM
resolve AST context
execute batches
modify candidates
```

---

# Rule-Based Policy

Use simple deterministic rules.

Do not build a complicated optimizer.

A good initial policy may resemble:

```text
if candidate_count <= 1:
    SequentialSingle

else if candidate_count is small
     and total context is small:
    SequentialBatch

else if candidate_count is moderate
     and batch contexts fit comfortably:
    ParallelBatch with low concurrency

else:
    conservative batched execution
```

Exact thresholds must be configurable and benchmark-driven.

---

# Example Policy Shape

Conceptually:

```text
0 candidates
    → no Verification

1 candidate
    → SequentialSingle

2–4 small candidates
    → SequentialBatch

5–10 moderate candidates
    → ParallelBatch, concurrency 2

large contexts
    → smaller batches

very large candidate
    → single-candidate batch

high truncation rate
    → conservative execution
```

Do not hard-code these exact numbers unless benchmark data supports them.

---

# Part 4 — Context-Aware Batch Sizing

Batch size should depend on context size.

Example:

```text
many tiny candidates
    → batch 4

medium candidates
    → batch 2

very large candidates
    → batch 1
```

Use both:

```text
MaxCandidatesPerBatch
MaxBatchInputTokens
```

Batch size must never override token safety limits.

---

# Effective Batch Size

The execution planner may produce:

```text
preferred batch size = 4
```

but the batch builder still enforces:

```text
token budget
```

Therefore actual batch size may be smaller.

This is expected.

---

# Part 5 — Adaptive Concurrency

Concurrency should depend on expected request size and workload shape.

Example:

```text
small prompts
many batches
    → concurrency 2 or higher if benchmarked safe

large prompts
few batches
    → concurrency 1

memory-heavy contexts
    → concurrency 1
```

Do not base concurrency solely on candidate count.

---

# Local Inference Constraints

The planner must assume that local inference may suffer from:

```text
GPU memory pressure
KV-cache pressure
request contention
reduced tokens/sec under concurrency
```

Therefore, high concurrency should require explicit configuration or benchmark support.

Prefer conservative defaults.

---

# Runtime Capability Configuration

If useful, expose a local runtime capability profile.

Example:

```text
Inference:
  MaxRecommendedConcurrentRequests: 2
```

The planner must never exceed the configured hard maximum.

---

# Part 6 — Hard Limits vs Adaptive Preferences

Separate:

```text
hard safety limits
```

from:

```text
adaptive preferred values
```

Example:

```text
HardMaxConcurrentBatches = 4
PreferredConcurrency = planner result
```

The planner may choose:

```text
2
```

but never:

```text
5
```

Hard limits always win.

---

# Part 7 — Manual Override

Provide a way to disable adaptive behavior.

Example:

```text
VerificationExecutionPolicy:
    Fixed
    Adaptive
```

In Fixed mode, use explicit Phase 7 settings.

In Adaptive mode, use the planner.

This is important for:

```text
benchmarking
debugging
rollback
```

---

# Fixed Mode Compatibility

Fixed mode must reproduce existing Phase 7 behavior.

Do not change semantics when adaptive mode is disabled.

---

# Part 8 — Planner Inputs

Use only data already available before Verification execution.

Allowed inputs include:

```text
candidate count
candidate category
estimated context size
group count
group size
truncated VerificationContext flags
configured model/runtime limits
```

Avoid inputs that require additional expensive analysis.

---

# Candidate Category Use

Candidate category may influence scheduling only when it predicts context size or isolation needs.

For example:

```text
lifetime candidate
```

may often require larger context than:

```text
simple local correctness candidate
```

However, do not introduce category-specific correctness behavior.

Category-based scheduling should remain optional and conservative.

---

# Do Not Predict Issue Validity

The planner must never prioritize or drop candidates based on a guess that they are probably false positives.

All eligible candidates still follow the existing verification policy.

Execution planning is not review judgment.

---

# Part 9 — Plan Reason

Every adaptive plan should include a concise deterministic reason.

Example:

```text
Mode=ParallelBatch
BatchSize=2
Concurrency=2
Reason="8 candidates, average verification context 4.1K tokens, no oversized candidates."
```

This should be available in diagnostics.

Do not use free-form LLM-generated explanations.

---

# Part 10 — Execution Integration

Preferred flow:

```text
Candidates
    ↓
VerificationContext resolution
    ↓
ReviewWorkloadProfileBuilder
    ↓
VerificationExecutionPlanner
    ↓
VerificationExecutionPlan
    ↓
VerificationBatchBuilder
    ↓
VerificationExecutor
```

The batch builder should receive the selected plan.

---

# Planner Timing

Build the execution plan after candidate VerificationContexts are available.

This is important because actual context sizes are better than guesses from Turn 1.

Do not plan concurrency or batch size before context resolution unless needed for an early coarse estimate.

---

# Context Resolution Remains Independent

The planner must not change:

```text
which context items are selected
```

for a candidate.

Phase 5 context-budget policy remains authoritative.

Phase 8 changes execution strategy, not verification evidence.

---

# Part 11 — Per-Group Planning vs Whole-Review Planning

Prefer whole-review planning for concurrency limits, but allow group-local batching.

Conceptually:

```text
whole review
    ↓
global concurrency plan

each semantic group
    ↓
local batches
```

Do not plan unrelated groups independently in a way that causes total concurrency to exceed the global limit.

---

# Global Concurrency Coordinator

If multiple semantic groups can verify concurrently, use a single global Verification concurrency limiter.

Do not create:

```text
2 concurrent batches per group
```

across ten groups accidentally.

The configured/planned concurrency limit should apply across the review execution.

---

# Cross-Group Parallelism

Phase 7 avoided cross-group batching.

Keep that rule.

However, batches from separate groups may execute concurrently if the global concurrency limit permits it.

This can reduce wall-clock latency while preserving semantic batching boundaries.

---

# Deterministic Scheduling

Where several batches are ready, schedule deterministically.

Recommended:

```text
group order
then batch order
```

Completion may be asynchronous, but scheduling order should remain stable.

---

# Part 12 — Adaptive Small-Review Fast Path

Small reviews should avoid unnecessary orchestration overhead.

Example:

```text
1 group
1 candidate
small context
```

should resolve directly to:

```text
SequentialSingle
```

No batching complexity should be added.

---

# Zero Candidate Fast Path

Preserve:

```text
0 candidates
→ no Verification planner execution needed
```

or allow the planner to return:

```text
NoOp
```

if cleaner.

Do not invoke the LLM.

---

# Part 13 — Adaptive Large-Review Protection

For a large review:

```text
many groups
many candidates
large contexts
```

the planner should prefer predictable bounded work over maximum concurrency.

Potential strategy:

```text
small batches
low concurrency
strict hard limits
```

Avoid creating very large batches merely to reduce call count.

---

# Overload Protection

If workload exceeds configured operational limits, do not crash or produce an unsafe prompt.

Reuse Phase 5 candidate caps and context budgets.

Phase 8 does not increase hard review workload limits.

---

# Part 14 — Historical Metrics

Do not require historical performance data for the initial implementation.

The first version should be rule-based.

However, structure metrics so that later tuning can answer:

```text
For this workload shape,
which mode was fastest?
```

Persist or log enough data for offline analysis.

---

# Optional Static Runtime Profile

If helpful, allow configuration such as:

```text
RuntimeProfile:
  SerialOptimized
  LowConcurrency
  HighThroughput
```

but do not introduce this unless it materially simplifies deployment-specific defaults.

The primary interface should remain explicit numeric limits.

---

# Part 15 — Metrics

Record the chosen plan per review execution.

At minimum:

```text
execution mode
planned batch size
planned concurrency
candidate count
estimated total verification tokens
average candidate context tokens
maximum candidate context tokens
batch count
actual max concurrency
Verification wall-clock duration
sum of batch durations
```

---

# Plan Effectiveness Metrics

Make it possible to compare:

```text
planned mode
actual latency
actual throughput
actual batch failures
fallback count
```

This will support tuning.

---

# Fallback Metrics

Record if execution deviated from the plan due to:

```text
batch split
single-candidate fallback
context oversize
model failure
```

Adaptive policy quality should be evaluated using actual behavior, not just the initial plan.

---

# Part 16 — Benchmark Matrix

Benchmark both Fixed and Adaptive modes.

Representative fixed baselines:

```text
Single / concurrency 1
Batch 2 / concurrency 1
Batch 2 / concurrency 2
Batch 4 / concurrency 2
```

Compare against:

```text
Adaptive
```

using identical workloads.

---

# Workload Classes

Benchmark at least:

```text
Tiny
- 1 candidate

Small
- 2–3 candidates

Medium
- 4–8 candidates

Large
- 9+ candidates

Large-context
- few candidates with large contexts

Mixed
- small and large candidate contexts together

Multi-group
- several semantic review groups
```

---

# Benchmark Dimensions

Measure:

```text
overall review latency
Verification wall-clock latency
number of Verification calls
input tokens
output tokens
batch failure rate
fallback rate
verified count
final finding count
```

---

# Quality Must Remain Constant

Adaptive execution must not change review semantics.

For the same candidates and contexts, compare:

```text
Fixed baseline result
Adaptive result
```

Any quality difference should be investigated as a batching/concurrency artifact.

---

# Part 17 — Conservative Initial Policy

Start with a deliberately simple policy.

Example concept:

```text
candidate_count == 1
    → single

small total context
    → sequential batch

moderate workload
    → batch + concurrency 2

large individual contexts
    → smaller batches

very large average context
    → concurrency 1
```

Do not add many thresholds in the first version.

---

# Configuration Thresholds

Possible configuration:

```text
AdaptiveVerification:
  SmallCandidateCountThreshold
  SmallTotalTokenThreshold
  LargeCandidateTokenThreshold
  PreferredSmallBatchSize
  PreferredMediumBatchSize
  PreferredConcurrency
```

Avoid exposing every internal heuristic as configuration.

Keep only values that are likely to require deployment tuning.

---

# Threshold Validation

Validate configuration relationships.

Examples:

```text
preferred concurrency <= hard max concurrency
preferred batch size <= hard max batch size
thresholds > 0
```

Fail configuration loading clearly if values are invalid according to existing conventions.

---

# Part 18 — Planner Tests

Add unit tests for plan selection.

At minimum:

## Zero candidates

Expected:

```text
No Verification
```

## One candidate

Expected:

```text
SequentialSingle
```

## Several tiny candidates

Expected:

```text
SequentialBatch
```

or configured small-review strategy.

## Moderate workload

Expected:

```text
ParallelBatch
```

when allowed.

## Large candidate contexts

Expected:

```text
smaller batch size
```

## Runtime concurrency hard limit

Planner never exceeds it.

## Adaptive disabled

Fixed configuration is used.

## Determinism

Same profile + configuration produces identical plan.

---

# Batch Integration Tests

Verify the selected plan actually affects the batch builder.

Example:

```text
plan batch size = 2
5 candidates
```

Expected:

```text
2
2
1
```

subject to token budget.

---

# Concurrency Integration Test

If planner chooses:

```text
MaxConcurrentBatches = 2
```

the executor must never exceed two active Verification calls.

---

# Multi-Group Concurrency Test

Two review groups each have multiple batches.

Ensure the concurrency cap applies globally.

---

# Fallback Test

Planner selects:

```text
ParallelBatch
```

but one batch fails.

Existing split/retry behavior must still work.

The planner should not need to re-plan the entire review.

---

# Oversized Candidate Test

One large candidate plus several small candidates.

Expected:

```text
large candidate in single batch
small candidates batched normally
```

according to existing batch safety rules.

---

# Part 19 — Logging

Add concise diagnostics such as:

```text
VerificationPlan:
mode=ParallelBatch
candidates=7
estimated_tokens=28140
avg_candidate_tokens=4020
max_candidate_tokens=7310
batch_size=2
concurrency=2
reason="moderate candidate count and bounded contexts"
```

After execution:

```text
VerificationResult:
planned_batches=4
actual_calls=5
splits=1
wall_ms=8210
sum_call_ms=14620
```

Do not log full prompt contents.

---

# Part 20 — Rollout

Keep adaptive mode behind configuration initially.

Recommended rollout:

```text
1. Fixed mode remains default.
2. Run Adaptive mode in benchmark/test environments.
3. Compare workload classes.
4. Tune thresholds.
5. Enable Adaptive by default after results are stable.
```

If the project does not maintain separate deployment environments, retain an easy configuration rollback path.

---

# Part 21 — Do Not Add Online Self-Tuning Yet

Do not automatically rewrite thresholds based on recent executions.

Phase 8 should produce data for later tuning, but behavior should remain configuration-driven.

This ensures:

```text
reproducibility
debuggability
predictability
```

---

# Part 22 — Interaction with Turn 1

Do not adapt Turn 1 model settings in this phase.

Turn 1 remains:

```text
non-thinking Candidate Discovery
```

Do not enable thinking for large MRs automatically.

That would mix execution scheduling with review semantics.

---

# Interaction with Turn 2

Turn 2 semantics remain identical.

Only:

```text
batch size
batch scheduling
concurrency
```

may change.

---

# Interaction with Turn 3

Turn 3 remains unchanged.

Do not parallelize or batch Finalization.

It should continue to see one deterministic verified issue set per review group or existing finalization boundary.

---

# Interaction with Semantic Grouping

Phase 6 grouping remains authoritative.

The adaptive planner must not merge semantic groups.

It may schedule batches from separate groups concurrently.

---

# Interaction with Context Budgets

Phase 5 budgets remain hard constraints.

The planner must never increase context budgets to improve batching efficiency.

---

# Interaction with Learned Rules

No change.

Learned-rule retrieval and attribution remain outside execution planning.

---

# Part 23 — Failure Isolation

Planner failure itself must not fail the review.

If adaptive planning throws or receives invalid input:

```text
log error
fallback to conservative fixed mode
```

Recommended fallback:

```text
SequentialSingle
```

or the existing Phase 7 fixed configuration.

Prefer existing configured safe baseline.

---

# Plan Validation

Before execution, validate:

```text
batch size >= 1
concurrency >= 1
batch size <= hard max
concurrency <= hard max
token budget <= hard model limit
```

If invalid, fallback safely.

---

# Part 24 — Suggested Internal Architecture

Preferred structure:

```text
CandidateIssue[]
    ↓
VerificationContextResolver
    ↓
VerificationWorkItem[]
    ↓
ReviewWorkloadProfileBuilder
    ↓
VerificationExecutionPlanner
    ↓
VerificationExecutionPlan
    ↓
VerificationBatchBuilder
    ↓
VerificationExecutor
    ↓
VerifiedIssue[]
```

Keep planning separate from execution.

---

# Class Responsibility Summary

## ReviewWorkloadProfileBuilder

Computes workload statistics.

## VerificationExecutionPlanner

Chooses execution strategy.

## VerificationBatchBuilder

Builds batches under the selected plan and hard limits.

## VerificationExecutor

Executes batches with bounded concurrency and fallback.

Do not collapse all four into one service.

---

# Migration Strategy

Recommended order:

```text
1. Introduce ReviewWorkloadProfile.
2. Introduce VerificationExecutionPlan.
3. Extract existing fixed Verification settings into a plan-compatible form.
4. Implement VerificationExecutionPlanner.
5. Add Adaptive/Fixed configuration mode.
6. Integrate planner before batch construction.
7. Add global concurrency coordination if not already present.
8. Add plan diagnostics.
9. Add planner tests.
10. Add end-to-end adaptive tests.
11. Benchmark workload classes.
12. Tune conservative thresholds.
13. Enable Adaptive mode only after validation.
```

---

# Compatibility Requirements

Preserve:

```text
CandidateIssue semantics
VerificationContextResolver
Verification context budgets
VerificationBatchBuilder safety limits
Turn 2 verification policy
batch failure fallback
VerifiedIssue semantics
Turn 3 Finalization
semantic grouping
learned-rule attribution
GitLab output behavior
review language behavior
```

Adaptive planning must remain an execution concern only.

---

# Do Not Implement Yet

Explicitly exclude:

```text
historical ML-based scheduling
online threshold learning
dynamic model switching
dynamic thinking-mode switching
speculative duplicate execution
multi-model verification
quality-score prediction
repository-wide scheduling
dynamic Turn 1 strategy changes
RAG adaptation
severity adaptation
```

These may be evaluated later if the simpler policy is insufficient.

---

# Acceptance Criteria

Phase 8 is complete when:

1. Verification workload profiling exists.
2. An explicit VerificationExecutionPlan exists.
3. A deterministic planner selects execution mode.
4. Adaptive and Fixed execution modes are supported.
5. Single-candidate reviews use a cheap fast path.
6. Batch size can adapt to context size.
7. Concurrency can adapt to workload size.
8. Hard context and concurrency limits always override adaptive preferences.
9. Planning is performed after VerificationContext resolution.
10. Semantic groups are never merged by the planner.
11. Global Verification concurrency is bounded across groups.
12. Planner output includes an explainable reason.
13. Plan selection is deterministic.
14. Planner failure has a safe fixed-mode fallback.
15. Existing batch split/retry behavior still works.
16. Adaptive execution does not change Turn 2 semantics.
17. Turn 3 behavior remains unchanged.
18. Plan and actual execution metrics are recorded.
19. Fixed-mode baseline behavior remains reproducible.
20. Unit tests cover major workload shapes.
21. Integration tests cover multi-group and failure scenarios.
22. Benchmarks compare Adaptive against several fixed configurations.
23. Quality results remain materially equivalent to the fixed baseline.
24. Production thresholds are configurable.
25. The project builds successfully.

---

# Implementation Principle

Do not make every review follow the same execution strategy.

A one-candidate review and a ten-candidate review are different workloads.

A review with 2K-token contexts and one with 20K-token contexts are also different workloads.

The intended Phase 8 architecture is:

```text
review workload
    ↓
measure shape
    ↓
choose conservative execution plan
    ↓
batch within hard limits
    ↓
execute with bounded concurrency
    ↓
observe actual performance
```

The core principle is:

> Adapt execution cost, not review semantics.

The same candidate should receive the same factual Verification task regardless of whether it is executed alone, inside a batch, sequentially, or concurrently.
