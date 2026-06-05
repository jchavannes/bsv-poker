# `@bsv-poker/app-services` — Invariants

Run: `node --test "packages/app-services/test/**/*.test.ts"`

## Session authentication — `session-auth.test.ts`

| ID | Claim | Proof (test) |
|---|---|---|
| INV-APS-1 | A seat-signed envelope verifies against the seat key (audit 1–3). | `a seat-signed envelope verifies against the seat key (audit 1–3)` |
| INV-APS-2 | An action forged for seat 1 by an attacker is NOT valid under seat 1's key. | `FORGERY rejected: an action for seat 1 signed by an attacker is NOT valid under seat 1’s key` |
| INV-APS-3 | The unsigned-fold audit exploit has no valid signature for the seat. | `UNSIGNED forged fold (the audit exploit) has no valid signature for the seat` |
| INV-APS-4 | A signature does not replay across table / hand / seat. | `a signature does not replay across table / hand / seat (binding)` |
| INV-APS-5 | The seat key derives deterministically from the wallet root (same root → same key). | `seat key derives DETERMINISTICALLY from a root … same root → same key` |
| INV-APS-6 | Different roots / purposes derive different keys (domain separation). | `different roots / different purposes derive different keys (domain separation)` |
| INV-APS-7 | A signed action binds its prior state hash — no replay against a different state (audit 8). | `a signed action binds its prior state hash — cannot be replayed against a different state (audit 8)` |

## Trust-boundary envelope validation — `message-validation.test.ts`

| ID | Claim | Proof (test) |
|---|---|---|
| INV-APS-8 | Valid commit/reveal/action envelopes are accepted (REQ-APP-103). | `valid commit/reveal/action envelopes are accepted (REQ-APP-103)` |
| INV-APS-9 | Unrecognized/malformed envelopes are REJECTED, never partially trusted. | `unrecognized or malformed envelopes are REJECTED (never partially trusted)` |
| INV-APS-10 | `parseAndValidate` rejects bad JSON and bad envelopes from the wire. | `parseAndValidate rejects bad JSON and bad envelopes from the wire` |

## Production / network gate — `network-gate.test.ts`

| ID | Claim | Proof (test) |
|---|---|---|
| INV-APS-11 | Default is play-money regtest with a no-real-value banner (REQ-PROD-012). | `default is play-money regtest with a no-real-value banner (REQ-PROD-012)` |
| INV-APS-12 | Mainnet is REFUSED without the explicit acknowledgement token. | `mainnet is REFUSED without the explicit acknowledgement token` |
| INV-APS-13 | Local services bind to loopback by default; non-loopback refused without opt-in (REQ-APP-106). | `local services bind to loopback by default; non-loopback is refused without opt-in (REQ-APP-106)` |

## Network boundaries + clients

| ID | Claim | Proof |
|---|---|---|
| INV-APS-14 | The relay client attaches/mints capability tokens and bounds responses. | `network.test.ts` |
| INV-APS-15 | Transcript rebuild from records reproduces live state (bounded parse, constant-time commit). | `tools/reconnect-e2e.ts`, `tools/validating-indexer-e2e.ts` |

## Accountable action/handshake timeout (audit 3) — `timeout-claim.test.ts`

| ID | Claim | Proof (test) |
|---|---|---|
| INV-APS-16 | A seat that stalls on its ACTION is dropped at the anchored deadline (engine check-or-fold default) and the honest clients converge byte-for-byte. | `a stalled seat is dropped at the anchored deadline and the two honest clients CONVERGE (audit 3)` |
| INV-APS-17 | No premature drop below the deadline; a properly-signed but premature/forged claim is rejected (the seat is NOT dropped). | `NO premature drop while below the deadline; a forged low-deadline claim is rejected (audit 3)` |
| INV-APS-18 | A seat that commits but never reveals is dropped; the survivors re-derive the deck among themselves and converge (the non-responder is excluded from the hand). Two-phase commit-before-reveal ordering preserved (late-entropy protection). | `HANDSHAKE drop: a seat that commits but never reveals is dropped; survivors re-derive the deck and CONVERGE (audit 3)` |
| INV-APS-19 | Malformed timeout-claims are rejected at the trust boundary (missing/negative/non-integer `d`, missing `subject`, self-claim). | `message-validation.test.ts` |

## To extend

A new network boundary adds a bounded-parse + validation step and the negative tests for malformed/
forged/oversize input; a new signed message extends `envelopeMessage` and re-proves non-replay.
