# Mental poker and card NFTs

## Dealerless shuffle (`MentalPoker`)

There is no trusted dealer and no single party that knows the deck order.

1. **Commit.** Before anyone reveals anything, each player publishes `SHA-256(entropy)` for a fresh
   32-byte random `entropy`. Committing first stops a player from choosing entropy after seeing others'.
2. **Reveal.** Each player reveals their `entropy`; everyone checks it against the commitment
   (`VerifyCommit`, constant-time compare).
3. **Compose.** Each player's entropy seeds an **unbiased** Fisher–Yates permutation of the deck. The
   draw stream is an HMAC-SHA256 counter stream; rejection sampling removes modulo bias (the rejection
   bound is computed in 64-bit to avoid the overflow that a 32-bit bound hits when it divides 2³²). The
   final deck order is the **composition** of every player's permutation, so no single player can fix it.
4. **Per-card keys.** `CombinedKey(seed, j)` is a real secp256k1 point derived from the combined seed,
   bound to card position `j` (rehashing on the rare invalid scalar).

This is verified by tests: composed permutations are valid bijections, the shuffle is deterministic
given the same entropies, and two networked peers independently compute the identical deck.

> **Roadmap — true hole-card privacy.** Today, once both peers reveal entropy they can both reconstruct
> the deck, so hole cards are not yet cryptographically private from the opponent during play. The
> planned upgrade is a commutative-encryption deal (secp256k1 scalar masking: every player masks every
> card, cards are dealt by other players stripping their masks so only the recipient can unmask theirs),
> which keeps each hole card private until showdown while remaining verifiable. This is secp256k1-only
> and adds no new dependency.

## Cards as NFTs (`CardNft`)

Every card a player holds is a 1-satoshi NFT in their wallet.

- **Seal.** A card is sealed to its owner with an ECIES-style scheme over secp256k1 (ephemeral ECDH →
  HKDF → AES-256-GCM). Only the owner's key can open it; an opponent's sealed card cannot be read.
- **Lock script.** The NFT's locking script binds `H(sealed)` as pushed data with `OP_DROP`, followed by
  `<pubkey> OP_CHECKSIG`. It **never** uses `OP_RETURN`. Tests assert the script *structure*
  (`OP_PUSHDATA … OP_DROP … OP_CHECKSIG`) rather than scanning raw bytes, because a `0x6a` byte can
  legitimately appear inside pushed hash data.
- **Transfer.** Transferring a card re-seals it to the new owner, so the sender loses the ability to open
  it. Tampering with a sealed blob fails the AES-GCM tag and is rejected.

The owned cards are persisted per-profile in the **Card Vault** and shown in the Wallet tab, so closing
the app never loses them.
