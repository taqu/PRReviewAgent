# Project-Specific Review Policy

## Preconditions, Assertions, and Hypothetical Edge Cases

This project uses assertions to enforce programmer-facing preconditions and internal invariants.

When a function uses `assert` to validate arguments, indices, object state, or other established invariants:

* Treat the asserted condition as part of the contract unless invalid input is expected during normal operation.
* Do not report missing exception handling, error returns, or runtime validation merely because an assertion is used.
* Do not suggest replacing assertions with exceptions or error codes unless the API contract requires recoverable handling.
* Report an issue only when the code violates the established contract, makes an assertion insufficient, or allows invalid state to reach code that is expected to handle it safely.

More generally, avoid reporting purely theoretical or impractical edge cases unless the problematic input is realistically reachable, comes from untrusted input, or can plausibly cause a serious correctness, safety, or security failure.