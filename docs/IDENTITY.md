# Identity — the Base ID key, Type-42, and the HMAC hash chain

> Status: living document, under active construction.

This is the cryptographic heart of the wallet and of the whole app: a single **identity** that pays, receives,
signs, encrypts, chats, plays, and owns NFTs — all from one root key that is itself **never** an address.

Code: `dotnet/src/BsvPoker.Core/Identity.cs` (`Type42`, `KeyChain`, `ChainedKey`, `KeyRing`, `IdentityPayment`)
on `dotnet/src/BsvPoker.Crypto/Secp256k1.cs`. Ported from the user's own `estates` implementation; not invented.

## 1. The Base ID key

**WHAT.** A secp256k1 key pair `(a, A=a·G)`. It is the user's identity — an NFT-equivalent they own and log in
with. It is **never** used as a payment address and never directly receives funds.

**WHY.** Reusing one key as both your public identity and your money address links every payment to your
identity and to each other. Separating them — identity public, money in fresh one-time keys — gives privacy and
forward-isolation: leaking one payment key reveals nothing about the identity or any other payment.

## 2. Type-42 derivation — paying an identity

For a payer with `(a, A)` and a payee identity `B`, and an agreed `invoice` string:

```
shared    = ECDH(payerPriv a, payeePub B)          # = ECDH(b, A); both sides get the same point
k         = HMAC-SHA256(key = shared, msg = invoice) mod n
childPub  = B + k·G          # the payer computes this → the one-time address to pay
childPriv = (b + k) mod n    # the payee computes this → the key that spends it
```

`PublicKey(childPriv) == childPub`, so the payee can spend exactly what the payer paid to, and **only** the
payee can (it needs `b`). A different `invoice` ⇒ a completely different one-time key — so every payment, and
every chat message, uses a fresh key that is never reused.

**Boundary.** `shared` depends on *both* parties' keys, so a third party who knows `B` and the `invoice` still
cannot derive `childPriv` (they lack `a` or `b`) — proven by the `IdentityTests` "wrong counterparty" case.

**In the wallet.** `IdentityPayment.PayToPub` / `SpendPriv`. Paying a handle/identity emits a claim receipt
`identity-payment|payerIdPub|invoice|txid`; the payee feeds it to "Claim a payment to my identity", which
derives `childPriv` and finds the coin by SPV.

## 3. The HMAC hash chain — ordered, one-use sub-keys

`KeyChain` derives a verifiable, ordered chain of sub-keys:

```
link[0] = SHA256("bsvpoker-keychain/v1" ‖ rootPub)
link[i] = SHA256(link[i-1] ‖ be32(i))
k[i]    = HMAC-SHA256(ECDH(rootPriv, counterpartyPub), link[i] ‖ be32(i)) mod n
childPriv[i] = (rootPriv + k[i]) mod n     childPub[i] = rootPub + k[i]·G
```

Each key binds an **index**, an **ECDH** secret, an **HMAC**, *and* the **prior link**. `KeyChain.Verify`
recomputes the whole chain from genesis and checks every link and that each pub matches its priv — so the set
is provably one ordered chain. Tamper with any earlier link and every later key fails to reproduce
(`IdentityTests` "tampering with any link breaks the chain").

**WHY this design.** Digital scarcity under nation-state attack needs every key provably linked, ordered, and
one-use, with no path from a sub-key (or a leaked link) back to the root or a sibling.

## 4. The KeyRing

`KeyRing` turns one 32-byte seed into an effectively unlimited space of unique keys: a fixed identity slot
(`IdentityPriv/Pub`, never rotates), fresh receive keys (`NextReceive`, a cursor so a key is never returned
twice), and per-message conversation keys (`MessagePriv/Pub`, ECDH-bound + advanced per sequence — a hash
chain so message A's key never equals message B's).

## 5. secp256k1 support added for this

`Secp256k1.ScalarAddModN(a,b) = (a+b) mod n` (the `root + k` of Type-42, rejects a zero result) and
`Secp256k1.PointAddCompressed(A,B) = A + B` (the `rootPub + k·G` public-side derivation, rejects infinity).
All on the in-tree pure-C# secp256k1 (no third-party library); no Ed25519, no BIP32/39/44.

## 6. One identity, everywhere

The same Base ID key is passed into the wallet, chat, the game, and NFT sealing, so: paying `@bob`, encrypting
a message to `@bob`, messaging `@bob`, and seating a hand against `@bob` all reference one identity; and card
NFTs are 1-sat outputs sealed to it (shown in the NFTs tab). It is integrated, not four separate keys.

## 7. Tests (executable claims)

`IdentityTests`: payer-pub == payee-priv's pub; fresh key per invoice; child ≠ root; wrong counterparty can't
derive; chain links/orders/verifies; any tampered link fails; wrong root rejected; KeyRing identity fixed &
receive keys unique; per-message counterparty derivation matches. Every claim has a positive and a hostile
negative test.
