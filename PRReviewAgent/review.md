# Phase 2 — Add Budgeted Splitting to C/C++ Semantic Review Groups

## Objective

Add deterministic size limits and splitting rules to the C/C++ semantic grouping introduced in Phase 1.

Phase 1 improves review context by grouping semantically related C/C++ changes together.

Phase 2 must prevent those semantic groups from becoming too large, noisy, or expensive.

The core rule is:

```text
Preserve strong semantic relationships,
but split oversized C/C++ groups before they become poor LLM review contexts.
```

This phase changes **group sizing and splitting only**.

Do not change the LLM review pipeline, prompts, or number of model calls.

---

# Existing Language Policy

Preserve the Phase 1 language policy.

## C / C++

Use semantic multi-file grouping.

## All Other Languages

Continue using:

```text
one changed file = one review group
```

Do not add budgeting or semantic splitting logic to non-C/C++ files unless required mechanically by the existing common grouping abstraction.

A standalone non-C/C++ file should remain a standalone group.

---

# Current Architecture

Conceptually:

```text
Changed files
    ↓
Language partitioning
    ↓
C/C++ semantic grouping
    ↓
Review groups
    ↓
Existing Turn 1
    ↓
Existing Turn 2
```

Phase 2 should extend only this part:

```text
C/C++ semantic grouping
    ↓
group budget enforcement
    ↓
final C/C++ review groups
```

Target:

```text
Changed C/C++ files / symbols
        ↓
Semantic relation graph
        ↓
Initial semantic groups
        ↓
Budget evaluation
        ↓
Split oversized groups
        ↓
Final deterministic review groups
```

---

# 1. Add an Explicit Group Budget

Introduce a clear budget for C/C++ review groups.

The budget should preferably use the same token estimation mechanism already used elsewhere in the application.

If an existing token estimator is available, reuse it.

Preferred primary budget:

```text
estimated prompt/context tokens
```

Optional secondary safety limits may include:

```text
maximum files per group
maximum changed symbols per group
maximum changed lines per group
```

Do not introduce all possible limits unless they are useful in the current architecture.

Keep the implementation simple.

---

# 2. Prefer Token Budget Over File Count

A fixed file count is not sufficient by itself.

For example:

```text
2 very large files
```

may be more expensive than:

```text
8 small files
```

Therefore, if token estimation is already available, use estimated context size as the primary limit.

Conceptually:

```text
if estimated_group_tokens <= group_budget:
    keep group unchanged
else:
    split group
```

Do not use exact model-token serialization if that would require expensive additional processing.

A stable approximation is sufficient for grouping.

---

# 3. Do Not Split Groups That Already Fit

If an initial semantic group fits within the configured budget, keep it intact.

Do not split groups simply because they contain several files.

Example:

```text
foo.h
foo.cpp
foo_loader.cpp
```

may remain one group if:

```text
semantic relationships are strong
AND
estimated context fits within the budget
```

Phase 2 should only intervene when necessary.

---

# 4. Preserve Strongest Semantic Relationships First

When splitting an oversized group, preserve the strongest relationships.

Use the following default priority unless the existing Phase 1 implementation already has a clearer equivalent:

```text
1. Same containing class / struct
2. Header / source pair
3. Direct relationship between changed symbols
4. Shared meaningful direct callee / constructor
5. Existing base-filename affinity
6. Same module / directory
```

When a split is required:

```text
weak relationships should break before strong relationships
```

For example:

```text
Foo declaration
Foo implementation
Foo::Open
Foo::Close
```

should remain together if possible.

A weakly related neighboring file should be moved to another group first.

---

# 5. Header / Source Pairs Should Be Difficult to Split

C/C++ header/source pairs are a primary reason semantic grouping exists.

Avoid splitting:

```text
foo.h
foo.cpp
```

unless keeping them together would violate a hard context limit that cannot reasonably be satisfied otherwise.

Similarly:

```text
include/render/foo.hpp
src/render/foo.cpp
```

should remain together when recognized as a pair.

Treat header/source affinity as a high-priority edge during splitting.

---

# 6. Same-Type Changes Should Prefer the Same Group

If several changed methods belong to the same class or struct, keep them together where practical.

Example:

```text
Environment::sample
Environment::pdf
Environment::eval
```

should normally remain in one group.

If the group is too large, split unrelated neighboring symbols before splitting methods belonging to the same type.

Only split one type across multiple groups when the budget makes it unavoidable.

---

# 7. Avoid Giant Connected Components

A semantic relation graph may create large transitive groups.

For example:

```text
A calls CommonHelper
B calls CommonHelper
C calls CommonHelper
D calls CommonHelper
...
```

must not automatically merge an entire subsystem.

Phase 2 should ensure that weak or generic relationships cannot create effectively repository-sized groups.

Shared common utilities should not act as strong grouping edges.

If Phase 1 already filters generic callees, preserve that behavior.

If necessary, strengthen the filter rather than allowing a giant group.

---

# 8. Splitting Must Be Deterministic

For identical changed files and semantic relations, the final split groups must be identical across runs.

Do not use:

```text
random partitioning
LLM-based splitting
embedding clustering
unstable hash iteration
```

