# Phase 2 — Add Multi-Language Rule Extraction Support

## Goal

Extend rule extraction so it is no longer hard-coded for C/C++.

The rule-extraction pipeline must recognize and support the following source languages:

```text
C
C++
C#
Python
Rust
```

The existing target extensions are:

```text
c
cpp
cxx
cc
h
hpp
inl
cs
py
rs
```

Phase 2 must make the rule-extraction SubAgent language-aware while preserving the architecture introduced in Phase 1.

Do not redesign AST context reduction or conservative rule extraction yet.

---

## Current Limitation

The current rule-extraction prompt is explicitly C/C++ specific.

Conceptually:

```text
You are an expert static analysis bot for C/C++.
```

This must be removed.

The rule-extraction path should instead determine the source language from the file being processed and include that information in the extraction request.

---

## Required Architecture

The Phase 1 structure must remain:

```text
RuleExtractionService
        |
        v
RuleExtractionSubAgent
        |
        v
Sub LLM
```

Add language detection before prompt construction:

```text
Changed File
    |
    v
SourceLanguageDetector
    |
    v
RuleExtractionContext
    |
    v
RuleExtractionSubAgent
```

The SubAgent remains responsible only for LLM inference.

Language detection and prompt selection must remain outside the transport/client layer.

---

## Introduce SourceLanguage

Add an enum or equivalent strongly typed representation.

Recommended shape:

```csharp
public enum SourceLanguage
{
    Unknown,
    C,
    Cpp,
    CSharp,
    Python,
    Rust
}
```

Do not pass raw file extensions throughout the rule-extraction pipeline when a normalized language value is sufficient.

---

## Add SourceLanguageDetector

Create a small deterministic language detector.

Recommended API:

```csharp
public static class SourceLanguageDetector
{
    public static SourceLanguage Detect(
        string filePath,
        string? pairPath = null);
}
```

The exact API may differ if another existing utility is more appropriate.

Use extension-based detection.

Required mappings:

```text
.c
    -> C

.cpp
.cxx
.cc
.hpp
.inl
    -> Cpp

.cs
    -> CSharp

.py
    -> Python

.rs
    -> Rust
```

---

## Handling `.h`

`.h` is ambiguous between C and C++.

Use a simple conservative rule.

Recommended behavior:

```text
.h + paired .c
    -> C

.h + paired .cpp/.cc/.cxx
    -> Cpp

.h without useful pair information
    -> Cpp
```

Do not implement complex source-language inference in this phase.

Do not inspect arbitrary source syntax solely to classify `.h`.

The existing file-pair information may be used when available.

---

## Unknown Languages

If the language cannot be determined:

```text
SourceLanguage.Unknown
```

should be returned.

Rule extraction may either:

* skip the file, or
* continue using a generic language-neutral prompt

Prefer skipping if the file extension is not part of the configured target extensions.

Do not guess a programming language from unrelated file content.

---

## Add Language to RuleExtractionContext

If a structured rule-extraction input type already exists from Phase 1, extend it.

Otherwise introduce a minimal context object.

Recommended example:

```csharp
public sealed record RuleExtractionContext
{
    public required SourceLanguage Language { get; init; }

    public required string FilePath { get; init; }

    public required string Diff { get; init; }

    public string? AstContext { get; init; }

    public string? FileDependencies { get; init; }
}
```

For Phase 2, keep the existing AST and dependency payloads unchanged.

Do not reduce them yet.

The purpose of this phase is language support only.

---

## Update RuleExtractionService

The rule extraction service must receive enough file information to determine the source language.

Current conceptual input:

```text
AST context
diff
file dependencies
```

Change this so the service also knows:

```text
file path
pair path, when available
```

For example:

```csharp
ExtractAndSaveRuleAsync(
    string filePath,
    string astContext,
    string diff,
    string fileDependencies,
    string? pairPath,
    CancellationToken cancellationToken)
```

or preferably:

```csharp
ExtractAndSaveRuleAsync(
    RuleExtractionContext context,
    CancellationToken cancellationToken)
```

Use whichever structure best matches the code after Phase 1.

Avoid unrelated refactoring.

---

## Make the System Prompt Language-Neutral

Replace the C/C++-specific role description.

Recommended base prompt:

```text
You are a static analysis extraction system.

Analyze the provided code change, AST structure, and relevant file dependencies.

Extract the underlying engineering rule, coding standard, or bug-fix pattern applied by the developer.

Use the programming language specified in the input when interpreting syntax and semantics.

Respond only in the required JSON format.
```

Do not add project-policy inference or conservative extraction rules yet.

Those belong to later phases.

---

## Include the Language Explicitly in the Prompt

Add a clear language field before the source context.

Example:

```text
# Language

Python

# File Dependencies
...

# AST Context
...

# Code Diff
...
```

Do not rely on the model to infer the language from syntax.

Keep the language name canonical:

```text
C
C++
C#
Python
Rust
```

---

## Optional Small Language Hints

A minimal per-language hint is acceptable if testing shows it improves extraction quality.

Keep hints short.

Example:

```text
C++:
Interpret ownership, lifetime, RAII, pointer/reference semantics, templates,
and const correctness according to C++ semantics.

C#:
Interpret nullability, IDisposable, async/await, exceptions, and task behavior
according to C# semantics.

Python:
Interpret None handling, exceptions, context managers, iteration, and dynamic typing
according to Python semantics.

Rust:
Interpret ownership, borrowing, Result/Option, traits, lifetimes, and unsafe code
according to Rust semantics.

C:
Interpret pointer lifetime, manual resource management, undefined behavior,
and explicit error handling according to C semantics.
```

These hints should not become large language-specific prompts.

A single shared extraction prompt must remain the default design.

---

