# Phase 6 — Optimize the Recovery Detection Prompt

## Objective

Optimize the dedicated Recovery Detection prompt introduced in Phase 4 so the second `thinking off` pass is better at finding **additional high-confidence correctness issues** in the focused Recovery context created by Phase 5.

This phase must change **Recovery prompt behavior only**.

Do not change:

```text
Primary Detection
semantic grouping
group budgets
changed-region tracking
Recovery context selection
number of LLM calls
candidate union
Turn 2
thinking configuration
```

The experiment should answer:

```text
Can a small amount of explicit review guidance make
Recovery Detection more effective without increasing
false positives or duplicate findings?
```

---

# Existing Pipeline

Preserve the current architecture:

```text
Review Groups
    ↓
Primary Detection
thinking off
    ↓
Coverage Resolver
    ↓
Focused Recovery Context
    ↓
Recovery Detection
thinking off
    ↓
Candidate Union
    ↓
Existing Turn 2
    ↓
Final Review
```

Phase 6 changes only:

```text
Recovery Detection prompt
```

---

# Background

The Recovery pass now receives:

```text
unreported changed regions
+
focused local source context
+
limited strong semantic supporting context
+
compact already-reported finding summary
```

The remaining problem is that a non-thinking model may still fail to apply some useful review strategies consistently.

The Recovery prompt should make those strategies explicit without turning into a large generic checklist.

The goal is not to make the model speculate harder.

The goal is:

```text
use the available code more systematically
```

---

# 1. Keep Primary Detection Prompt Unchanged

Do not modify:

```text
review1.en.md
```

or the equivalent Primary Detection template.

Primary Detection must remain the benchmark control.

Only the Recovery prompt should change.

This is required so the Phase 5 vs Phase 6 benchmark isolates the effect of prompt optimization.

---

# 2. Preserve the Existing Recovery Output Schema

Recovery Detection must continue returning the same candidate structure as Primary Detection.

Required fields remain:

```text
location
problem
evidence
impact
suggested_fix
confidence
```

Allowed confidence values remain:

```text
high
medium
```

Do not output low-confidence candidates.

Do not add:

```text
severity
valid
verification_status
region_id
review_score
```

to the LLM output schema.

Internal provenance and region mapping remain application responsibilities.

---

# 3. Preserve Existing Output Constraints

Keep the Recovery output constraints equivalent to:

```text
Maximum 10 candidates

confidence must be high or medium

Do not output low-confidence candidates

Do not assign Critical, Major, or Minor severity

Do not use Markdown

Do not write the final review

Output JSON only
```

Do not relax these constraints.

---

# 4. Recovery Is Additive, Not Corrective

The prompt must make the role clear:

```text
Primary Detection already produced some findings.

Recovery Detection should search the remaining changed regions
for additional issues.
```

Do not ask Recovery to:

```text
validate Primary findings
reject Primary findings
rank Primary findings
rewrite Primary findings
```

Recovery is another Detection pass.

It is not Verification.

---

# 5. Do Not Tell the Model That Remaining Regions Contain Bugs

Avoid language such as:

```text
Find bugs missed by the first pass.
```

This creates a strong false-positive incentive.

Prefer:

```text
Review the remaining changed regions for additional
high- or medium-confidence correctness issues.

A remaining changed region is not necessarily incorrect.
```

This sentence should be explicit.

---

# 6. Make Changed-Code Evidence the Primary Standard

Recovery should report an issue only when it can build a concrete chain from code to incorrect behavior.

For every candidate, require the model to establish:

```text
1. What changed?
2. Why can the new behavior be incorrect?
3. Under what condition does it matter?
4. What observable behavior can be affected?
5. What minimal change would correct it?
```

If the provided code cannot establish this chain with at least medium confidence:

```text
do not report the candidate
```

---

# 7. Explicitly Check Internal Consistency

Add a compact instruction to compare the changed expression against directly related code already present in the Recovery context.

Examples:

```text
paired calculations
sibling implementations
related fields
nearby coordinate calculations
matching encode/decode logic
initialization/cleanup symmetry
producer/consumer assumptions
```

The prompt should encourage consistency comparison when supporting code is actually present.

Do not tell the model to assume that differing implementations are necessarily bugs.

---

# 8. Add a Small Set of High-Value Review Heuristics

Use a compact list derived from observed benchmark failure modes.

The Recovery model should pay particular attention to changed expressions involving:

```text
- dimension or index mismatches
- swapped values with the same or compatible types
- order-sensitive operations
- incorrect variable selection
- inconsistent use of related implementations
- sign or direction reversals
- changed conditions or comparison direction
```

Keep this list short.

Do not build a language textbook into the prompt.

---

# 9. Explicitly Highlight Same-Type Argument Swaps

One important class of subtle bugs is:

```text
arguments remain type-compatible
but their semantic roles are different
```

The prompt should explicitly instruct the model to inspect changed calls where argument order changed.

Examples include:

```text
atan2(y, x)
cross(normal, tangent)
texture(u, v)
copy(source, destination)
min/max bounds
width/height
row/column
```

Do not instruct the model that these examples are always bugs.

Use wording equivalent to:

```text
If argument order or semantically distinct same-type values changed,
verify whether the operation is order-sensitive.
```

---

# 10. Explicitly Check Dimension and Coordinate Consistency

For changed arithmetic involving:

```text
width
height
row
column
u
v
x
y
stride
index
```

ask the model to compare related operations.

Examples:

```text
row * width + column

index / width
index % width

u normalized by width
v normalized by height
```

The prompt should look for inconsistency rather than impose one universal convention.

---

# 11. Explicitly Check Variable-Role Substitution

A common correctness bug changes a valid variable into another type-compatible but semantically different variable.

Examples:

```text
sampled direction → surface normal
width → height
u → v
vertex.u → vertex.v
source → destination
```

Recovery should actively inspect changed variable substitutions.

Suggested guidance:

```text
When a changed expression replaces one variable with another,
verify that the new variable still represents the semantic quantity
required at that point.
```

---

# 12. Explicitly Check Order-Sensitive Math

Changes involving non-commutative operations deserve attention.

Examples:

```text
cross(a, b)
subtract(a, b)
divide(a, b)
matrix multiplication
quaternion multiplication
atan2(y, x)
ordered comparisons
```

The prompt should not assume:

```text
f(a, b) == f(b, a)
```

merely because both forms compile.

Again, report only when the available code establishes that the new ordering is incorrect.

---

# 13. Compare With Sibling Implementations When Available

If Recovery context contains a related implementation, use it as evidence.

Example:

```text
renderSphere:
row * settings.height + column

renderMesh:
row * settings.width + column
```

This inconsistency is useful evidence.

However:

```text
different code != bug
```

The candidate must still explain why one behavior violates the local semantics.

---

# 14. Keep Specification-Dependent Cases Conservative

This requirement is important.

Some changed expressions may be valid or invalid depending on project-specific conventions that are not visible in the provided code.

Examples may include:

```text
coordinate-system handedness
texture orientation
UV convention
azimuth convention
API-specific coordinate convention
```

If the correctness of a change depends on such missing specification:

```text
do not report it as a bug unless the provided code establishes
the convention strongly enough.
```

The prompt should prefer omission over speculation.

This is especially important for controlling false positives.

---

# 15. Do Not Treat Generic Best Practices as Bugs

Recovery should focus on correctness regressions.

Do not report:

```text
style preferences
optional refactoring
naming issues
performance micro-optimizations
missing comments
subjective API design
generic defensive programming suggestions
```

unless they directly cause concrete incorrect behavior.

---

# 16. Prefer Changed-Code Regressions

Recovery should prioritize findings caused by the current change.

Do not report unrelated pre-existing issues unless the existing review policy already requires that behavior.

If both old and new code are visible, reason about:

```text
what behavior changed because of the patch
```

rather than reviewing the entire surrounding implementation as fresh code.

---

# 17. Do Not Repeat Already-Reported Findings

The Recovery prompt receives a compact list of Primary findings.

Make the exclusion instruction explicit:

```text
Do not emit a candidate that describes the same underlying problem
as an already-reported finding.
```

Same function does not necessarily mean same problem.

For example:

```text
traceMesh roughness UV swap

traceMesh wrong lighting direction
```

are separate issues.

Deduplicate by underlying problem, not symbol name.

---

# 18. Avoid Near-Duplicate Variants

Do not report multiple candidates for:

```text
the same changed expression
the same root cause
the same incorrect behavior
```

with slightly different wording.

Prefer one concrete candidate.

The existing Turn 2 still performs final deduplication, but Recovery should avoid obvious duplication proactively.

---

# 19. Use Confidence Conservatively

Define Recovery confidence explicitly.

## High

Use when:

```text
the changed code itself,
or direct comparison with visible related code,
strongly establishes incorrect behavior
```

## Medium

Use when:

```text
the issue is well-supported by the visible code
but requires one reasonable semantic assumption
```

Do not emit a candidate when confidence would be low.

Do not use `medium` merely to include speculative concerns.

---

# 20. Suggested Recovery Reasoning Procedure

The prompt may include a compact internal review sequence such as:

```text
For each remaining changed region:

1. Identify exactly what expression or behavior changed.

2. Determine the semantic role of the changed values.

3. Compare it with nearby or related code when available.

4. Check whether operand order, variable selection, dimensions,
   direction, indexing, or conditions changed meaningfully.

5. Determine whether the new behavior can be shown to be incorrect
   from the provided code.

6. If yes, emit one candidate.

7. If the conclusion depends on missing specification or uncertain
   conventions, do not emit it.
```

