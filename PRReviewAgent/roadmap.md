# Phase 6 — Finalize the SLM Project-Adaptation Feature and Refine README

## Goal

Finalize the lightweight project-adaptation feature introduced in Phases 1–5 and bring its user-facing documentation up to date.

This phase is primarily a cleanup, integration, and documentation phase.

Do not introduce another major rule-learning architecture.

The intended final design is intentionally modest:

```text
Main Code Review
    -> Main LLM

Project Adaptation
    -> Dedicated SubAgent / smaller LLM
    -> conservative rule extraction
    -> learned project-specific review rules
```

The project-adaptation feature is auxiliary.

It does not need to discover every possible project rule.

It only needs to provide useful project-specific guidance without pushing the main code-review system in an obviously incorrect direction.

---

## Phase 6 Scope

Complete the following work:

1. Review the implementation produced by Phases 1–5.
2. Remove obsolete or duplicated code left by the migration.
3. Make configuration names and comments clear.
4. Ensure failure isolation remains intact.
5. Ensure all supported programming languages use the intended rule-extraction path.
6. Run the relevant tests.
7. Refine `README.md` so it accurately describes the current system.
8. Document project adaptation as an optional auxiliary feature.
9. Do not advertise capabilities that the implementation does not actually provide.

---

# Part 1 — Final Architecture Review

Verify that the final architecture follows this responsibility split:

```text
                         Code Review System
                                |
                 +--------------+--------------+
                 |                             |
                 v                             v
          Main Review Path             Project Adaptation
                 |                             |
                 v                             v
          Main Review Agent          RuleExtractionSubAgent
                 |                             |
                 v                             v
             Main LLM                     Sub LLM
                 |                             |
                 |                      conservative rule
                 |                         extraction
                 |                             |
                 |                             v
                 |                        Learned Rules
                 |                             |
                 +-------------+---------------+
                               |
                               v
                         Code Review
```

The main review model remains responsible for actual code-review judgment.

The smaller Sub LLM must not replace the main reviewer.

---

## Main and Sub LLM Separation

Preserve the model separation established previously.

Current development endpoints are:

```text
Main LLM
http://192.168.128.152:9090
```

```text
Sub LLM
http://192.168.128.152:9080
```

These addresses are local deployment details.

Do not hard-code them into normal application logic.

They must remain configuration-driven.

Do not document these private development addresses in the public/general README unless the existing README is explicitly intended only for this local environment.

Use generic endpoint examples in documentation instead.

For example:

```toml
[agent]
reviewer = "http://localhost:9090/v1"
```

and:

```toml
[subagent.rule_extraction]
endpoint = "http://localhost:9080/v1"
```

Adjust the exact path to match the actual implemented configuration and API behavior.

Do not invent `/v1` if the implemented configuration does not use it.

---

# Part 2 — Verify Phase 1 SubAgent Separation

Confirm that rule extraction no longer calls the shared main-review agent directly.

Expected:

```text
RuleExtractionService
        |
        v
RuleExtractionSubAgent
        |
        v
configured Sub LLM
```

Not:

```text
RuleExtractionService
        |
        v
Main Review Agent
```

There must be no silent fallback from the Sub LLM to the Main LLM.

If rule extraction fails, skip rule extraction.

The main reviewer must remain unaffected.

---

# Part 3 — Verify Multi-Language Support

Confirm that rule extraction supports the configured source languages.

Current default review extensions are conceptually:

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

They should map to:

```text
C
C++
C#
Python
Rust
```

Verify that no C/C++-only assumptions remain in the common rule-extraction prompt.

In particular, search for obsolete wording such as:

```text
You are an expert static analysis bot for C/C++.
```

and remove it if still present in the active rule-extraction path.

Do not remove legitimate C/C++-specific logic from language-specific processing.

---

## Header Detection

Retain the simple `.h` handling implemented previously.

Do not add sophisticated language inference during this cleanup phase.

If paired-file information is available, use it.

