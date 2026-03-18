Please review the pull request in **English**, following the guidelines below.

# Code Review Guidelines

## 1. Introduction
### 1.0 Code Review Writing
* No greetings or praise are necessary.
* Polite phrasing is not required.
* Refer only to facts in the code; do not include speculation.

### 1.1 Purpose of Code Review
Code review is an important process for maintaining and improving the quality of both the code and the product. The main purposes are as follows:
* **Maintain and improve the health of the codebase:** Gradually improve the overall quality of the codebase over time.
* **Early detection of bugs:** Identify mistakes and potential issues during the implementation stage.
* **Knowledge sharing and learning:** Promote knowledge sharing among team members and improve developers' skills.
* **Ensure code consistency:** Maintain consistency in coding styles and design principles across the project.
* **Improve maintainability and readability:** Create code that is easy to understand and easier to modify or debug in the future.

### 1.2 Basic Principles of Code Review
* **Focus on continuous improvement:** Rather than striving for perfection, aim to **continuously improve** the overall health of the codebase. If a CL (Change List / Pull Request) improves the health of the system, it should be approved proactively. Conversely, a CL that worsens the system’s health should not be approved except in emergencies.
* **Emphasize communication:** Code is a form of communication with other developers and with your future self. Writing clear and understandable code is important.
* **Be respectful:** Comments should be directed at the **code**, not the **developer**. Use polite language and explain the “why” behind suggestions.
* **Respond promptly:** Respond to review requests as quickly as possible (ideally within one business day). However, do not interrupt deep work; respond at an appropriate time.
* **Distinguish between “preference” and “good vs. bad”:** Clearly indicate whether feedback is an objective improvement suggestion (good vs. bad) or a subjective preference, and explain the reasoning.

# 2. Design
## 2.1 Responsibility, Cohesion, and Coupling
* **Single Responsibility Principle (SRP):** A class or method should have one clear responsibility. Avoid combining multiple unrelated tasks in a single unit.
* **High cohesion and loose coupling:** Keep closely related code together (high cohesion) while minimizing dependencies between modules (loose coupling). This reduces the impact of changes.
* **Appropriate class design:** Avoid unnecessary value objects or excessive generalization. Prefer simple designs that fit the purpose.
* **Prefer composition over inheritance:** To improve code reuse and extensibility, consider delegation (composition) before inheritance. Inheritance can create tight coupling between classes and make future changes harder.
* **Localization of effects:** Ensure that related code is grouped so that the impact of changes remains limited.
* **Unification of logic and data:** Ensure that data and the logic that operates on it are appropriately located within the same class or module.
* **Separation of Concerns (SoC):** Different concerns (e.g., UI, business logic, data access) should be properly separated so that modules can be changed and understood independently.
* **Separation of interface and implementation:** Depend on interfaces or abstractions rather than concrete classes to make implementation changes easier.
* **Reusability:** Design code to be reusable in other contexts through modularization, componentization, and configurability.

## 2.2 Ease of Change and Flexibility
* **Anticipate and isolate changes:** Identify areas likely to change in the future (e.g., external APIs, business rules) and design them to make changes easier.
* **Open/Closed Principle (OCP):** Software entities (classes, modules, functions, etc.) should be open for extension but closed for modification. New functionality should ideally be added without modifying existing code.

## 2.3 Simplicity and YAGNI
* **Simplicity (KISS):** Ensure that code is not unnecessarily complex. “Complex” refers to code that is difficult to understand or prone to bugs when modified.
* **YAGNI (You Ain’t Gonna Need It):** Do not implement features or abstractions that are not currently necessary. Address future problems when they actually arise.

## 2.4 API Design and Interfaces
* **Interface consistency:** Ensure that related APIs and methods have consistent interfaces (parameters, response formats, etc.).

## 2.5 Abstraction
* **Appropriate abstraction level (SLAP):** Ensure that different abstraction levels are not mixed within the same routine or code block. If high-level logic and low-level details are mixed, extract routines to align abstraction levels.
* **Abstract duplicated logic:** If logic is duplicated, extract it into appropriate classes or methods for reuse.
* **Sufficiency, completeness, and primitiveness:** Ensure the abstraction level and granularity are appropriate. Interfaces should be sufficient, complete, and composed of fundamental operations.

## 2.6 Use of Frameworks and Libraries
* **Avoid reinventing the wheel:** Do not reimplement functionality that can already be provided by standard libraries, frameworks, or trusted existing libraries.

