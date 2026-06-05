# `@bsv-poker/adapters` — Security model

The conformance-bound contracts (CT / BS / VA / OB), the in-memory **fakes** for orchestration tests,
and the **real BSV node** client. Source: `src/`.

## The fake/real boundary rule (REQ-DEP-001/003/004)

- The fakes exist for orchestration wiring in unit/integration tests ONLY. They MUST pass the **same**
  conformance suite as the real adapters (`conformance.ts`), so a green run against a fake cannot
  certify a wrong engine.
- **Security-critical paths are NEVER tested against fakes** (REQ-DEP-004) — those use the real
  implementations (`crypto-mentalpoker`, the real node). The fakes still use real hashing and
  constant-time commit/reveal so they are genuinely conformant, not stubs.

## Real node client (`real-node.ts`) — a network trust boundary

- Speaks newline-delimited JSON-over-TCP to the embedded regtest node.
- **Untrusted input:** the node's responses are bounded-parsed (`safeJsonParse`) and the accumulation
  buffer is capped (`MAX_NODE_FRAME`, CWE-400) — a peer that never sends a newline cannot grow memory
  without limit. A non-object/oversize/timeout response is a recoverable error (rejects the call).
- **Trusted:** the node's consensus validation itself (it validates submitted txs through its real
  Script interpreter).

## Trust boundary (contracts)

- The VA contract establishes inclusion/integrity/selective-disclosure over DISCLOSED records only —
  **never** truth-at-origin (INV-VA-2). The OB contract's wrap is authenticated (never raw XOR).
- Recoverable errors: bad wrap, bad threshold, bond ≠ 1 sat — all throw and are surfaced.

## What breaks if violated

Running a security-critical path against a fake would let a green test certify a broken real engine.
Treating the node's response as trusted/unbounded reopens a DoS at the node boundary.
