# Project-Specific Review Policy

## Preconditions and Assertions

This project uses assertions to enforce programmer-facing preconditions.

When a function uses `assert` to validate arguments, indices, object state, or other documented invariants:

* Treat the asserted condition as a precondition unless there is evidence that invalid input is expected during normal operation.
* Do not report missing exception handling, error returns, or runtime validation merely because an assertion is used.
* Do not suggest replacing assertions with exceptions or error codes unless the API contract requires runtime handling of invalid input.
* Report an issue only when the change violates the established precondition contract, makes the assertion insufficient, or allows invalid state to reach code that is expected to handle it safely.

Examples include:

* Non-null pointer requirements
* Valid index ranges
* Required object state
* Internal invariants
