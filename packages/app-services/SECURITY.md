# `@bsv-poker/app-services` — Security model

The application layer that sits on the untrusted network: session authentication, the trust-boundary
envelope validator, the lobby, the networked table clients, transcript rebuild, and the
production/network gate. This is where hostile relay/peer/indexer bytes enter, so every boundary here
is treated as hostile. Source: `src/`.

## Attacker model

A malicious peer or relay that forges another seat's action, replays a signed action elsewhere, floods
or oversizes frames, sends malformed JSON, grinds a favourable seat, or tries to flip the client into
real-funds mode without authorisation.

## Defences (by surface)

| Surface | Threat | Defence | Test |
|---|---|---|---|
| `session-auth.ts` | Action forgery | every envelope Ed25519-signed; inbound verified against the seat's registered key. | `a seat-signed envelope verifies…`, `FORGERY rejected…` |
| `session-auth.ts` | Replay across table/hand/seat/state | the signed message binds tableId+hand+seat AND the prior state hash (`prev`). | `a signature does not replay…`, `a signed action binds its prior state hash…` |
| `session-auth.ts` | Key linkage | seat key derives deterministically from the wallet root with domain separation. | `seat key derives DETERMINISTICALLY…`, `different roots / purposes derive different keys` |
| `message-validation.ts` | Malformed/unknown envelope | `validateEnvelope` returns the typed envelope or `null` — never partial trust. | `valid … accepted`, `unrecognized or malformed … REJECTED`, `parseAndValidate rejects bad JSON` |
| `network.ts` | Oversize JSON / unframed SSE | `safeJsonParse` + bounded SSE/HTTP buffers (CWE-400). | `network.test.ts` |
| `network.ts` | Unauthorised channel | capability tokens minted/attached transparently; re-mint on 401/403. | `network.test.ts` (capability flow) |
| `network-gate.ts` | Unauthorised real funds | default play-money regtest; mainnet REFUSED without the exact acknowledgement token; loopback enforced. | `mainnet is REFUSED without the explicit acknowledgement token`, `local services bind to loopback by default…` |
| `lobby.ts` | Spoofed join / grindable seat | signed joins (mandatory); CSPRNG nonces; non-grindable beacon seating. | lobby tests; audit-02 #2/#9 |
| `transcript.ts` | Poisoned transcript | bounded record parse; constant-time commit check; hex-validated reveal. | `transcript` rebuild tests |

## Trust boundary

- **Trusted:** the local wallet root / session key, the deterministic engine.
- **Untrusted:** every relay frame, peer envelope, indexer record, and node response.
- **Recoverable errors:** any malformed/forged/oversize input → dropped/rejected; live play continues
  over authenticated frames.
- **Fatal errors:** none on hostile input (the boundary parsers never throw uncaught).
- **Side effects:** network I/O (relay/indexer) and local persistence; no LOAD-BEARING state is kept
  in browser localStorage (REQ-UI-002).

## What breaks if violated

Skipping the signature check lets a peer forge a fold/raise as you; dropping the `prev` binding lets a
signed action be replayed at a different state; enabling mainnet without the token risks real funds.
