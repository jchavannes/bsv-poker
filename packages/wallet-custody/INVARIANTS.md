# `@bsv-poker/wallet-custody` — Invariants

Run: `node --test "packages/wallet-custody/test/**/*.test.ts"`

| ID | Claim | Proof (test in `custody.test.ts`) |
|---|---|---|
| INV-WAL-1 | A 32-byte scalar builds a usable secp256k1 signing key. | `scalarToPrivateKey builds a usable secp256k1 signing key` |
| INV-WAL-2 | Derivation is deterministic per (gid, j, role); different inputs → different keys (REQ-WALLET-001). | `derive is deterministic per (gid,j,role); different inputs → different keys (REQ-WALLET-001)` |
| INV-WAL-3 | A custody signature validates through the REAL interpreter against the derived pubkey. | `a signature from custody validates through the real interpreter against the derived pubkey` |
| INV-WAL-4 | Mode A `reconstructAndSign` sums scalars and produces a valid signature (and rejects a zero sum). | `Mode A reconstructAndSign sums scalars and produces a valid signature` |
| INV-WAL-5 | The software backend refuses Mode B operations it cannot honestly perform (no false Mode-B claim). | `software custody refuses Mode B combineSignShare (must not claim Mode B under Mode A)` |
| INV-WAL-6 (negative) | A non-32-byte scalar is rejected. | `scalarToPrivateKey` throws (exercised by INV-WAL-1's guards) |

## To extend

A new derivation or signing path adds a positive test (it produces a valid, verifiable signature) and
negative tests (wrong context → different key; out-of-range/zero scalar → rejected; scalar never
returned to the caller).