Use deterministic ordering and deterministic tie-breaking.

Good tie-breakers include:

```text
repository path
source location
symbol qualified name
existing changed-file order
```

Benchmark comparisons depend on stable grouping.

---

# 9. Prefer Cohesive Subgroups Over Equal-Sized Subgroups

Do not try to make every group have the same size.

Semantic cohesion is more important than perfectly balanced token counts.

Prefer:

```text
Group A: 70% of budget, strongly related
Group B: 40% of budget, strongly related
```

over:

```text
Group A: 55%, mixed relationships
Group B: 55%, mixed relationships
```

The goal is not load balancing.

The goal is good review context.

---

# 10. Suggested Splitting Strategy

A simple deterministic strategy is preferred.

One acceptable approach:

```text
1. Build the initial semantic group.

2. Sort semantic relations by strength.

3. Build subgroups around the strongest connected relationships.

4. Add related files/symbols while the group remains within budget.

5. When adding another unit would exceed the budget,
   start or assign it to another subgroup.

6. Preserve deterministic ordering and tie-breaking.
```

Another acceptable implementation is a weighted graph partition if Phase 1 already uses such a model.

Do not introduce a sophisticated clustering dependency solely for Phase 2.

---

# 11. Choose an Appropriate Grouping Unit

Use the grouping unit already established in Phase 1.

If semantic grouping is internally symbol-based but review contexts are file-based, preserve that architecture.

For example:

```text
Symbol relationships
    ↓
derive file affinity
    ↓
final file-oriented review group
```

Do not rewrite the whole review pipeline into symbol-fragment prompts as part of Phase 2.

---

# 12. Account for Prompt Overhead

If the token budget is based on estimated review input size, leave room for fixed prompt overhead.

Do not fill a group to the model's full context capacity.

Conceptually:

```text
usable_group_budget =
    configured_context_budget
    - fixed_prompt_reserve
    - output_reserve
```

If the project already has context budgeting utilities, reuse their conventions.

Do not introduce a conflicting context-limit system.

---

# 13. Use a Conservative Default Budget

Choose a default that leaves enough room for:

```text
review instructions
MR metadata
diff formatting
Turn 1 output
other fixed context
```

Do not assume that the entire model context can be consumed by source files.

If no existing application-level value makes the correct budget obvious, define the budget as a named configuration constant rather than scattering numeric literals.

Example concept:

```text
CppReviewGroupTokenBudget
```

The exact naming should follow existing project conventions.

---

# 14. Keep the Budget Configurable

Prefer a single clearly defined configuration value or constant.

Avoid hard-coding the same threshold in several locations.

If the application already has review configuration, integrate with it.

Otherwise, keep the change minimal.

This is still benchmark-driven work, so avoid building an elaborate configuration framework.

---

# 15. Non-C/C++ Behavior Must Not Change

For:

```text
.py
.cs
.java
.rs
.ts
.go
...
```

continue using:

```text
one changed file = one review group
```

Do not merge or split those files based on C/C++ semantic budgets.

A large Python file is still one per-file group in Phase 2.

Handling very large individual non-C/C++ files is outside this task.

---

# 16. Mixed-Language Merge Requests

Mixed-language behavior should remain straightforward.

Example input:

```text
include/foo.h
src/foo.cpp
src/foo_loader.cpp
scripts/build.py
tools/check.cs
```

Possible result:

```text
C/C++ Group 1:
  include/foo.h
  src/foo.cpp

C/C++ Group 2:
  src/foo_loader.cpp

Per-file Group 3:
  scripts/build.py

Per-file Group 4:
  tools/check.cs
```

The C/C++ groups may split because of budget or semantic cohesion.

The non-C/C++ files remain independent.

---

# 17. Add Budget Diagnostics

Extend grouping diagnostics so benchmark logs explain why a group was kept or split.

For each final C/C++ group, log at least:

```text
group ID
files
changed symbols if available
estimated tokens
configured budget
```

For a split, also log a concise reason.

Example:

```text
C++ semantic group g0 exceeded budget:
  estimated_tokens: 18420
  budget: 12000

Split result:
  g0a: 10380 tokens
  g0b: 7440 tokens
```

If relationship information is available, optionally record:

```text
preserved:
  header/source: foo.h <-> foo.cpp

split weak relation:
  module affinity: foo_loader.cpp
```

Keep logs concise.

Do not add these diagnostics to the LLM prompt.

---

# 18. Extend Benchmark Metadata

If benchmark logging already records groups, add budget-related fields.

Example:

```json
{
  "group_id": "g0",
  "strategy": "cpp-semantic",
  "estimated_tokens": 10380,
  "token_budget": 12000,
  "files": [
    "include/foo.h",
    "src/foo.cpp"
  ]
}
```

Useful run-level metrics include:

```text
initial semantic group count
final group count
number of split groups
largest group tokens
average group tokens
```

Do not add automatic bug scoring.

---

# 19. Do Not Modify Review Prompts

Do not change:

```text
review1.en.md
review2.en.md
```

Do not add text explaining semantic relationships.

Do not add new review heuristics.

