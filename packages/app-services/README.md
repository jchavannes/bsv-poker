# `@bsv-poker/app-services`

The application layer over the untrusted network: session authentication (Ed25519 per seat), the
trust-boundary envelope validator, the lobby (signed joins + non-grindable seating), the networked
table clients (signed play, dual-pathed to the indexer), transcript rebuild, persistence, and the
production/network gate (play-money by default; mainnet refused without an explicit token).

## WHAT (security-relevant modules)

| Module | Role |
|---|---|
| `session-auth.ts` | Ed25519 session keys; `envelopeMessage` (the canonical signed message); verify. |
| `message-validation.ts` | `validateEnvelope`/`parseAndValidate` — the inbound trust boundary. |
| `network.ts` | RelayClient/IndexerClient — capability tokens, bounded JSON/SSE, register. |
| `network-gate.ts` | Play-money default; mainnet acknowledgement token; loopback enforcement. |
| `lobby.ts` | Signed joins; CSPRNG nonces; beacon (non-grindable) seating. |
| `interactive-client.ts` / `networked-table-client.ts` | Signed multiplayer play; validation at the boundary. |
| `transcript.ts` | Reconnect/rebuild from the (authenticated) transcript. |

## Security & invariants

- [`SECURITY.md`](./SECURITY.md) — attacker model, per-surface defences, trust boundary.
- [`INVARIANTS.md`](./INVARIANTS.md) — every claim mapped to its test. Background:
  [`PROTOCOL.md`](../../PROTOCOL.md), [`THREAT_MODEL.md`](../../THREAT_MODEL.md).

## Tests

```
node --test "packages/app-services/test/**/*.test.ts"
```
