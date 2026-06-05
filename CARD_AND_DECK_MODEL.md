# Card and deck model (mental poker)

How the deck is shuffled with no trusted dealer and no single party knowing the order. Source:
`packages/crypto-mentalpoker/src/realct.ts`, `packages/app-services/src/{mp-shuffle,shuffle}.ts`.

## Construction

- **Cards** are `0..51` (`protocol-types/cards.ts`). A deck order is a strict permutation of `[0..52)`.
- **Commit / reveal** (core §4.1): each party derives a SECRET permutation from its own entropy,
  publishes `commit = SHA-256(entropy)` first, then later `reveal`s the entropy. Committing before
  revealing stops late-entropy selection (a party cannot choose its contribution after seeing others').
- **Shuffle order** = the **composition** of every party's secret permutation in canonical party
  order (`Π = π_N ∘ … ∘ π_1`). No single party knows the full order from its own entropy alone
  (`shuffledDeck`, `composePermutations`).
- **Combined per-card key** `Q_j` is a real secp256k1 point derived from the combined seed
  `σ = SHA-256(r_1 ‖ … ‖ r_N)` (`combinedKey`), rehashed on the negligible chance of an invalid scalar.

## Unbiased sampling (no modulo bias)

Each party's permutation is an **unbiased Fisher–Yates**: each step rejection-samples a uint32 from a
counter-mode PRF stream and discards values `>= floor(2^32/bound)*bound` before reducing — so the
selection is uniform, not modulo-biased. The rejection loop is **bounded** (`MAX_REJECTION_DRAWS`,
fail-closed) because the acceptance region is always `>= 1/2`, making >128 consecutive rejections a
`< 2^-128` event (`realct.ts` `permutationFromEntropy`). The browser play-money shuffle
(`app-services/shuffle.ts`) uses the same unbiased sampler over the platform CSPRNG and **fails closed
if no CSPRNG is present** (no `Math.random` fallback).

## Reveal verification (timing-safe)

Opening a card checks `H(face ‖ blind) == commitment` and entropy reveals check
`SHA-256(secret) == commitment` using **constant-time** comparison (`constantTimeEqualHex`,
CWE-208/697), so the match test leaks no timing.

## Security properties and where they're proven

| Property | Mechanism | Proof |
|---|---|---|
| No party fixes the order alone | composition of secret perms after commit-reveal | `crypto-mentalpoker` shuffle tests |
| No late-entropy selection | commit precedes reveal | commit/reveal flow tests |
| Uniform (unbiased) shuffle | rejection sampling, bounded | `permutationFromEntropy` test |
| Hidden until reveal | per-card hiding commitment `H(face‖blind)` | reveal/verify tests |
| Reveal match leaks no timing | constant-time hex compare | `INV-CT-2` |
| CSPRNG-only randomness | fail-closed; `Math.random` banned | `lint-security`, `INV-RND-*` |

## TRACKED ASSUMPTION

The two-round EC encryption (GB2616862, core §4.4) and the full in-script EC-derivation proof
(§19.C) are layered as the embedded node's interpreter gains the EC numeric opcodes; the Phase-1
combined-key + fair-play path is what is implemented and tested today.