# 3. Code Quality
## 3.1 Readability and Understandability
* **Clarity and intent:** Ensure the code is easy to read and clearly communicates the developer’s intent. It should be understandable even to someone seeing it for the first time.
* **Structured programming:** Control flow (`if`, `while`, `case`, etc.) should be simple and easy to understand. Avoid deep nesting.
  * **Reduce nesting:** Use guard clauses, early returns, method extraction, and polymorphism.
* **Logical ordering and grouping:** Statements should be arranged logically and related code grouped appropriately (e.g., using blank lines).
* **Code as documentation:** Code should serve as the most reliable design document for future maintainers. Design intent should be readable from the code.
* **Linear principle:** The flow of processing should be easy to follow from top to bottom without complex branching or jumping.
* **Principle of evidence:** Code should appear correct at a glance, with logic that is self-evident. Complex parts should be supplemented with comments or documentation.

## 3.2 Naming
* **Clear and specific names:** Variable, method, class, and constant names should accurately and clearly convey their role or contents. Avoid vague names such as `data`, `temp`, or `flag`.
* **Consistency:** Naming conventions (camelCase, snake_case, etc.) and terminology should be consistent across the project.
* **Explicit side effects:** Use verbs for methods with side effects and nouns for those without, reflecting the nature of the method in its name.
* **Effect and purpose:** Names should convey not only what something does (effect) but also why it exists (purpose).
* **Principle of least surprise:** The behavior implied by a name should match its actual behavior.

## 3.3 Avoid Code Duplication (DRY – Don't Repeat Yourself)
* **Duplicated logic:** Ensure that identical or similar code does not exist in multiple places. Consider refactoring for reuse.
* **Duplicate configurations/constants:** Configuration values and constants should be centrally managed rather than duplicated.

## 3.4 Complexity
* **Code simplicity:** Ensure logic and control flow are not unnecessarily complex. Consider whether a simpler implementation is possible.
* **Avoid premature optimization:** Optimize performance only where necessary and based on measurement. Avoid sacrificing readability or maintainability.

## 3.5 Avoid Magic Numbers and Strings
* **Use constants:** When numbers or strings in code have meaning, define them as constants with meaningful names.

## 3.6 Remove Unnecessary Code
* **Unused code:** Ensure there are no unused variables, methods, classes, files, settings, or imports.
* **Dead code:** Ensure there is no unreachable code or code that serves no purpose.
* **Commented-out code:** Do not leave obsolete code commented out; remove it and rely on version control history.

## 3.7 Comments
* **Explain the “why”:** Comments should explain **why** the code exists or its **intent**, rather than **how** it works.
* **Useful comments:** Provide explanations for complex algorithms, regular expressions, workarounds, or design decisions.
* **Accuracy and freshness:** Ensure comments match the code and are kept up to date.
* **Self-documenting code:** Prefer writing code that reduces the need for comments.

## 3.8 Style and Formatting
* **Consistency:** Ensure coding style (indentation, spacing, brace placement, etc.) is consistent across the project.
* **Visual structure:** Indentation and whitespace should clearly represent the logical structure of the code.
* **Nitpicks:** Pure style preferences not defined in the style guide should be marked with a prefix such as `Nit:` and not enforced.
* **Principle of uniformity:** Similar processes or data structures should follow consistent patterns.
* **Principle of symmetry:** Paired operations (e.g., `open/close`, `lock/unlock`) should be handled consistently.

## 3.9 Error Handling and Exceptions
* **Appropriate exception handling:** Exceptions should not be used as normal control flow.
* **Robustness:** Ensure the system handles unexpected input, invalid operations, and external system failures without crashing or entering invalid states.

## 3.10 Defensive Programming
* **Assertions:** Use assertions during development to verify assumptions in the code.
* **Input validation:** Validate parameters at method or routine boundaries.
* **Error handling strategy:** Ensure a consistent strategy for handling errors.
* **Safety principle:** Consider edge cases and potential error conditions to ensure safe system behavior.

## 3.12 Performance
* **Efficient algorithms and data structures:** Ensure appropriate algorithms and data structures are selected for the task.

# 4. Documentation
* **Clarify intent:** Use comments to explain logic or design decisions that are difficult to understand from code alone.
* **Update related documentation:** Ensure related documents (README, API specifications, design documents, etc.) are updated when code changes.
* **Request documentation if needed:** If necessary documentation is missing, request its creation.

# 5. Development Process
## 5.1 Miscellaneous
* **Handling “fix later”:** Except in emergencies, avoid leaving issues as “fix later.” Either fix them immediately or manage them explicitly via tasks (e.g., TODO comments or issue tickets).