Keep this concise.

The prompt should guide analysis without becoming verbose.

---

# 21. Do Not Add Chain-of-Thought Output Requirements

Do not ask the model to output:

```text
reasoning steps
analysis
scratchpad
step-by-step thought process
```

The final candidate fields already contain the necessary review evidence.

The model should output JSON only.

---

# 22. Keep Evidence Concrete

Good evidence:

```text
The changed expression derives `col` with `% SampleHeight`,
while the row calculation uses `/ SampleWidth` and `phi`
normalizes the resulting column by `SampleWidth`.
```

Weak evidence:

```text
This looks suspicious and may cause unexpected behavior.
```

The Recovery prompt should require the first style.

---

# 23. Keep Impact Proportional

Do not encourage exaggerated impact statements.

Impact should describe the concrete affected behavior.

Good:

```text
The sampled environment direction can map to the wrong column
when width and height differ.
```

Avoid:

```text
This could catastrophically break rendering.
```

unless the visible code justifies that conclusion.

---

# 24. Suggested Fix Must Validate the Diagnosis

Keep `suggested_fix` required.

A plausible minimal fix serves as a useful consistency check.

Example:

```text
Change `index % SampleHeight` to `index % SampleWidth`.
```

If the model cannot state a concrete minimal correction with confidence, that is evidence the candidate may be too speculative.

Do not propose large refactors unless necessary.

---

# 25. Keep Recovery Prompt Compact

The optimized prompt must remain significantly smaller than a large exhaustive review manual.

Prefer:

```text
core role
evidence standard
short heuristic list
spec-dependent caution
output constraints
```

Avoid dozens of specialized rules.

Recovery latency and input tokens are still important.

---

# 26. Do Not Modify Recovery Context Construction

Phase 5 already defines Recovery context construction.

Do not change:

```text
target-region selection
fragment merging
supporting-context selection
context budget
reported-region exclusion
source-range construction
```

during Phase 6.

If the prompt exposes a context-builder bug, record it separately rather than fixing both in the same benchmark experiment.

---

# 27. Do Not Modify Semantic Grouping

Preserve:

```text
C/C++ semantic grouping
C/C++ group budgeting
non-C/C++ one-file grouping
```

exactly as in the Phase 5 baseline.

---

# 28. Do Not Modify Primary Detection

Primary Detection remains unchanged.

This includes:

```text
prompt
context
candidate limit
candidate schema
timing
```

The experiment concerns Recovery only.

---

# 29. Do Not Modify Candidate Union

Preserve existing:

```text
Primary candidates
+
Recovery candidates
↓
conservative duplicate handling
↓
existing Turn 2
```

Do not introduce candidate ranking or scoring.

---

# 30. Do Not Modify Turn 2

Turn 2 remains responsible for:

```text
candidate validation
false-positive rejection
deduplication
project policy
severity
final language
formatting
```

Do not move these responsibilities into Recovery.

---

# 31. Add Prompt-Version Metadata

Because this phase is benchmark-driven, record which Recovery prompt version produced each run.

For example:

```text
recovery_prompt_version = "phase6-v1"
```

Use a simple constant or existing template/version mechanism.

Do not add a complex prompt registry.

This makes later comparison easier.

---

# 32. Preserve Benchmark Metrics

Continue recording:

```text
Primary candidate count

Recovery candidate count
Recovery latency
Recovery input/output tokens
Recovery newly-reported region count
Recovery duplicate count

combined candidate count
Turn 2 final finding count
total latency
```

No metric semantics should change in this phase.

---

# 33. Add Recovery Yield Metrics if Convenient

If the existing benchmark recorder supports it without automatic bug scoring, calculate structural metrics such as:

```text
recovery_candidates_per_target_region

recovery_candidates_per_1000_input_tokens

recovery_duplicate_ratio
```

Do not classify candidates as TP/FP automatically unless such scoring already exists.

External benchmark analysis remains the source of correctness labels.

---

# 34. Tests

Prompt changes should be supported with lightweight tests where the project supports template validation.

At minimum verify:

## Case 1 — Output Constraints Present

The Recovery template clearly requires:

```text
JSON only
Maximum 10 candidates
high/medium confidence only
no severity
```

---

## Case 2 — Remaining Regions Are Not Assumed Buggy

The template explicitly states that remaining changed regions are not necessarily incorrect.

---

## Case 3 — Duplicate Avoidance

The template clearly tells Recovery not to repeat already-reported findings.

---

## Case 4 — Specification-Dependent Caution

The template tells the model not to report issues whose correctness depends on missing project-specific conventions.

---