Otherwise preserve the existing conservative fallback.

---

# Part 4 — Verify Conservative Context Selection

Confirm that the SubAgent does not receive a complete file-level AST by default.

The intended path is:

```text
Full file
    |
    v
AST / structural analysis
    |
    v
deterministic context selection
    |
    +-- expanded changed code
    +-- containing structural context
    +-- directly relevant symbols
    +-- directly relevant dependency changes
    |
    v
RuleExtractionSubAgent
```

Full-file AST analysis may remain internally available.

Do not remove AST capabilities required by:

* expanded diff generation
* file pairing
* normal review processing
* other existing analysis features

Only the SubAgent input is intentionally narrow.

---

## Context Principle

Preserve this behavior:

```text
Analyze broadly.
Send narrowly.
```

Do not reintroduce:

```text
full AST
all symbols
all imports/includes
all dependencies
whole call graph
```

into rule extraction merely to improve a few isolated examples.

---

# Part 5 — Verify Conservative Rule Extraction

Confirm that the active prompt uses the conservative policy established in Phase 4.

The rule extractor should:

* treat changed code as primary evidence
* treat structural context as supporting evidence
* extract only a reusable rule supported by the change
* avoid inferring project-wide policy from one change
* avoid unsupported developer-intent inference
* avoid merely restating the diff
* return `UNKNOWN` when no meaningful reusable rule is evident

Preserve the principle:

```text
precision > recall
```

for this feature.

---

## UNKNOWN Behavior

Verify end-to-end behavior for:

```json
{
  "ast_pattern": "",
  "rule_description": "UNKNOWN",
  "bad_pattern": "",
  "good_pattern": ""
}
```

UNKNOWN should be treated as a normal no-rule result.

It must not:

* create embeddings
* insert a learned rule
* trigger the main LLM
* cause MR processing to fail

---

# Part 6 — Clean Up Obsolete Code

Review code touched by Phases 1–5 and remove migration leftovers where safe.

Look for:

* unused old agent invocation paths
* unused prompt constants
* duplicate language detection
* obsolete configuration access
* unused AST serialization specifically retained only for the old rule-extraction prompt
* dead helper methods
* duplicated SubAgent client construction
* outdated comments referring to C/C++-only extraction
* comments describing full-AST rule-extraction behavior that is no longer true

Do not perform unrelated broad refactoring.

The cleanup should be directly connected to the project-adaptation changes.

---

# Part 7 — Settings Cleanup

The application settings are loaded once at startup and remain effectively immutable.

Preserve this behavior.

Do not add:

* hot reload
* runtime configuration mutation
* endpoint switching
* runtime model routing
* file watchers

Verify that SubAgent configuration follows the same startup configuration model.

Prefer typed access if that is what Phases 1–5 established.

Avoid adding new direct `TomlTable` access from rule-extraction services.

---

## Configuration Template

Refine `config.template.toml` so that the relationship between the main reviewer and rule-extraction SubAgent is obvious.

The exact names must match the actual implementation.

Conceptually, the template should communicate something like:

```toml
[agent]
# Main LLM used for code review.
reviewer = "http://localhost:9090"
reviewer_name = "Reviewer"
reviewer_model = ""

[subagent.rule_extraction]
# Smaller auxiliary LLM used only for project rule extraction.
enabled = true
endpoint = "http://localhost:9080"
name = "RuleExtractor"
model = ""
max_output = 1024
temperature = 0.0
topp = 0.9
timeout = 120

[auto_improve]
enabled = false

# Local embedding model used for learned project rules.
model_path = "Models/granite-embedding-97M-multilingual-r2-Q8_0.gguf"

db_path = "AppData/review_rules.db"
stale_rule_months = 3
min_confidence_score = 7
context_size = 16384
chunk_overlap = 128
```

Do not blindly replace the configuration with this example.

Use the actual keys implemented by the project.

Preserve backward compatibility where the existing configuration policy requires it.

---