## Do Not Create Separate SubAgents Per Language

Do not add:

```text
CppRuleExtractionSubAgent
PythonRuleExtractionSubAgent
RustRuleExtractionSubAgent
...
```

All supported languages must use the same:

```text
RuleExtractionSubAgent
```

The language is input context, not agent identity.

The configured Sub LLM endpoint remains unchanged.

---

## Preserve Existing Sub LLM Routing

Rule extraction must continue using the Sub LLM introduced in Phase 1.

Do not modify the main review-agent routing.

Conceptually:

```text
Main Code Review
    -> Main LLM

Rule Extraction
    -> RuleExtractionSubAgent
    -> Sub LLM
```

Language support must not cause any fallback to the main review LLM.

---

## Preserve Existing Extraction Output

Do not change the JSON schema in this phase.

Keep:

```json
{
  "ast_pattern": "...",
  "rule_description": "...",
  "bad_pattern": "...",
  "good_pattern": "..."
}
```

Do not add:

```text
confidence
evidence
UNKNOWN
language
```

to the output schema yet.

Language belongs to the extraction input in this phase.

---

## Preserve Existing AST Behavior

Do not change how much AST data is generated or passed to the SubAgent yet.

In particular, do not implement:

* AST semantic slicing
* changed-node-only AST extraction
* dependency filtering
* symbol filtering
* include/import filtering
* context-budget reduction

These belong to the next phase.

Phase 2 should establish multi-language correctness before input reduction is introduced.

---

## Python Test Project

A Python test project will be available for validation.

Use it to verify the complete rule-extraction path.

At minimum, prepare test cases representing simple and clearly understandable changes.

Recommended cases:

### None check

Before:

```python
user.save()
```

After:

```python
if user is not None:
    user.save()
```

Expected behavior:

The extraction should identify a rule related to validating an optional value before dereferencing or invoking methods on it.

---

### Context manager

Before:

```python
f = open(path)
data = f.read()
f.close()
```

After:

```python
with open(path) as f:
    data = f.read()
```

Expected behavior:

The extraction should recognize use of a context manager for deterministic resource cleanup.

---

### Exception handling

Before:

```python
value = int(text)
```

After:

```python
try:
    value = int(text)
except ValueError:
    value = 0
```

Expected behavior:

The extraction should identify handling of an expected conversion failure.

Do not require exact wording.

The important requirement is that the rule is semantically reasonable and clearly based on the change.

---

## Tests

Add focused automated tests.

### SourceLanguageDetector

Test all configured extensions:

```text
.c
.cpp
.cxx
.cc
.h
.hpp
.inl
.cs
.py
.rs
```

Test extension case behavior according to existing project conventions.

If paths are currently treated case-sensitively, do not silently change the global behavior unless necessary.

---

### `.h` pairing

Verify at minimum:

```text
foo.h + foo.c
    -> C

foo.h + foo.cpp
    -> Cpp

foo.h + no pair
    -> Cpp
```

---

### Prompt generation

Verify that generated prompts include the correct language.

For example:

```text
foo.py
    -> Language: Python

foo.rs
    -> Language: Rust

foo.cs
    -> Language: C#
```

Verify that the old C/C++-specific system statement is no longer present for Python, Rust, or C#.

---

### Routing

Verify that all supported languages still use:

```text
RuleExtractionSubAgent
```

and not the main review agent.

---

### Existing C/C++ behavior

Run existing C/C++ tests and ensure rule extraction still works.

The goal is to add supported languages, not regress the original ones.

---

## Logging

Add the detected language to existing rule-extraction diagnostic logs where useful.

For example:

```text
Extracting rule for {Path} as {Language}
```

Do not log source content, full AST data, or prompts.

---

## Failure Behavior

If language detection fails or an unsupported extension reaches the extraction path:

* do not crash MR processing
* log the condition at an appropriate level
* skip extraction when appropriate

Do not route the request to another model as a fallback.

---

## Non-Goals

Do not implement the following in Phase 2:

* conservative rule extraction
* `UNKNOWN` support
* confidence scoring
* output evidence
* AST context reduction
* semantic slicing
* symbol relevance filtering
* dependency relevance filtering
* include/import relevance filtering
* model benchmarking
* per-language models
* per-language SubAgents
* rule lifecycle redesign
* clustering changes
* main reviewer changes
* database schema changes

Keep this phase focused on source-language support.

---

## Expected Result

After Phase 2:

```text
GitLab changed file
        |
        v
SourceLanguageDetector
        |
        +-- C
        +-- C++
        +-- C#
        +-- Python
        +-- Rust
        |
        v
RuleExtractionService
        |
        v
RuleExtractionSubAgent
        |
        v
Sub LLM
```

The same rule-extraction pipeline should work across all supported languages.

The model must always be told which language it is analyzing.

---

## Acceptance Criteria

Phase 2 is complete when all of the following are true:

1. Rule extraction is no longer hard-coded to C/C++.

2. `SourceLanguage` or an equivalent normalized language representation exists.

3. The configured extensions map to C, C++, C#, Python, or Rust.

4. `.h` files use simple pair-based C/C++ detection when pair information is available.

5. The detected language is explicitly included in the rule-extraction prompt.

6. All supported languages use the same `RuleExtractionSubAgent`.

7. Rule extraction continues using the Sub LLM and does not fall back to the main review LLM.

8. Existing rule JSON output remains unchanged.

9. Existing AST and dependency context behavior remains unchanged.

10. Python rule extraction works against the provided test project for several simple changes.

11. Existing C/C++ rule extraction still works.

12. Automated tests cover language detection and prompt generation.

Keep the implementation small. The purpose of this phase is only to make the existing rule-extraction path language-aware before later phases reduce and constrain the context supplied to the SLM.
