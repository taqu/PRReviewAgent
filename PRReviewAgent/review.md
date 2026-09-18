# Phase 6 — Replace Base-Filename Grouping with Semantic Review Groups

## Objective

Replace the current base-filename review grouping strategy with a deterministic **semantic grouping** strategy.

The existing grouping logic is intentionally simple:

```text
foo.h
foo.cpp
→ group "foo"
```

This works well for obvious header / implementation pairs, but it has two important limitations:

```text
src/network/foo.cpp
src/storage/foo.cpp
```

may be grouped together even though they are unrelated.

At the same time:

```text
Foo.cpp
FooImpl.cpp
FooFactory.cpp
```

may be split into separate groups even when they form one logical change.

Phase 6 should build review groups using semantic relationships already available from path structure, pair-file resolution, and AST / symbol analysis.

The goal is:

> Group changed files that should reasonably be reviewed together, while keeping unrelated changes separate.

Do not turn grouping into repository-wide graph partitioning.

The implementation must remain deterministic, bounded, explainable, and inexpensive.

---

# Current Pipeline

The review architecture after the previous phases is approximately:

```text
Changed files
    ↓
AST / semantic analysis
    ↓
Review grouping
    ↓
budget-aware Turn 1 context
    ↓
Candidate Discovery
    ↓
candidate-specific Verification Context
    ↓
Turn 2 Verification
    ↓
Turn 3 Finalization
```

Phase 6 changes only:

```text
Review grouping
```

The remaining review stages should continue to behave as before.

---

# Current Grouping Problem

The current implementation groups by:

```csharp
Path.GetFileNameWithoutExtension(reviewContext.Path)
```

Conceptually:

```text
include/foo.h       → foo
src/foo.cpp         → foo
```

This is useful for paired files but does not represent semantic ownership reliably.

Examples of false merging:

```text
src/network/config.cpp
src/storage/config.cpp
```

Both become:

```text
config
```

even when they belong to different subsystems.

Examples of false splitting:

```text
include/image.h
src/image.cpp
src/image_loader.cpp
src/image_factory.cpp
```

These may represent one coordinated API change but become multiple groups.

---

# Target Strategy

Construct groups using three sources of affinity:

```text
1. Path / module affinity
2. Header / source pair relationships
3. Changed-symbol dependency relationships
```

Use these in that order of conceptual strength, but do not implement them as arbitrary numeric scores unless useful.

The preferred mental model is:

```text
changed files
    ↓
module boundaries
    ↓
pair-file edges
    ↓
changed-symbol dependency edges
    ↓
bounded connected groups
```

---

# Core Design Principle

Grouping is not dependency expansion.

Grouping should answer:

> Which changed files belong to the same review topic?

It should NOT answer:

> Which repository files could possibly affect this change?

Only files already selected as review targets should normally become group members.

Unchanged files remain context for Verification, not primary review-group members.

---

# Changed Files Are the Grouping Universe

Build groups primarily from:

```text
reviewContexts
```

or the equivalent collection of changed reviewable files.

Do not automatically add unchanged dependency files to the group membership.

For example:

```text
Changed:
include/foo.h
src/foo.cpp

Unchanged:
src/worker.cpp
```

The group should contain:

```text
include/foo.h
src/foo.cpp
```

The unchanged `worker.cpp` may later be used by Verification Context Resolution but should not become a group member merely because it calls `Foo`.

---

# New Component

Introduce a dedicated component such as:

```csharp
ReviewGroupBuilder
```

or:

```csharp
SemanticReviewGroupBuilder
```

Exact naming may follow project conventions.

Conceptually:

```csharp
IReadOnlyList<FileGroup> Build(
    IReadOnlyList<ReviewContext> reviewContexts,
    SemanticWorkspace workspace);
```

The grouping algorithm should not be embedded directly inside the review execution method.

It should be independently testable.

---

# Group Identity

Do not use a bare base filename as the unique identity.

Each group should have:

```text
stable internal id
display topic
member files
grouping reasons
```

Example:

```text
Group ID:
src/render:image

Display topic:
render / image

Files:
include/render/image.h
src/render/image.cpp
src/render/image_loader.cpp
```

Exact representation may differ.

The internal identity should be deterministic.

---