## Case 5 — Changed-Code Focus

The template tells the model to focus on correctness issues caused by the changed code.

---

## Case 6 — Same-Type / Order-Sensitive Changes

The template includes concise guidance for:

```text
swapped same-type values
order-sensitive operations
dimension / coordinate consistency
variable-role substitution
```

---

# 35. Benchmark Plan

Compare:

```text
A. Phase 5
   focused Recovery context
   existing Recovery prompt
   thinking off

B. Phase 6
   same focused Recovery context
   optimized Recovery prompt
   thinking off
```

Keep identical:

```text
benchmark corpus
model
Primary prompt
Primary context
semantic groups
group budgets
Recovery context
number of turns
candidate schema
Turn 2
```

Run multiple trials.

---

# 36. Primary Benchmark Metrics

Measure:

```text
Primary detections

Recovery additional detections
Recovery duplicate findings
Recovery false positives

Final findings

Recovery input tokens
Recovery output tokens
Recovery latency
Total latency
```

Especially compare:

```text
Recovery useful yield
vs
Recovery false-positive rate
```

---

# 37. Important Benchmark Interpretation

Do not treat every intentionally modified line as a mandatory finding.

Some seeded changes may be specification-dependent and not provably incorrect from the available code.

The important benchmark categories are:

```text
code-provable correctness issues

specification-dependent changes

false positives
```

Phase 6 should primarily improve detection of **code-provable issues**.

Do not optimize the prompt to force detection of ambiguous cases.

---

# 38. Success Criteria

Phase 6 is successful if:

1. Only the Recovery prompt changes.

2. Primary Detection remains unchanged.

3. Recovery context remains unchanged from Phase 5.

4. Recovery continues to review only remaining changed regions.

5. The prompt explicitly avoids assuming remaining regions are buggy.

6. The prompt requires concrete changed-code evidence.

7. The prompt includes a small set of high-value correctness heuristics.

8. Same-type argument/value swaps receive explicit attention.

9. Order-sensitive operations receive explicit attention.

10. Dimension and coordinate consistency receive explicit attention.

11. Variable-role substitutions receive explicit attention.

12. Specification-dependent cases are handled conservatively.

13. Low-confidence issues remain prohibited.

14. Already-reported findings are not intentionally repeated.

15. Candidate schema is unchanged.

16. Turn 2 is unchanged.

17. No additional LLM calls are introduced.

18. Prompt version is recorded for benchmark comparison.

19. Recovery false-positive rate does not materially regress.

20. Recovery additional detection of code-provable issues improves or remains stable.

---

# Non-Goals

Do not implement:

```text
new Recovery context retrieval
caller/callee expansion
repository-wide context search
new semantic grouping
group-budget tuning
third detection pass
recursive Recovery
thinking fallback
thinking-on escalation
candidate scoring
candidate ranking
new Turn 2 logic
automatic bug labeling
automatic TP/FP scoring
language-specific Recovery prompts
```

These are separate experiments.

---

# Suggested Implementation Order

Implement in this order:

```text
1. Locate the existing Recovery prompt template.

2. Preserve its current output schema and hard constraints.

3. Clarify the Recovery role:
   additional detection only.

4. Add the explicit statement that remaining regions
   are not necessarily buggy.

5. Strengthen the concrete-evidence requirement.

6. Add the compact high-value heuristic section.

7. Add conservative handling for specification-dependent cases.

8. Strengthen duplicate avoidance.

9. Keep the prompt concise.

10. Add a simple Recovery prompt version identifier.

11. Update or add prompt-template tests.

12. Run existing tests.

13. Benchmark Phase 5 vs Phase 6 using thinking off.
```

Do not modify Recovery context construction during this task.

---

# Suggested Prompt Shape

The final Recovery prompt should approximately follow this structure:

```text
Role
    ↓
Review only remaining changed regions

Important caution
    ↓
Remaining does not mean buggy

Already-reported exclusion
    ↓
Do not repeat existing findings

Evidence standard
    ↓
Changed code must establish incorrect behavior

Focused review heuristics
    ↓
same-type swaps
order-sensitive operations
dimension / coordinate consistency
variable-role substitutions
related implementation consistency

Specification caution
    ↓
Do not guess project conventions

Candidate requirements
    ↓
location
problem
evidence
impact
suggested_fix
confidence

Output constraints
    ↓
max 10
high/medium only
no severity
JSON only
```

Keep each section concise.

---

# Design Principle

The central principle of Phase 6 is:

```text
Recovery has less code to inspect,
so give it a better checklist for how to inspect that code.
```

Do not make Recovery more speculative.

Make it more systematic.

The desired result is:

```text
focused context
+
explicit high-value review strategy
+
strict evidence threshold
=
more useful additional findings
without a false-positive explosion
```