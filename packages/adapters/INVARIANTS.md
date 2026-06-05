# `@bsv-poker/adapters` — Invariants

Run: `node --test "packages/adapters/test/**/*.test.ts"`

## Conformance (fakes must pass the same suite as the real adapters) — `conformance.test.ts`

| ID | Claim | Proof (test) |
|---|---|---|
| INV-ADP-1 | The CT fake passes the CT conformance suite. | `CT fake passes the CT conformance suite` |
| INV-ADP-2 | The BS fake passes the BS conformance suite. | `BS fake passes the BS conformance suite` |
| INV-ADP-3 | The VA fake passes the VA conformance suite. | `VA fake passes the VA conformance suite` |
| INV-ADP-4 | The OB fake passes the OB conformance suite. | `OB fake passes the OB conformance suite` |

The real CT adapter passes the SAME CT suite (`crypto-mentalpoker` INV-MP-1), so a green fake run and a
green real run certify the same contract.

## Architecture / seams — `architecture.test.ts`, `seams.test.ts`

| ID | Claim | Proof |
|---|---|---|
| INV-ADP-5 | The contract seams are honoured (no security-critical path is wired to a fake). | `architecture.test.ts`, `seams.test.ts` |
| INV-ADP-6 | Fake commit/reveal is constant-time and uses real hashing (genuinely conformant). | `fakes.ts` uses `constantTimeEqualHex` (shared `INV-CT-2`) |

## Real node client (boundary)

| ID | Claim | Proof |
|---|---|---|
| INV-ADP-7 | The node response is bounded-parsed and the socket buffer capped (CWE-400). | `real-node.ts` (`safeJsonParse` + `MAX_NODE_FRAME`); exercised by the on-chain e2es |

## To extend

A new adapter adds a conformance test that BOTH the fake and the real implementation must pass, and
(for any network adapter) a bounded-input guard with a negative test.