# Grouping Reason Metadata

Where practical, record why files were grouped.

For example:

```text
foo.h <-> foo.cpp
reason: pair

foo.cpp <-> foo_factory.cpp
reason: changed-symbol dependency

foo.cpp <-> foo_test.cpp
reason: module / test affinity
```

This metadata is useful for:

* debugging;
* tests;
* metrics;
* future tuning.

It does not need to be sent to the LLM unless useful.

---

# Stage 1 — Normalize Paths

Normalize repository-relative paths before grouping.

Handle consistently:

```text
/
\
case sensitivity according to repository/platform conventions
dot segments
```

Do not compare raw platform-specific path strings.

Preserve original repository-relative path for output.

Use normalized paths only for matching.

---

# Stage 2 — Determine Module Affinity

Use directory structure as the first broad grouping boundary.

Examples:

```text
include/render/image.h
src/render/image.cpp
src/render/image_loader.cpp
```

share module affinity:

```text
render
```

while:

```text
src/network/config.cpp
src/storage/config.cpp
```

should normally remain separate.

---

# Module Key

Introduce a deterministic module-key calculation.

Possible examples:

```text
include/render/image.h → render
src/render/image.cpp   → render
tests/render/image.cpp → render
```

The algorithm should recognize common structural roots such as:

```text
include/
src/
source/
lib/
tests/
test/
```

and derive the logical path below them.

Example:

```text
include/foo/bar.h
src/foo/bar.cpp
tests/foo/bar_test.cpp
```

may share module key:

```text
foo
```

or:

```text
foo/bar
```

depending on project structure.

Keep this configurable or heuristic-driven rather than hard-coding one repository layout.

---

# Do Not Overgeneralize Module Roots

Avoid assuming all repositories use:

```text
include/
src/
tests/
```

Support a fallback based on common path prefix.

If no logical module root can be identified, use the immediate parent directory or another stable conservative fallback.

The grouping algorithm must still work for layouts such as:

```text
engine/render/
engine/network/
tools/parser/
```

---

# Stage 3 — Preserve Explicit Pair Relationships

Existing pair-file resolution is high-confidence information.

If the system already resolved:

```text
foo.h ↔ foo.cpp
```

and both files are changed review targets, they should normally be placed in the same group.

Pair relationships should override minor path differences when clearly valid.

Example:

```text
public/foo.h
implementation/foo.cpp
```

may still belong together if the pair resolver established the relationship.

---

# Pair Relationship Strength

Treat explicit pair-file edges as strong grouping edges.

Conceptually:

```text
A --pair--> B
```

should normally merge A and B.

Do not infer pair relationships solely from identical base filenames inside the new group builder if a better pair resolver already exists.

Reuse existing pairing logic.

---

# Stage 4 — Changed-Symbol Dependency Affinity

Use AST / semantic information to detect relationships between changed files.

Only consider dependencies involving changed symbols.

Examples:

```text
FooFactory::Create
    → constructs Foo

Foo::Open
    → called by Worker::Start

ImageLoader::Load
    → returns Image
```

If both endpoint files are changed in the same MR, this relationship may justify grouping.

---

# Important Constraint

Do not group files merely because one includes another.

For example:

```text
foo.cpp includes logging.h
bar.cpp includes logging.h
```

does not imply:

```text
foo.cpp
bar.cpp
```

belong in the same review group.

Imports/includes are weak structural information.

Use them only as supporting evidence, not as a primary grouping edge.

---

# Strong Symbol Relationships

Good candidates for grouping edges include:

```text
changed caller → changed callee
changed implementation → changed interface declaration
changed derived type → changed base/interface
changed factory → changed constructed type
changed serializer → changed serialized type
changed test → changed production symbol
```

These indicate coordinated changes more strongly than generic references.

---

# Weak Symbol Relationships

Avoid merging groups solely because of:

```text
shared utility usage
shared common type
shared logging dependency
shared allocator
shared standard-library type
widely referenced enum
global configuration access
```

These relationships create giant groups and reduce review focus.

---

# Relationship Must Involve Changed Code

Prefer edges where the relevant reference itself is part of changed code.

For example:

```text
Worker::Run changed to call Foo::Open
Foo::Open also changed
```

is a strong grouping signal.

But:

```text
Worker::Run unchanged and has always called Foo::Open
Foo::Open changed
```

does not justify putting `Worker` in the group because `Worker` is not even a changed file.

---

# Stage 5 — Test Affinity

Changed tests should usually follow the production code they directly test.

Example:

```text
src/render/image.cpp
tests/render/image_test.cpp
```

should likely share a review group if the semantic index shows that the test directly references the changed production symbols.

Do not group all tests in the same module together automatically.

Prefer direct test-to-symbol relationship.

---

# Test File Naming Hints

Naming may be used as supporting evidence:

```text
foo.cpp
foo_test.cpp
test_foo.cpp
foo_tests.cpp
```

but should not be the only mechanism when semantic information is available.

Use deterministic naming heuristics as fallback.

---

# Stage 6 — Build a Small Affinity Graph

Represent changed files as nodes.

Add bounded edges for meaningful relationships:

```text
pair
same logical module
changed-symbol dependency
test-target relationship
```

Conceptually:

```text
A ---- B
|      |
C      D
```

Then construct review groups from the relevant connected structure.

However, do not blindly use unrestricted connected components.

A single weak bridge must not merge large unrelated subsystems.

---

# Edge Strength

Classify relationships into simple strengths.

Suggested:

```text
Strong:
- explicit header/source pair
- declaration/definition
- direct changed-symbol call
- direct changed interface/implementation
- direct test target

Medium:
- same logical component/module
- direct changed type dependency
- direct changed field/type relationship

Weak:
- include/import
- shared generic dependency
- shared utility
```

Weak edges should generally not merge groups by themselves.

Do not over-engineer a large scoring model.

A small discrete classification is sufficient.

---

# Merge Rules

A conservative initial strategy:

```text
1. Merge all strong edges.
2. Use medium edges when files also have module affinity.
3. Do not merge using weak edges alone.
```

This is easier to understand and test than a complex weighted clustering algorithm.

---

# Transitive Merge Protection

Avoid accidental giant groups caused by transitivity.

Example:

```text
A strongly related to B
B weakly related to C
C strongly related to D
```

Do not automatically produce:

```text
A B C D
```

if the B-C relationship is weak.

Only edges accepted by the merge policy should contribute to connected grouping.

---

# Group Size Limit

Introduce:

```text
MaxFilesPerReviewGroup
```

or equivalent.

Even semantically related changes can become too large for a focused Turn 1 review.

When a connected semantic group exceeds the limit, split it deterministically.

Do not rely only on Turn 1 context truncation to handle an oversized group.

---

# Oversized Group Splitting

When a group exceeds the configured size, split using:

```text
module/submodule boundaries
strongly connected local clusters
pair preservation
changed-symbol locality
```

Never split an explicit header / implementation pair unless unavoidable.

Prefer:

```text
render/image
render/texture
```

over arbitrary chunks based on file order.

---

# Token-Aware Group Size

If existing context-size estimation is available from Phase 5, group size may also consider estimated Turn 1 source cost.

A group with:

```text
4 huge files
```

may be more expensive than:

```text
10 tiny files
```

Consider a configurable:

```text
MaxEstimatedTurn1TokensPerGroup
```

or allow the Phase 5 context builder to report group cost.

Do not duplicate token estimation logic.

---

# Pair Preservation During Splitting

When splitting an oversized group:

```text
foo.h
foo.cpp
```

must normally remain together.

Treat explicit pair edges as indivisible units during the first split pass.

The same applies to tightly coupled declaration / definition pairs.

---

# Group Topic Generation

Generate a useful deterministic display topic.

Avoid topics such as:

```text
foo
```

when ambiguous.

Prefer:

```text
render/image
network/config
storage/config
parser/lexer
```

Possible topic sources:

```text
logical module
dominant changed symbol
paired base name
common semantic component
```

Do not ask an LLM to generate group names.

---

# Topic Examples

Example:

```text
Files:
include/render/image.h
src/render/image.cpp
src/render/image_loader.cpp

Topic:
render/image
```

Another:

```text
Files:
src/network/config.cpp
include/network/config.h

Topic:
network/config
```

Another:

```text
Files:
src/parser/lexer.cpp
tests/parser/lexer_test.cpp

Topic:
parser/lexer
```

---

