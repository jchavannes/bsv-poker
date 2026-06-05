# Seat assignment — non-grindable, Sybil boundary (audit #27)

How players who join a waiting room are assigned seats, and what an adversary can and cannot do.
Implemented in `packages/app-services/src/lobby.ts` (`joinWaitingRoom`); proven by
`packages/app-services/test/seat-ordering.test.ts` and `tools/lobby-e2e.ts`.

## The mechanism — commit-reveal seating

Seat order is a deterministic function of a **beacon** over every seated player's secret nonce, so
no party controls it alone. To stop a late joiner from biasing the beacon, the nonces are agreed by
**commit-reveal** (the same discipline as the deck shuffle):

1. **Commit phase.** Each player draws a CSPRNG nonce and broadcasts a signed `join` envelope carrying
   only the **commitment** `H(nonce)` (the signature binds the commitment + the player's pubkey —
   possession proof, audit 3). A player enters the reveal phase ONLY after it has seen a commitment
   from every seat.
2. **Reveal phase.** Each player broadcasts a signed `seat-reveal` with its **nonce**. A reveal is
   accepted only if it is from a seat that committed AND `H(nonce)` equals that seat's commitment
   (binding) AND the signature verifies.
3. **Seat.** `beacon = H(sorted pub:nonce over all seats)`; seat order = sort by `H(beacon‖pub)`.
   Deterministic — every peer derives the identical assignment.

## What this prevents

- **Grinding / last-mover advantage (the #27 fix).** Every nonce is FIXED (committed) before ANY
  nonce is disclosed. The first reveal can only be published by a party that has already seen all
  commitments, so by induction every commitment precedes every reveal globally
  (`seat-ordering.test.ts` asserts exactly this on the wire: no `seat-reveal` frame appears before all
  `join` commitments). A late joiner sees only hashes during the commit phase, so it gains nothing by
  delaying, and once it commits it cannot change its nonce.
- **Seat-by-pubkey selection.** The order key `H(beacon‖pub)` passes through the beacon, which is
  bound to every player's committed nonce — choosing a vanity pubkey cannot target a seat.
- **Spoofing a pubkey.** Joins and reveals are Ed25519-signed by the seat key (possession proof).

## The Sybil boundary (stated plainly)

Non-grindability is **cryptographic**; Sybil resistance is **economic** and lives one layer up:

- Each seat costs a **buy-in** debited from the player's wallet (`WalletService.buyIn`, enforced in the
  app before `joinWaitingRoom`), so occupying N seats costs N buy-ins. There is no cryptographic
  identity/stake gate in the lobby itself — a party willing to fund N independent buy-ins can present
  N seats, exactly as N distinct funded players could.
- This is the intended trust model for play-money regtest tables (the only Phase-1 currency): the cost
  of a seat, not an identity oracle, is the admission control. A future value-bearing deployment that
  needs stronger Sybil resistance would add a per-seat bond/stake at admission (the bond machinery
  already exists — `bondRevealOrForfeitLocking`, see [`KEY-LIFECYCLE.md`](KEY-LIFECYCLE.md) and the
  forfeiture path) — but that is an admission-policy choice, not a seating-fairness fix; the seating
  itself is non-grindable regardless of how many seats a party funds.
