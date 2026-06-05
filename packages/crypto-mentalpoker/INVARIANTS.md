# `@bsv-poker/crypto-mentalpoker` — Invariants

Run: `node --test "packages/crypto-mentalpoker/test/**/*.test.ts"`

| ID | Claim | Proof (test in `realct.test.ts`) |
|---|---|---|
| INV-MP-1 | The real CT passes the same CT conformance suite as the fake (a green run can't certify a wrong engine). | `real CT passes the CT conformance suite` |
| INV-MP-2 | Canonical party order is lexicographic by compressed pubkey, independent of arrival order (REQ-CRYPTO-003). | `canonical party order is lexicographic by compressed pubkey (REQ-CRYPTO-003)` |
| INV-MP-3 | A derived permutation is a genuine permutation of `[0..n)` (no repeats/omissions). | `permutation is a genuine permutation of [0..n)` |
| INV-MP-4 | The shuffle order depends on EVERY party's secret permutation (INV-CT-1) — no party fixes it alone. | `shuffle composes secret permutations (INV-CT-1): order depends on every party` |
| INV-MP-5 | Combined per-card keys are REAL secp256k1 compressed points (33 bytes, 0x02/0x03 prefix). | `combined keys are REAL secp256k1 compressed points (33 bytes, 02/03 prefix)` |
| INV-MP-6 | Commit/reveal match is constant-time. | `protocol-types INV-CT-2` (the shared comparator used here) |
| INV-MP-7 | The unbiased sampler's rejection loop is bounded (fail-closed). | `permutationFromEntropy` (`MAX_REJECTION_DRAWS`) — exercised by INV-MP-3 across sizes |

## To extend

A new crypto construction adds a design note (name/purpose/inputs/outputs/domain-separation/failure/
assumptions/non-goals/test-vectors/negative-tests) per the project rule, plus positive and negative
tests here.