# Same Filename in Different Modules

This case must be explicitly tested:

```text
src/network/config.cpp
src/storage/config.cpp
```

They must not be grouped solely because both have base name:

```text
config
```

This is one of the primary motivations for Phase 6.

---

# Related Files with Different Names

Also explicitly support:

```text
image.cpp
image_loader.cpp
image_factory.cpp
```

when changed-symbol relationships show coordinated behavior.

Do not require identical base names.

---

# Cross-Module Changes

Sometimes a valid change spans module boundaries.

Example:

```text
api/foo.h
backend/foo_impl.cpp
```

or:

```text
parser/ast.cpp
semantic/resolver.cpp
```

Do not enforce module boundaries as hard barriers.

A strong changed-symbol relationship may merge files across modules.

Module affinity is a default boundary, not an absolute rule.

---

# Public API Changes

Changed public interfaces may affect multiple changed implementations.

Example:

```text
include/backend.h
src/linux_backend.cpp
src/windows_backend.cpp
```

If all implementations are changed because of the same interface change, they may belong in one group if the size remains manageable.

Use interface/implementation relationships as strong semantic edges.

---

# Inheritance

If a changed base class and changed derived class are directly related:

```text
Base
Derived
```

they may be grouped.

Do not traverse the entire inheritance hierarchy.

Only direct changed relationships are relevant for grouping.

---

# Factory / Product Relationship

If a changed factory implementation directly constructs a changed product type:

```text
FooFactory::Create → Foo
```

this can be a grouping edge.

Again, require both files to be changed review targets.

---

# Call Graph Usage

Use only direct changed-symbol call relationships.

Do not use:

```text
callers of callers
callees of callees
```

for grouping.

Phase 6 must not introduce multi-hop semantic expansion.

---

# Reference Graph Usage

Generic symbol references should be weaker than call or declaration relationships.

For example:

```text
file A references constant from file B
```

does not necessarily justify grouping.

Use reference relationships only when:

* the referenced symbol is itself changed; and
* the reference is directly relevant to the changed logic; and
* module affinity or another supporting relationship exists.

---

# Include / Import Information

Include/import information may help identify module context but should not directly drive merging.

Treat:

```text
#include "foo.h"
```

as weak evidence.

The stronger relationship is:

```text
changed symbol in caller uses changed declaration in foo.h
```

Prefer semantic identity over text-level include edges.

---

# New Files

New files may have limited historical semantic relationships.

Group them using:

```text
module path
direct changed-symbol references
test naming
pair relationships
```

Do not isolate every new file automatically.

---

# Deleted Files

Deleted files should still participate in grouping using available pre-change semantic information where reliable.

If semantic data is unavailable, fall back to:

```text
module path
pair relationship
previous path
```

Do not fail grouping because a current source representation is absent.

---

# Renamed Files

Treat a rename as identity continuity where possible.

Example:

```text
old/path/foo.cpp
→
new/path/foo.cpp
```

Do not treat the old and new names as separate semantic files.

Use the new path for grouping output.

Retain rename metadata separately.

---

# Generated Files

Preserve existing review filtering.

Generated files excluded from review should not become group nodes merely because semantic analysis sees references to them.

---

# Group Ordering

Groups must have deterministic ordering.

Recommended order:

```text
normalized topic
then minimum normalized member path
```

or another stable strategy.

Do not depend on dictionary iteration order.

---

# File Ordering Inside Group

Use stable ordering.

Recommended:

```text
1. public headers / declarations
2. private headers
3. main implementation
4. related implementations
5. tests
6. other files
```

Within each category, sort by normalized path.

Pair files should remain adjacent where practical.

---

# Grouping Determinism

The same:

```text
changed files
AST relationships
configuration
```

must produce the same grouping.

Do not use:

```text
LLM output
randomness
hash iteration order
timing-dependent discovery
```

for grouping decisions.

---

# Explainability

Provide diagnostic information such as:

```text
Group render/image:
- include/render/image.h
  joined via pair with src/render/image.cpp

- src/render/image_loader.cpp
  joined via changed-symbol dependency ImageLoader::Load -> Image
```

This may be emitted only in debug logs or internal metadata.

Explainability will be important when a grouping decision produces unexpected review behavior.

---

# Metrics

