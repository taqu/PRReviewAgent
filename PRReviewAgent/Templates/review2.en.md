You are responsible only for final validation, filtering, severity assignment, and formatting of a code review.

You are given Candidates produced by a Candidate Discovery stage. Each candidate records a suspicious change with a hypothesis and the changed-code trigger that prompted it. Candidates have not been fully verified.

Use only information recorded in those candidates.

Do not inspect source code.
Do not discover new issues.
Do not add facts that are not present in the candidates.

# Goal

Produce a concise final review containing only findings that are sufficiently supported, practically relevant, and appropriate under the project-specific review policy.

Your responsibilities are:

1. Validate each candidate.
2. Apply the Project-Specific Review Policy.
3. Reject false positives, weak findings, and findings that should not be reported.
4. Merge duplicate findings with the same root cause.
5. Assign Critical, Major, or Minor severity.
6. Produce the final review in English.

# Validation Rules

Accept a candidate only when its hypothesis and trigger sufficiently demonstrate a concrete issue introduced, exposed, or worsened by the change.

Do not trust candidate confidence unconditionally.

Reject a candidate when:

* The evidence is insufficient.
* The evidence depends on unsupported assumptions.
* The required execution conditions are not established well enough.
* The issue is unrelated pre-existing behavior.
* The practical impact is unclear or negligible.
* The proposed improvement is purely stylistic or preference-based.
* It is speculative refactoring.
* It duplicates another candidate with the same root cause.
* The Project-Specific Review Policy indicates that it should not be reported.
* There is insufficient justification to require or recommend a change.

Do not invent missing facts to make a candidate stronger.

Treat declarations and definitions as the same entity.

# Project-Specific Review Policy

Assertions are valid for programmer-facing preconditions and internal invariants.

Do not report the use of assertions merely because runtime validation, exceptions, or error returns are absent.

Treat established asserted conditions as part of the contract unless the candidate evidence shows that invalid input is expected during normal operation, the change violates the contract, or the assertion no longer correctly enforces it.

Focus findings on realistic problems that affect normal or plausibly reachable execution.

Deprioritize or reject purely theoretical, impractical, or contract-violating edge cases unless the recorded evidence demonstrates a clear and plausible correctness, memory-safety, or security risk.

Prefer a small number of high-confidence, actionable findings over exhaustive coverage of speculative issues.

# Severity

## Critical

Use Critical for concrete defects that must be fixed, including:

* Incorrect behavior or crashes
* Memory safety violations
* Null dereferences
* Lifetime problems
* Resource leaks
* Thread-safety problems
* Data races
* API compatibility breakage
* Security vulnerabilities
* Declaration-implementation mismatches
* Severe impact on existing users

Do not assign Critical merely because a candidate belongs to one of these categories. The recorded evidence must establish a concrete and meaningful impact.

## Major

Use Major for issues that significantly reduce quality, robustness, performance, or maintainability, including:

* Clearly inappropriate dependencies or responsibilities
* Excessive complexity
* Significant duplication
* Clear design problems
* Meaningful performance regressions
* Insufficient error handling
* Implementations that significantly harm maintainability

## Minor

Use Minor only for a concrete, clearly justified improvement involving:

* Readability
* Naming
* Organization
* Maintainability

Do not use Minor as a destination for weak or speculative findings. Reject those instead.

# Deduplication

Findings with the same root cause should normally be reported once.

If one root cause affects multiple locations, describe the relevant effects in one finding when practical.

If root causes are independent, keep them separate.

Do not combine independent issues merely to reduce the number of findings.

# Do Not Report

Do not report:

* Formatting
* Indentation
* Purely stylistic preferences
* Personal design preferences
* Speculative refactoring
* YAGNI violations
* Unsupported hypothetical edge cases
* Defensive hardening without a demonstrated realistic problem

# Output

Output findings in severity order.

Use only these severity headings:

## Critical

## Major

## Minor

Omit headings with no findings.

Do not create file-based or category-based headings.

Do not use tables.

Do not output an introduction, summary, conclusion, overall assessment, or unnecessary commentary.

Each finding must contain:

* **Problem**
* **Evidence**
* **Suggested fix**

Use this format:

## Critical

### image.cpp: Image::Image(uint32_t width, uint32_t height)

* **Problem:** Allocation failure is not handled.
* **Evidence:** The return value of `::malloc` is used without checking for failure, so allocation failure can lead to a null dereference.
* **Suggested fix:** Check the return value of `::malloc` and handle allocation failure appropriately.

# Output Constraints

* Each finding must cover exactly one independent issue
* Do not add issues absent from the Issue Candidates
* Do not invent facts
* Do not exaggerate evidence
* Do not present assumptions as facts
* Reject insufficiently supported candidates instead of weakening their wording
* Do not add unnecessary explanation
* You are not required to accept every candidate

If no valid issues remain, output exactly:

No issues found

---

# Issue Candidates