# Part 8 — README.md Refinement

Refine the existing `README.md`.

Do not replace it with generic GitHub/GitLab boilerplate.

Do not invent unsupported product claims.

The README should explain what the system currently does in practical terms.

Preserve useful existing installation, build, configuration, GitHub/GitLab, and usage instructions unless they are outdated.

Reorganize only where necessary to improve clarity.

---

## README — Project Description

The opening section should concisely explain that the project is an AI-assisted code-review service for pull/merge requests.

Describe the system based on the actual implementation.

A suitable conceptual description is:

```text
PRReviewAgent is an AI-assisted code-review service for GitHub pull requests
and GitLab merge requests.

It combines a main review LLM with static structural context and can optionally
learn lightweight project-specific review rules from merged changes.
```

Adapt names and provider support to the actual repository.

Do not claim GitHub or GitLab support if the current implementation does not actually support the corresponding workflow.

---

## README — Features

Refine the feature list to reflect actual capabilities.

Where supported by the implementation, include concepts such as:

```text
- AI-assisted pull/merge request review
- GitHub and/or GitLab integration
- structural/AST-assisted review context
- configurable source-file extensions
- multilingual review output, if currently supported
- optional project-specific rule learning
- separate lightweight LLM for project adaptation
```

Do not call the rule-learning system autonomous learning or continuous training.

It is rule extraction and retrieval, not model fine-tuning.

Avoid wording such as:

```text
The model automatically trains itself on your repository.
```

Prefer:

```text
The optional project-adaptation feature extracts reusable review rules
from merged code changes and stores them for later review context.
```

---

# Part 9 — README Project Adaptation Section

Add a dedicated section such as:

```text
## Project Adaptation
```

Explain its purpose clearly and conservatively.

Recommended content:

```text
Project adaptation is an optional auxiliary feature.

When enabled, merged changes can be analyzed to identify small reusable
engineering rules or bug-fix patterns.

Rule extraction runs through a separate configurable SubAgent, allowing a
smaller local model to handle this workload without consuming the main
code-review model.

The extractor uses the changed code as its primary evidence and only a compact
subset of relevant structural context. It may intentionally decline to learn
a rule when the change does not clearly demonstrate one.

This feature is designed to provide lightweight project-specific context,
not to infer a complete coding standard from the repository.
```

Adjust terminology to match the implementation.

---

## README — Explain Conservative Behavior

Document `UNKNOWN` conceptually without exposing unnecessary internal details.

For example:

```text
Not every merged change becomes a learned rule. Trivial, ambiguous, or
insufficiently supported changes are ignored.
```

This behavior should be presented as intentional.

Do not imply that every merge teaches the system something.

---

# Part 10 — README Architecture

If the README already contains an architecture section, update it.

Otherwise add a small architecture section if it materially helps users understand the two-model design.

Keep it compact.

Recommended diagram:

```text
Pull / Merge Request
        |
        v
Static / Structural Context
        |
        v
Main Review Agent
        |
        v
Main LLM
        |
        v
Code Review


Merged Change
        |
        v
Compact Change Context
        |
        v
Rule Extraction SubAgent
        |
        v
Smaller LLM
        |
        v
Learned Project Rules
```

Also show that learned rules may later contribute context to code review if that is how the current implementation works.

Do not turn the README into an internal design document.

---

# Part 11 — README Supported Languages

Document the default source-language coverage based on the configured extensions.

Recommended presentation:

```text
## Supported Source Languages

The default configuration includes:

- C
- C++
- C#
- Python
- Rust
```

Optionally show extensions:

```text
C:      .c
C++:    .cpp, .cxx, .cc, .h, .hpp, .inl
C#:     .cs
Python: .py
Rust:   .rs
```

Clarify that the reviewed extensions are configurable through `target_extensions`.

Do not imply that every language receives equally deep static semantic analysis unless that is actually true.

---

# Part 12 — README LLM Configuration

Explain the two model roles.

Use terminology such as:

```text
Main Reviewer
```

and:

```text
Rule Extraction SubAgent
```

Recommended explanation:

```text
The main reviewer should use the model intended for code-review quality.

The project-adaptation extractor may use a smaller, faster model because its
task is intentionally narrow and conservative.
```

Do not require Gemma 4 specifically unless the application itself requires it.

The README may mention Gemma 4 E4B as an example only if examples are useful.

For example:

```text
A small local model such as Gemma 4 E4B can be used for rule extraction.
```

Do not present E4B as a mandatory dependency if the endpoint is OpenAI-compatible/configurable.

---

# Part 13 — README Configuration Example

Update the configuration example to include the SubAgent configuration.

Use neutral addresses such as:

```toml
[agent]
reviewer = "http://localhost:9090"

[subagent.rule_extraction]
enabled = true
endpoint = "http://localhost:9080"
```

The example must exactly match the actual property names after implementation.

Do not expose:

```text
192.168.128.152
```

in README examples.

That is environment-specific information.

---

# Part 14 — README Auto Improve Description

If the README describes `[auto_improve]`, refine the terminology.

Explain that it stores and retrieves project-specific learned rules.

Where relevant, document:

```text
enabled
embedding model path
rule database path
stale rule handling
minimum confidence threshold
```

Only describe configuration keys that actually exist after Phase 6.

Do not document experimental or removed settings.

---

# Part 15 — README Limitations

Add a short limitations section if one does not already exist.

Keep it practical.

Recommended points:

```text
- Project adaptation does not fine-tune the LLM.
- Not every merged change produces a learned rule.
- Rule extraction intentionally favors conservative results.
- Static/semantic context depth varies by programming language.
- The auxiliary SubAgent is optional and does not replace the main reviewer.
```

Avoid apologetic wording.

These are design constraints, not defects.

---

# Part 16 — README Terminology Consistency

Use consistent terminology throughout the README.

Prefer:

```text
Main Review Agent
Rule Extraction SubAgent
Main LLM
Sub LLM
project adaptation
learned rule
expanded diff
structural context
```

Avoid switching unpredictably between terms such as:

```text
small agent
child agent
mini reviewer
training model
learning model
secondary reviewer
```

unless those are actual class/configuration names.

---

# Part 17 — Do Not Expose Internal Development Details

Search the README and configuration examples for local-only details.

Do not publish:

* private IP addresses
* internal hostnames
* tokens
* secrets
* repository credentials
* machine-specific paths
* local test-project locations

Use neutral examples instead.

Do not modify real secrets files as part of this phase.

---

# Part 18 — Tests and Validation

Run all tests relevant to the modified system.

At minimum, preserve coverage for:

```text
SubAgent routing
configuration loading
language detection
compact context generation
UNKNOWN handling
rule persistence
Sub LLM failure isolation
```

Also run existing general project tests to detect regressions.

Fix failures caused by Phases 1–6.

Do not broaden the task into unrelated historical failures unless they are directly caused by this work.

---

## End-to-End Smoke Test

Perform at least one realistic end-to-end project-adaptation test.

Preferred target:

```text
Python test project
```

Validate:

```text
merged change
    -> detected as Python
    -> expanded diff generated
    -> compact context generated
    -> RuleExtractionSubAgent invoked
    -> Sub LLM response parsed
    -> reasonable rule persisted OR UNKNOWN safely discarded
```

Also confirm that normal code review continues through the Main LLM path.

---

# Part 19 — Logging Review

Ensure logs make the two inference paths distinguishable.

Useful events include:

```text
Main review request
Rule extraction request
Rule extraction UNKNOWN
Rule extraction rejected
Rule extraction failure
Learned rule stored
```

Do not log complete prompts, full source files, full ASTs, API keys, or secrets.

Avoid excessive informational logging for every internal AST node or symbol.

---

# Part 20 — Remove Temporary Validation Code

If Phase 5 introduced temporary diagnostics, comparison modes, or test-only instrumentation into production code, review them.

