# Task: Add Temporary File-Based Benchmark Logging for Review Runs

## Objective

Add temporary benchmark logging to the existing code review pipeline so repeated `thinking off` and `thinking on` benchmark runs can be compared without manually collecting results.

This is an experimental instrumentation change.

Do not redesign the review pipeline.

Do not change review prompts, review behavior, candidate selection, or model configuration.

The application cannot determine whether the model is running with thinking enabled or disabled, so **do not attempt to detect or infer the thinking mode**.

Instead, assign every review execution a unique `review_run_id` based primarily on the review start timestamp.

---

# Requirements

## 1. Generate a Review Run ID at Review Start

Generate one ID when the complete review begins.

Use UTC time and include milliseconds.

Recommended format:

```text
yyyyMMdd_HHmmss_fff
```

Example:

```text
20260920_015843_217
```

To avoid accidental collisions if multiple reviews start within the same millisecond, append a short uniqueness suffix.

For example:

```text
20260920_015843_217_4f2a
```

The exact suffix implementation can be:

- a short random hexadecimal value,
- a short GUID prefix,
- or another simple process-local collision-safe value.

Prefer a compact ID that remains readable and naturally sortable by start time.

Example:

```text
20260920_015843_217_a83f
```

Create this ID exactly once per review and pass/reuse it for all Turn 1, Turn 2, and final logging associated with that review.

Do not generate a new run ID for each turn.

---

# 2. Do Not Encode Thinking Mode in the Run ID

The application does not know whether the external model is configured with thinking enabled or disabled.

Therefore, do not add fields such as:

```text
thinking_on
thinking_off
reasoning_mode
```

unless that information is already explicitly available from the existing application configuration.

Do not guess it from:

- latency,
- token count,
- model response,
- model name,
- output structure,
- or any other indirect signal.

The benchmark operator will associate run IDs with thinking mode externally.

---

# 3. Write Benchmark Logs to Files

Create a temporary benchmark log directory.

For example:

```text
benchmark-logs/
```

Create one directory per review run:

```text
benchmark-logs/
└── 20260920_015843_217_a83f/
```

Store all data belonging to that review under the same directory.

Recommended layout:

```text
benchmark-logs/
└── 20260920_015843_217_a83f/
    ├── summary.json
    ├── turn1.json
    ├── turn2.txt
    └── metadata.json
```

If the current architecture has structured Turn 2 output available before rendering, JSON may be used instead of text.

Do not introduce complicated persistence infrastructure.

Plain files are sufficient.

---

# 4. Write Review Metadata

Write basic run metadata to:

```text
metadata.json
```

Include at least:

```json
{
  "review_run_id": "20260920_015843_217_a83f",
  "started_at_utc": "2026-09-19T16:58:43.217Z",
  "completed_at_utc": "2026-09-19T16:58:51.842Z"
}
```

If already available without additional architectural changes, also include useful identifiers such as:

```text
repository
merge request ID
commit SHA
file group/topic
model name
```

Do not add expensive lookups just for benchmark metadata.

Only record information already available in the review execution path.

---

# 5. Record Existing Turn Timing Information

Turn-level inference timing is already measured by the application.

Reuse the existing measurements.

Do not add a second independent timing implementation unless necessary.

Record at least:

```text
turn1 duration
turn2 duration
total review duration
```

Use milliseconds as the serialized unit.

Example:

```json
{
  "turn1_duration_ms": 4218,
  "turn2_duration_ms": 1634,
  "total_duration_ms": 6027
}
```

If existing metrics distinguish model inference time from prompt construction or other processing time, preserve those values as separate fields where convenient.

Do not change the existing timing semantics.

---

# 6. Record Turn 1 Output Exactly Enough for Benchmarking

Save the Turn 1 result before Turn 2 modifies, filters, or formats it.

The benchmark must make it possible to inspect:

```text
number of Turn 1 candidates
candidate locations
problem
evidence
impact
suggested_fix
confidence
```

If Turn 1 already deserializes into the existing `IssuesResponse`, serialize that object directly where practical.

