# CLAUDE.md

## Code style

- Never write comments. No inline comments, no doc comments, no explanatory comments.
- Prefer concise code.

## Speed

- Avoid operations that are slow relative to generating the code itself (compilation is often one). If you're confident enough in the code, skip them.

## Tests

- Only write unit tests, and only deterministic ones.
- Integration tests, or any other kind of test, must be asked for first — never write them unprompted.
- Never use `Task.Delay()` in tests. It is non-deterministic.
- Every test must be bounded by a timeout of 20 seconds maximum. Never exceed it.
- The whole test suite must finish fast. A test that takes seconds means something is wrong (usually a deadlock) — fix the cause, do not raise the timeout.