Keep lightweight useful metrics if they fit the project.

Remove:

* temporary full-AST-vs-compact comparisons
* hard-coded experiment switches
* temporary test endpoints
* console dumps of prompts
* debug-only source-code logging
* one-off model comparison code

Do not remove proper automated tests.

---

# Part 21 — Code Quality

Run the formatter/build/analyzers normally used by the repository.

Fix issues introduced by this project-adaptation work.

Do not perform broad cosmetic rewriting of unrelated source files.

Keep the diff focused.

---

# Part 22 — Documentation Must Match Implementation

Before completing Phase 6, compare:

```text
README.md
config.template.toml
Settings implementation
SubAgent implementation
RuleExtractionService
```

Ensure configuration names, defaults, and described behavior agree.

The code is the source of truth.

If the README and the implementation differ, update the README to match the implementation rather than inventing compatibility behavior.

---

# Non-Goals

Do not add the following in Phase 6:

* another LLM tier
* automatic model escalation
* fallback from Sub LLM to Main LLM
* model-generated confidence scoring
* multi-agent voting
* automatic fine-tuning
* repository-wide policy inference
* rule clustering redesign
* rule lifecycle redesign
* new database architecture
* deep call-graph reasoning
* full semantic analysis for Python
* full C++ compiler-level semantic analysis
* per-language SubAgents
* dynamic configuration reload
* runtime model switching
* production benchmarking infrastructure
* unrelated code-review feature redesign

The system already has the desired architecture.

Phase 6 should finish it, not expand it.

---

# Expected Final State

The final project-adaptation architecture should be understandable as:

```text
                    PR / MR Review
                         |
                         v
                  Main Review Agent
                         |
                         v
                      Main LLM
                         |
                         v
                    Review Result


                    Merged Change
                         |
                         v
              Expanded / Relevant Context
                         |
                         v
              Rule Extraction SubAgent
                         |
                         v
                       Sub LLM
                         |
                  +------+------+
                  |             |
                  v             v
             useful rule      UNKNOWN
                  |             |
                  v             v
               store         discard
```

The main reviewer remains the high-quality review component.

The SubAgent remains a cheap, conservative auxiliary component.

---

# Acceptance Criteria

Phase 6 is complete when all of the following are true:

1. The Phase 1–5 project-adaptation implementation is reviewed and cleaned up.

2. Rule extraction remains isolated from the Main LLM.

3. The Main and Sub LLM endpoints remain independently configurable.

4. No silent fallback from Sub LLM to Main LLM exists.

5. C, C++, C#, Python, and Rust remain supported by the language-aware extraction path.

6. Full-file AST and unrelated symbol/dependency lists are not passed to the SubAgent by default.

7. Expanded changed code remains the primary extraction evidence.

8. Conservative `UNKNOWN` behavior remains functional.

9. UNKNOWN and invalid results are never persisted as learned rules.

10. SubAgent failure cannot break the main review path.

11. Startup-loaded immutable configuration behavior is preserved.

12. `config.template.toml` accurately documents the Main Reviewer and Rule Extraction SubAgent configuration.

13. `README.md` accurately describes the current code-review system.

14. `README.md` includes the optional project-adaptation feature.

15. `README.md` explains the Main LLM / Sub LLM responsibility split.

16. `README.md` documents the supported source languages and configurable extensions.

17. `README.md` describes project adaptation conservatively and does not imply model fine-tuning.

18. README examples contain no private IP addresses, credentials, or environment-specific internal details.

19. Existing relevant automated tests pass.

20. A Python end-to-end smoke test succeeds.

21. Existing C/C++ behavior remains functional.

22. Temporary Phase 5 experimental/debug code that is no longer useful has been removed.

23. No major new learning architecture has been introduced.

Keep the final implementation simple.

The desired outcome is a practical code-review system with an optional, inexpensive, conservative project-adaptation layer—not a general autonomous project-learning system.
