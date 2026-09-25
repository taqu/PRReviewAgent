# Task: Use Per-File Review Groups for Non-C/C++ Files

## Objective

Restrict semantic multi-file grouping to C and C++ sources.

For all non-C/C++ languages, use simple per-file review grouping:

```text
one changed file = one review group
```

This change should keep the grouping behavior predictable and prevent Phase 1 from expanding into generic cross-file semantic grouping for every supported language.

---

## Required Behavior

### C / C++

C and C++ files may continue to use semantic grouping.

Examples include:

```text
.c
.cc
.cpp
.cxx
.h
.hh
.hpp
.hxx
```

C/C++ grouping may combine related changed files based on the Phase 1 grouping logic, such as:

```text
header/source relationships
same containing class or struct
direct relationships between changed symbols
```

Do not change or weaken the intended C/C++ semantic grouping behavior as part of this task.

---

### All Other Languages

For every non-C/C++ file:

```text
one changed file = one review group
```

Examples:

```text
foo.py      -> its own review group
Foo.cs      -> its own review group
Foo.java    -> its own review group
foo.rs      -> its own review group
foo.ts      -> its own review group
foo.go      -> its own review group
```

Do not combine non-C/C++ files based on:

```text
same directory
same module
same class name
shared callees
shared imports
similar filenames
semantic relationships
```

in this phase.

---

## Mixed-Language Changes

If a merge request contains both C/C++ and other languages, handle them independently.

Example:

```text
include/foo.h
src/foo.cpp
scripts/build.py
tools/check.py
```

Expected grouping:

```text
Group 1:
  include/foo.h
  src/foo.cpp

Group 2:
  scripts/build.py

Group 3:
  tools/check.py
```

Non-C/C++ files must not be merged into a C/C++ semantic group merely because they are in the same directory or reference related concepts.

---

## Language Classification

Use the existing language/file-type detection mechanism if one already exists.

Do not introduce a new parser or large language-classification subsystem for this change.

If grouping is currently based on file extensions, keep the implementation simple and deterministic.

C/C++ extensions should be recognized consistently with the rest of the codebase.

Avoid broad assumptions such as treating every header-like file as C++ unless that is already the project's convention.

---

## Preserve Existing Review Pipeline

Do not modify:

```text
Turn 1 prompt
Turn 2 prompt
candidate schema
Maximum 10 candidate rule
confidence rules
severity logic
review formatting
thinking configuration
model configuration
number of LLM calls
```

This task changes grouping behavior only.

---

## Preserve Deterministic Ordering

Non-C/C++ file groups should be created in a stable order.

Prefer the existing changed-file order or normalized repository path ordering.

The same set of changed files should produce the same group order across benchmark runs.

Do not depend on unstable dictionary/hash iteration order.

---

## Avoid Duplicate Inclusion

Each non-C/C++ changed file should belong to exactly one review group.

Do not duplicate the same non-C/C++ file across multiple groups.

For this phase:

```text
non-C/C++ file
    ↓
exactly one group
```

---

## C/C++ and Header Handling

Do not accidentally split valid C/C++ header/source relationships because of this change.

For example:

```text
foo.h
foo.cpp
```

should still be eligible for the same C/C++ group.

Likewise, project layouts such as:

```text
include/render/foo.hpp
src/render/foo.cpp
```

should continue to use C/C++ grouping logic when the relationship is recognized.

---

## Out of Scope

Do not implement special cross-file grouping for:

```text
C# partial classes
Java packages
Kotlin source sets
Rust modules
TypeScript interfaces and implementations
Python packages
Go packages
```

Even if those languages could benefit from semantic grouping, they are intentionally out of scope for this phase.

They should remain file-local until benchmark evidence shows a need for language-specific grouping.

Also do not add:

```text
generic semantic clustering
embedding similarity
LLM-based grouping
repository-wide grouping
cross-language grouping
```

---

## Suggested Implementation Shape

Prefer a clear branch near the group-construction boundary.

Conceptually:

```text
for each changed file:
    if file is C or C++:
        send it through C/C++ semantic grouping
    else:
        create a standalone review group
```

If the current implementation first partitions files and then builds groups, an equivalent design is fine.

Keep the language-specific decision localized rather than scattering extension checks throughout the grouping code.

---

## Diagnostics

If grouping diagnostics already exist, make the distinction visible.

Example:

```text
Review group 0
strategy: cpp-semantic
files:
  include/foo.h
  src/foo.cpp
```

```text
Review group 1
strategy: per-file
files:
  scripts/build.py
```

Do not expose these diagnostics to the LLM prompt.

They are only for debugging and benchmark analysis.

---

## Tests

Add or update tests covering at least the following cases.

### Case 1 — Two Non-C/C++ Files

Input:

```text
a.py
b.py
```

Expected:

```text
2 groups
1 file per group
```

---

### Case 2 — C++ Header / Source Pair

Input:

```text
foo.h
foo.cpp
```

Expected:

```text
eligible for one C/C++ semantic group
```

---

### Case 3 — Mixed Languages

Input:

```text
foo.h
foo.cpp
build.py
tool.cs
```

Expected:

```text
C/C++ group:
  foo.h
  foo.cpp

Standalone group:
  build.py

Standalone group:
  tool.cs
```

---

### Case 4 — Same Directory, Non-C/C++

Input:

```text
src/a.py
src/b.py
```

Expected:

```text
two separate groups
```

Directory affinity must not merge them.

---

### Case 5 — Similar Names, Non-C/C++

Input:

```text
Foo.cs
FooTests.cs
```

Expected:

```text
two separate groups
```

Filename similarity must not merge them.

---

## Acceptance Criteria

The task is complete when:

1. C/C++ files continue to use semantic grouping.

2. Every non-C/C++ changed file gets its own review group.

3. Non-C/C++ files are not grouped by directory, symbol relationships, shared callees, or filename similarity.

4. Mixed-language merge requests correctly separate C/C++ semantic groups from standalone non-C/C++ groups.

5. Existing Turn 1 and Turn 2 behavior is unchanged.

6. No additional LLM calls are introduced.

7. Grouping remains deterministic.

8. No non-C/C++ file is duplicated across groups.

9. Existing C/C++ header/source grouping does not regress.

10. Tests cover C/C++, non-C/C++, and mixed-language cases.

---

## Design Principle

Keep Phase 1 language-specific and narrow:

```text
C / C++
    ↓
semantic multi-file grouping

everything else
    ↓
one changed file per review group
```

Do not generalize cross-file semantic grouping until there is benchmark evidence that another language needs it.
