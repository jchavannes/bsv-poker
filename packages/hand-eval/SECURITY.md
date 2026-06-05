# `@bsv-poker/hand-eval` — Security model

Pure hand ranking / showdown evaluation. It decides **who wins the pot**, so a ranking bug directly
misallocates funds — that is its security weight, despite having no network surface.

## Why it is security-critical

- **Correctness = fund safety:** the ordering this module produces selects the pot winner(s). A wrong
  comparison hands money to the wrong seat.
- **Determinism:** every client must rank identically (P2). The evaluator is pure and reproduces a
  fixed oracle bit-for-bit, so two clients cannot disagree on a showdown.

## Trust boundary

- **Trusted:** the card sets handed in (already-revealed cards from the validated transcript).
- **Untrusted within the boundary:** inputs are well-formed cards (`isCard`); the evaluator does not
  trust card counts blindly — it ranks exactly the documented hand sizes per variant.
- **Recoverable / fatal:** invalid card values are rejected by the card type; there is no I/O to fail.
- **Side effects:** none (pure functions).

## What breaks if violated

A category or tie-break error awards the pot to the wrong hand. This is guarded by the §19.D vector
suite reproducing the oracle exactly and by the exhaustive-play settlement/conservation checks.