Record at least:

```text
changed file count
group count
average files per group
maximum files per group
pair-edge count
symbol-dependency edge count
module-affinity merge count
oversized group split count
```

Also correlate with review metrics from Phase 5:

```text
Turn 1 input tokens per group
Turn 1 latency per group
candidate count per group
verified count per group
final finding count per group
```

This will show whether semantic grouping improves both context quality and latency.

---

# Useful Derived Metrics

Make it possible to calculate:

```text
files per candidate
tokens per candidate
tokens per verified finding
groups with zero candidates
groups split by budget
```

A large number of zero-candidate groups may indicate over-splitting.

Very large groups with many unrelated candidates may indicate under-splitting.

---

# Do Not Optimize Group Count Alone

Fewer groups are not inherently better.

For example:

```text
1 group with 30 unrelated files
```

is worse than:

```text
5 coherent groups
```

even if it requires more Turn 1 calls.

Optimize for:

```text
semantic coherence
bounded context
review accuracy
latency
```

rather than minimum LLM invocation count.

---

# Avoid Excessive Fragmentation

At the same time, do not create one group per file by default.

That loses cross-file change context, especially for C/C++:

```text
header ↔ implementation
interface ↔ implementation
factory ↔ object
test ↔ production code
```

Grouping should preserve meaningful coordinated changes.

---

# Interaction with Turn 1

Turn 1 behavior must not change semantically.

Each new semantic group should be passed to the existing:

```text
Turn1ContextBuilder
```

The context builder remains responsible for:

```text
full-file vs semantic-scope selection
diff anchors
budget enforcement
semantic summary
```

The group builder should not duplicate context formatting.

---

# Interaction with Semantic Summary

The Phase 4 semantic summary should be generated per new semantic group.

Do not include relationships to unrelated groups unless they are useful as a compact boundary hint.

For example, it is acceptable to say:

```text
Foo::Open is called by changed symbol Worker::Run in another review group.
```

only if that information helps discovery.

Do not automatically merge groups merely to avoid such cross-group references.

---

# Interaction with Verification

Verification remains candidate-specific.

A candidate may require source from another review group or an unchanged file.

That is acceptable.

The VerificationContextResolver should remain independent from Turn 1 grouping boundaries.

This is important:

```text
review grouping != verification context boundary
```

Turn 2 may cross group boundaries when necessary to verify a concrete candidate.

---

# Cross-Group Dependencies

If two groups have a direct changed-symbol dependency but were kept separate due to size or module boundaries, preserve enough relationship metadata for Verification.

Do not duplicate the complete second group into Turn 1.

Verification can resolve the required symbol later.

---

# Interaction with Learned Rules

Do not redesign learned-rule retrieval.

If RAG currently runs once per review execution, preserve that.

If rules are attached to all groups, continue doing so unless existing architecture says otherwise.

Semantic grouping should not change rule lifecycle semantics.

---

# Interaction with Candidate IDs

Candidate IDs may currently restart per group.

Preserve existing behavior unless it causes ambiguity in execution-wide tracking.

If IDs need to become globally unique, prefer deterministic composition such as:

```text
g2-c3
```

or internal group ID + candidate index.

Do not change public output merely for internal convenience.

---

# Group IDs

If persistence or metrics require stable group IDs, generate them deterministically from:

```text
topic
member paths
```

or another stable canonical representation.

Do not use random GUIDs when a deterministic ID is sufficient.

---

# Configuration

Introduce only necessary configuration.

Potential values:

```text
MaxFilesPerReviewGroup
MaxEstimatedTokensPerReviewGroup
EnableSemanticGrouping
```

A feature flag may be useful for benchmarking against the old grouping.

Do not expose dozens of edge weights as configuration in the first implementation.

Keep relationship policy in code unless real tuning data shows a need.

---

# Feature Flag / Fallback

Provide a safe fallback to the previous grouping behavior during rollout if practical.

Example:

```text
GroupingMode:
    BaseFilename
    Semantic
```

This makes A/B benchmarking and rollback easier.

Do not maintain two architectures indefinitely if semantic grouping proves stable.

---

# Fallback Behavior

If semantic analysis is unavailable or fails:

```text
use path-aware conservative grouping
```

rather than immediately falling back to bare base filename.