The experiment must isolate:

```text
semantic grouping + group budgeting
```

from prompt changes.

---

# 20. Do Not Add Additional Detection Passes

Do not implement the planned recovery pass in Phase 2.

The pipeline remains:

```text
Final semantic groups
    ↓
Existing Turn 1
    ↓
Existing Turn 2
```

No additional LLM call should be introduced.

Recovery detection will be evaluated separately later.

---

# 21. Preserve Candidate Constraints

Do not change the current Turn 1 constraints:

```text
Maximum 10 candidates
confidence = high or medium
no low-confidence candidates
JSON only
```

Phase 2 must not affect candidate schema or review policy.

---

# 22. Tests

Add focused tests for budget behavior.

## Case 1 — Small C++ Group

Input:

```text
foo.h
foo.cpp
```

Estimated size:

```text
below budget
```

Expected:

```text
one group
```

---

## Case 2 — Oversized C++ Group

Input:

```text
foo.h
foo.cpp
foo_loader.cpp
foo_cache.cpp
```

Estimated size:

```text
above budget
```

Expected:

```text
multiple groups
```

while preserving the strongest relationships.

---

## Case 3 — Preserve Header / Source Pair

Input relationships:

```text
foo.h <-> foo.cpp       strong
foo.cpp <-> helper.cpp  weak
```

Budget requires splitting.

Expected:

```text
foo.h
foo.cpp
```

remain together.

`helper.cpp` should split first.

---

## Case 4 — Preserve Same Class

Changed symbols:

```text
Foo::Open
Foo::Close
Foo::Reset
Bar::Update
```

If splitting is required and `Bar::Update` is weakly related:

Expected:

```text
Foo methods remain together
Bar::Update is split first
```

---

## Case 5 — Determinism

Run grouping multiple times with identical input.

Expected:

```text
same groups
same membership
same ordering
```

---

## Case 6 — Non-C/C++

Input:

```text
a.py
b.py
```

Expected:

```text
2 standalone groups
```

Budgeted semantic splitting must not merge or otherwise alter them.

---

## Case 7 — Mixed Language

Input:

```text
foo.h
foo.cpp
foo_extra.cpp
build.py
Tool.cs
```

Expected:

```text
C/C++ groups follow semantic + budget rules
build.py is standalone
Tool.cs is standalone
```

---

# 23. Benchmark Plan

After implementation, compare:

```text
A. Phase 1
   semantic grouping
   no budget-driven split beyond existing behavior
   thinking off

B. Phase 2
   semantic grouping
   budgeted deterministic splitting
   thinking off
```

Keep everything else identical.

Measure:

```text
per-bug detection frequency
false-positive frequency

Turn 1 latency
Turn 2 latency
total latency

input tokens
output tokens

group count
group token sizes
largest group size
number of budget splits
```

The key question is:

```text
Does limiting oversized semantic groups reduce context noise
without losing the benefit of semantic grouping?
```

---

# 24. Success Criteria

Phase 2 is successful if:

1. C/C++ semantic groups have an explicit size budget.

2. Groups below the budget remain unchanged.

3. Oversized groups are split deterministically.

4. Strong semantic relationships are preserved preferentially.

5. Header/source pairs are preserved where practical.

6. Same-class changes stay together where practical.

7. Weak directory/module relationships break before strong relationships.

8. Giant connected components are avoided.

9. Non-C/C++ files remain one-file-per-group.

10. No review prompt changes are made.

11. No additional LLM calls are introduced.

12. Existing Turn 1 and Turn 2 behavior is unchanged.

13. Grouping diagnostics expose budget and split decisions.

14. Tests cover normal, oversized, mixed-language, and deterministic cases.

15. Review quality does not materially regress against Phase 1.

---

# Non-Goals

Do not implement:

```text
second detection pass
recovery review
verification turn
candidate union
candidate-driven context retrieval
unchanged caller/callee expansion
full-file context redesign
prompt changes
review heuristics
LLM-based grouping
embedding-based grouping
generic semantic grouping for non-C/C++ languages
automatic benchmark scoring
```

These are separate phases.

---

# Suggested Implementation Order

Implement in this order:

```text
1. Locate the Phase 1 C/C++ semantic grouping boundary.

2. Reuse the existing token estimator if available.

3. Add one explicit C/C++ group budget.

4. Calculate estimated size for initial semantic groups.

5. Leave groups below budget unchanged.

6. Implement deterministic splitting for oversized groups.

7. Preserve header/source and same-type relationships first.

8. Add stable ordering and tie-breaking.

9. Add grouping/budget diagnostics.

10. Extend benchmark metadata if useful.

11. Add focused unit tests.

12. Run existing tests.

13. Benchmark Phase 1 vs Phase 2 using thinking off.
```

Do not proceed into recovery detection during this task.

---

# Design Principle

The central principle of Phase 2 is:

```text
Semantic grouping should improve comparison,
not create oversized contexts that hide the changes being reviewed.
```

Preserve the smallest set of strongly related C/C++ code needed for effective review.

Break weak relationships before strong ones.

Keep the implementation deterministic, narrow, benchmarkable, and easy to roll back.
