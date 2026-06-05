# `@bsv-poker/crypto-mentalpoker`

The SECURITY-CRITICAL mental-poker crypto: commit/reveal of per-party entropy, the secret-permutation
shuffle (composition of every party's permutation), and the real secp256k1 combined per-card keys.
Never run against a fake (REQ-DEP-004).

## WHAT / HOW / WHY

- **commit/reveal** (`entropyCommitSync`, `makeRealCT().entropyReveal`): bind entropy before
  revealing, so no party selects after seeing others'. Reveal match is constant-time.
- **shuffle** (`permutationFromEntropy`, `composePermutations`, `shuffledDeck`): each party derives a
  SECRET permutation from its entropy via a counter-mode PRF and an **unbiased**, bounded
  rejection-sampled Fisher–Yates; the deck order is their composition — no single party knows it.
- **combined keys** (`combinedSeed`, `combinedKey`): a real secp256k1 point per card derived from the
  combined seed `SHA-256(r_1‖…‖r_N)`.

WHY this design and what it defends is in [`SECURITY.md`](./SECURITY.md) and the system-level
[`CARD_AND_DECK_MODEL.md`](../../CARD_AND_DECK_MODEL.md).

## Security & invariants

- [`SECURITY.md`](./SECURITY.md) — attacker model, defences, trust boundary.
- [`INVARIANTS.md`](./INVARIANTS.md) — every claim mapped to its test.

## Tests

```
node --test "packages/crypto-mentalpoker/test/**/*.test.ts"
```
