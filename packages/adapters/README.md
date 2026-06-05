# `@bsv-poker/adapters`

The conformance-bound contracts (CT mental-poker, BS bonded-subsat channel, VA verifiable-accumulator,
OB oblivious-broadcast), the in-memory **fakes** for orchestration tests, and the **real BSV node**
client (`real-node.ts`).

## WHAT / WHY

- **Contracts** define the seams the SDK wires; the **fakes** implement them for tests and MUST pass
  the same conformance suite as the real adapters — so a green run against a fake cannot certify a
  wrong engine (REQ-DEP-001/003).
- **Security-critical paths never use a fake** (REQ-DEP-004); they use `crypto-mentalpoker` and the
  real node. The fakes still use real hashing + constant-time commit/reveal.
- **`real-node.ts`** is a network trust boundary: node responses are bounded-parsed and the socket
  buffer is capped (CWE-400).

## Security & invariants

- [`SECURITY.md`](./SECURITY.md) — the fake/real rule, the node boundary, the VA/OB limits.
- [`INVARIANTS.md`](./INVARIANTS.md) — conformance + boundary claims → tests.

## Tests

```
node --test "packages/adapters/test/**/*.test.ts"
```