A reasonable fallback might be:

```text
logical module + base filename
```

Example:

```text
network/config
storage/config
```

This already avoids one major class of collision.

---

# Path-Aware Fallback

Conceptually:

```text
src/network/config.cpp → network/config
src/storage/config.cpp → storage/config
```

If pair metadata exists, merge the pair.

This fallback should remain deterministic and safe without AST.

---

# Group-Build Failure

A grouping exception must not abort the whole review.

Log the failure and use the conservative fallback grouping.

The review pipeline should continue.

---

# Tests

Add focused unit tests for grouping.

At minimum cover the following.

## Header / implementation pair

```text
include/foo.h
src/foo.cpp
```

Expected:

```text
same group
```

---

## Same base name, different modules

```text
src/network/config.cpp
src/storage/config.cpp
```

Expected:

```text
different groups
```

unless a strong explicit semantic relationship exists.

---

## Different names, direct changed-symbol relationship

```text
src/image.cpp
src/image_loader.cpp
```

where:

```text
ImageLoader::Load → Image
```

and both relevant symbols are changed.

Expected:

```text
same group
```

when within limits.

---

## Unrelated files in same directory

```text
src/render/image.cpp
src/render/shader.cpp
```

with no meaningful changed-symbol relationship.

Expected:

```text
do not merge solely because both are under render
```

unless module policy intentionally uses that directory as one small component.

Use the conservative interpretation.

---

## Changed test and production file

```text
src/foo.cpp
tests/foo_test.cpp
```

with direct test reference.

Expected:

```text
same group
```

---

## Unchanged caller

```text
src/foo.cpp changed
src/worker.cpp unchanged
```

Expected:

```text
worker.cpp is not added to the group
```

---

## Strong cross-module relationship

```text
api/foo.h
backend/foo_impl.cpp
```

with explicit interface/implementation relation.

Expected:

```text
same group
```

---

## Weak include relationship

Two files include the same changed header but otherwise have no direct changed-symbol relationship.

Expected:

```text
do not merge solely because of shared include
```

---

## Oversized semantic group

Create more related files than:

```text
MaxFilesPerReviewGroup
```

Expected:

```text
deterministic semantic split
```

with explicit pair relationships preserved.

---

## Token-heavy group

A group exceeds estimated Turn 1 budget despite a small file count.

Expected:

```text
group splitting or bounded handling according to configured policy
```

without arbitrary file loss.

---

## New file

A new implementation and its changed test are grouped correctly.

---

## Deleted file

A deleted implementation remains associated with its changed declaration when available.

---

## Rename

A renamed file keeps semantic identity and does not appear twice.

---

## Missing AST

Grouping uses path-aware fallback.

---

## Determinism

Repeated runs with identical data produce identical:

```text
group count
member sets
group order
topics
```

---

# Integration Tests

Add at least one representative C++ merge request with:

```text
include/render/image.h
src/render/image.cpp
src/render/image_loader.cpp
src/network/config.cpp
src/storage/config.cpp
tests/render/image_test.cpp
```

Use semantic relationships such that:

```text
image.h
image.cpp
image_loader.cpp
image_test.cpp
```

form one coherent review group.

Ensure:

```text
network/config.cpp
```

and:

```text
storage/config.cpp
```

remain separate.

Verify the resulting groups flow correctly through Turn 1, Verification, and Finalization.

---

# Benchmark

Compare:

```text
Baseline:
base-filename grouping

New:
semantic grouping
```

Using the same MRs and model configuration.

Measure:

```text
group count
Turn 1 calls
Turn 1 total input tokens
Turn 1 total latency
average Turn 1 tokens per group
candidate count
verified count
final finding count
overall review latency
```

---

# Quality Evaluation

Manually inspect whether semantic grouping improves detection of cross-file issues such as:

```text
header / implementation mismatch
changed API + changed caller
ownership across related classes
factory / object contract
test / implementation consistency
interface / implementation compatibility
```

Also inspect for regressions caused by:

```text
unrelated files merged together
related files split unnecessarily
groups becoming too large
cross-group dependencies being lost
```

---

# Diagnostic Interpretation

## Too many tiny groups

Possible causes:

```text
semantic edges too strict
module affinity too weak
test relations not recognized
pair resolver incomplete
```