Example:

```text
turn1.json
```

```json
{
  "issues": [
    {
      "location": "renderer.cpp: traceMesh",
      "problem": "...",
      "evidence": "...",
      "impact": "...",
      "suggested_fix": "...",
      "confidence": "high"
    }
  ]
}
```

Do not transform or normalize the candidate content solely for logging.

The logged output should reflect what Turn 1 actually returned.

---

# 7. Record Turn 2 Final Output

Save the Turn 2 result as well.

If Turn 2 produces final review text, save the exact generated review text:

```text
turn2.txt
```

If Turn 2 also has a useful structured representation already available, it may additionally be serialized.

Do not change Turn 2 output behavior for the sake of logging.

The purpose is only to capture what the existing pipeline produced.

---

# 8. Create a Machine-Friendly Summary

Create:

```text
summary.json
```

This should contain the fields useful for later automated aggregation.

Suggested structure:

```json
{
  "review_run_id": "20260920_015843_217_a83f",
  "started_at_utc": "2026-09-19T16:58:43.217Z",

  "turn1": {
    "duration_ms": 4218,
    "candidate_count": 8,
    "input_tokens": 12345,
    "output_tokens": 1834
  },

  "turn2": {
    "duration_ms": 1634,
    "input_tokens": 2941,
    "output_tokens": 811
  },

  "overall": {
    "duration_ms": 6027,
    "input_tokens": 15286,
    "output_tokens": 2645
  }
}
```

Only include token fields that are already available from the model/API response.

Do not invent estimates if exact token usage is unavailable.

If some token information is unavailable, omit it or serialize it as `null`.

---

# 9. Candidate Count Must Be Recorded

Turn 1 currently has:

```text
Maximum 10 candidates
```

Record the actual number of returned candidates.

Example:

```json
"candidate_count": 7
```

This is important for determining whether a benchmark run:

- found only a subset of the seeded bugs,
- reached the 10-candidate cap,
- or produced extra false-positive candidates.

Do not infer correctness in the application.

The logger only records outputs.

---

# 10. Do Not Automatically Score Bugs

Do not add hard-coded knowledge of the intentionally seeded bugs.

Do not implement:

```text
true positive detection
false positive detection
false negative detection
bug location matching
benchmark scoring
```

in this change.

This instrumentation should remain generic enough to log any review execution.

Scoring can be performed later from the generated files.

---

# 11. Prefer One Run Directory Over One Global Append-Only Log

Prefer:

```text
benchmark-logs/<review_run_id>/
```

over a single large log file.

Reasons:

- individual runs are easy to inspect,
- Turn 1 and Turn 2 outputs remain intact,
- failed/incomplete runs can be identified,
- runs can be copied or deleted independently,
- later aggregation scripts can simply enumerate directories.

A global index file is not required.

---

# 12. Handle Failed or Partial Reviews

Logging must not break the review.

Benchmark logging is secondary to the actual review execution.

If logging fails:

- log a warning using the existing application logger,
- continue the review where possible.

If Turn 1 succeeds but Turn 2 fails, preserve the Turn 1 files.

The metadata/summary should make an incomplete run identifiable if practical.

For example:

```json
{
  "status": "turn2_failed"
}
```

Possible simple statuses:

```text
running
completed
turn1_failed
turn2_failed
failed
```

Do not add a complex state machine.

---

# 13. Create the Run Directory Early

Create the run ID and benchmark directory near the start of the review, before Turn 1 inference begins.

This ensures that even interrupted reviews can leave useful diagnostic data.

Suggested flow:

```text
Review starts
    ↓
Generate review_run_id
    ↓
Create benchmark run directory
    ↓
Record initial metadata
    ↓
Turn 1
    ↓
Write Turn 1 result + metrics
    ↓
Turn 2
    ↓
Write Turn 2 result + metrics
    ↓
Write final summary / completion metadata
```

---

# 14. Keep Instrumentation Separate From Review Logic

Avoid scattering direct file writes throughout review logic.

