Review the pull request in English according to the following guidelines.

# Review Policy

Review only the changed code.

In addition to the source code, structured context generated from AST analysis is provided in JSON. Use it when relevant, including:

* Changed scopes
* Types, functions, and methods
* Declaration-definition relationships
* Call graph
* Symbol references
* Inheritance and implementation relationships
* Imports and dependencies
* Declarations in related files

Limit findings to the change and its impact. Do not report unrelated issues.

If the source code conflicts with the structured context, treat the source code as authoritative.

Treat declarations and definitions as the same entity. Do not report the same issue more than once.

# Severity

## Critical

Issues that must be fixed.

* Bugs or crashes
* Memory safety, null dereference, lifetime, or resource leak issues
* Thread safety or data races
* Breaking API compatibility
* Security issues
* Declaration/implementation inconsistencies
* Serious impact on existing callers or users

## Major

Issues that significantly harm quality or maintainability.

* Poor dependencies or responsibilities
* Excessive complexity or duplication
* Clear design problems
* Performance problems
* Insufficient error handling
* Significant maintainability problems

## Minor

Improvements with clear practical value.

* Readability
* Naming
* Code organization
* Maintainability

Do not report, in principle:

* Formatting or indentation
* Pure style preferences
* Personal design preferences
* Refactoring without a clear reason
* YAGNI violations

# AST Context

Use the call graph, symbol references, inheritance relationships, and other structured context as needed to evaluate:

* Impact on callers and callees
* API changes
* Impact on inheritance relationships

# Output

Output findings in severity order using the following format.

* Use only `## Critical`, `## Major`, and `## Minor` as severity headings
* Omit severity sections with no findings
* Do not add headings for files, modules, categories, or other grouping
* Keep each finding independent
* Do not use tables, introductions, summaries, conclusions, or unnecessary Markdown

Format:

```markdown
# image
## Critical
### image.cpp: Image::Image(uint32_t width, uint32_t height)

- **Issue:** Missing handling for memory allocation failure
- **Reason:** `::malloc` may return `nullptr`, but the result is not checked, which may cause a null dereference and crash in subsequent operations.
- **Suggested fix:** Check the return value and handle allocation failure appropriately.
```

Each finding must contain:

* **Issue**
* **Reason**
* **Suggested fix**

Do not combine multiple independent issues into one finding.

If no issues are found, output only:

`No issues found`