Do not immediately loosen all edge rules.

Inspect real examples first.

---

## Very large groups

Possible causes:

```text
module affinity too broad
generic references treated as strong
include edges incorrectly used for merging
transitive merging too permissive
```

Prefer tightening weak relationships before lowering hard group-size limits.

---

## Candidate recall decreases

Check whether related changed files that previously shared Turn 1 context were separated.

Use group diagnostics to identify missing semantic edges.

---

## Turn 1 latency increases

Check:

```text
group count
group source size
duplicate source across groups
```

Do not assume more groups are necessarily the cause.

Smaller groups may still reduce total generation cost.

---

# Migration Strategy

Recommended implementation order:

```text
1. Extract existing grouping into a dedicated ReviewGroupBuilder abstraction.
2. Add path normalization and logical module detection.
3. Add path-aware fallback grouping.
4. Integrate existing pair relationships.
5. Add changed-symbol dependency edges.
6. Add test-target relationships.
7. Add strong / medium / weak edge classification.
8. Build deterministic groups.
9. Add group-size and token-size safeguards.
10. Add deterministic topic generation.
11. Add grouping diagnostics / metrics.
12. Add feature flag for old vs semantic grouping.
13. Add unit and integration tests.
14. Benchmark against base-filename grouping.
15. Make semantic grouping the default only after validation.
```

Keep each stage independently testable where practical.

---

# Compatibility Requirements

Preserve:

```text
Turn 1 Candidate Discovery contract
Turn1ContextBuilder
context budgeting
CandidateIssue schema
VerificationContextResolver
Turn 2 Verification semantics
VerifiedIssue schema
Turn 3 Finalization
severity policy
learned-rule tracking
GitLab output behavior
English/Japanese output
review execution metrics
```

Grouping should change which changed files are reviewed together, not what the individual review stages mean.

---

# Do Not Implement Yet

Explicitly exclude:

```text
repository-wide graph partitioning
multi-hop dependency grouping
LLM-generated groups
LLM-generated group names
dynamic group merging during review
dynamic group splitting based on LLM output
cross-group candidate sharing
verification batching
parallel verification redesign
RAG redesign
severity changes
review-policy changes
```

These belong to later optimization work if needed.

---

# Acceptance Criteria

Phase 6 is complete when:

1. Base-filename grouping is no longer the primary grouping strategy.
2. Group construction is implemented in a dedicated component.
3. Paths are normalized consistently.
4. Logical module affinity is available.
5. Existing header / source pair relationships are preserved.
6. Direct changed-symbol relationships can merge related changed files.
7. Direct test-to-production relationships can influence grouping.
8. Generic includes/imports do not merge groups by themselves.
9. Same-name files in unrelated modules remain separate.
10. Related files with different names can be grouped.
11. Only changed review-target files normally become group members.
12. Unchanged dependencies remain Verification context rather than group members.
13. Strong cross-module relationships can override module boundaries.
14. Multi-hop dependency traversal is not used.
15. Groups are bounded by file and/or estimated context size.
16. Oversized groups are split deterministically.
17. Header / implementation pairs are preserved during splitting where practical.
18. Group topics are deterministic and path-aware.
19. Group ordering and member ordering are deterministic.
20. Semantic-analysis failure has a safe path-aware fallback.
21. Group diagnostics and metrics are available.
22. Existing review stages require no semantic changes.
23. Unit and integration tests cover the major grouping cases.
24. Benchmark results compare semantic grouping against the previous strategy.
25. The project builds successfully.

---

# Implementation Principle

Do not group files because their names happen to look similar.

Do not group files because they happen to share a common dependency.

Group files because the change itself shows that they belong to the same review concern.

The intended architecture after Phase 6 is:

```text
Changed files
    ↓
path / module analysis
    ↓
pair relationships
    ↓
changed-symbol relationships
    ↓
bounded semantic review groups
    ↓
Turn 1 Candidate Discovery
    ↓
candidate-specific Verification
    ↓
Turn 3 Finalization
```

The core principle is:

> Review together what changed together semantically.

Use path information to establish boundaries, pair relationships to preserve obvious C/C++ structure, and changed-symbol relationships to connect coordinated changes that filenames alone cannot identify.