Prefer a small helper such as:

```csharp
BenchmarkRunRecorder
```

or similarly named temporary component.

Possible API:

```csharp
var recorder = BenchmarkRunRecorder.Start(...);

recorder.RecordTurn1(...);
recorder.RecordTurn2(...);
recorder.Complete(...);
```

The exact design should fit the existing codebase.

Keep it simple.

This is temporary benchmarking infrastructure, not a new permanent subsystem.

---

# 15. Use Asynchronous File I/O Where Convenient

If the surrounding review execution is asynchronous, prefer async file operations.

However, do not introduce unnecessary complexity.

The amount of benchmark data is small.

Correctness and minimal invasiveness are more important than optimizing file-writing performance.

---

# 16. Preserve Raw Timing Comparability

Do not perform benchmark file writes inside the measured LLM inference interval.

For example, avoid:

```text
start Turn1 timer
→ invoke model
→ write turn1.json
→ stop Turn1 timer
```

Prefer:

```text
start Turn1 timer
→ invoke model
→ stop Turn1 timer
→ write turn1.json
```

The existing Turn 1 / Turn 2 latency values must continue representing the same work they represented before this instrumentation.

The benchmark logger itself should not materially inflate the measured inference latency.

---

# 17. Add Benchmark Logs to Git Ignore

Add the benchmark output directory to `.gitignore`.

For example:

```gitignore
benchmark-logs/
```

Do not commit benchmark result files.

---

# 18. Temporary Nature of the Change

Mark the benchmark recorder clearly as temporary if appropriate.

A short comment such as:

```csharp
// Temporary benchmark instrumentation for thinking-on/off comparison.
```

is sufficient.

Do not over-engineer configuration, persistence, database support, or UI around this functionality.

---

# Suggested Output Example

After several benchmark runs:

```text
benchmark-logs/
├── 20260920_015843_217_a83f/
│   ├── metadata.json
│   ├── summary.json
│   ├── turn1.json
│   └── turn2.txt
│
├── 20260920_020011_084_91cd/
│   ├── metadata.json
│   ├── summary.json
│   ├── turn1.json
│   └── turn2.txt
│
└── 20260920_020154_662_f127/
    ├── metadata.json
    ├── summary.json
    ├── turn1.json
    └── turn2.txt
```

The benchmark operator can then separately record:

```text
20260920_015843_217_a83f = thinking off
20260920_020011_084_91cd = thinking off
20260920_020154_662_f127 = thinking on
```

without requiring the review application to know the model's external thinking configuration.

---

# Out of Scope

Do not change:

```text
review1.en.md
review2.en.md
Turn 1 output constraints
Maximum 10 candidate behavior
candidate validation logic
severity rules
review formatting
AST/context generation
file grouping
model configuration
thinking configuration
```

Do not implement the previous Phase 1 prompt optimization as part of this task.

The benchmark must use the restored pre-modification review behavior.

---

# Acceptance Criteria

The task is complete when:

1. Every review receives one unique, timestamp-based `review_run_id`.

2. The same ID is used for Turn 1 and Turn 2.

3. Each run produces its own benchmark directory.

4. The raw Turn 1 output is persisted.

5. The Turn 2/final review output is persisted.

6. Existing per-turn inference durations are persisted.

7. Total review duration is persisted.

8. Candidate count is persisted.

9. Existing token counts are persisted when available.

10. Benchmark logging does not change review behavior.

11. Benchmark logging does not alter the semantics of existing latency measurements.

12. The application does not attempt to determine whether thinking is enabled or disabled.

13. Benchmark files are excluded from Git.

14. Partial results remain available if a later turn fails.

---

# Primary Design Principle

This change is measurement infrastructure only.

```text
Same review pipeline
Same prompts
Same code context
Same model

Only external thinking configuration differs
        ↓
Timestamp-based review_run_id
        ↓
Persist Turn 1 / Turn 2 outputs and existing metrics
        ↓
Compare runs afterward
```

Keep the implementation small, isolated, and easy to remove after the benchmark is complete.