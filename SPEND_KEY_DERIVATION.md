# Spend-key derivation

How an on-chain spend key is produced, and the rule that keeps stable identity off-chain. Source:
`packages/wallet-custody/src/custody.ts`, `packages/crypto-mentalpoker/src/realct.ts`,
`packages/script-templates-ts/src/templates.ts`.

## The rule

> The long-term wallet key authenticates the player. It must **never** appear directly as an on-chain
> spend key or address. Every on-chain spend key is **derived** with a fully domain-separated context.

## How a spend key is derived

- A per-card / per-game scalar is `w_(gid, j, role) = HKDF-SHA256(master, "<gid>:<j>:<role>")`,
  rejection-sampled into the valid secp256k1 range (`custody.ts` `deriveScalar`). Its **public** key
  is what appears on-chain (`derive(gid, j, role)` returns the compressed pubkey hex only).
- **Mode A** (Phase-1 default, core §4.3/§9.3): the combined per-card key `Q_j` is a real secp256k1
  point bound to the shuffle seed (`realct.ts` `combinedKey`). To spend the combined-key output, the
  disclosed scalars are **summed** (`w = Σ scalars mod n`) and that single scalar signs the spend
  (`custody.ts` `reconstructAndSign`); the sum is rejected if zero.
- The signature is a **low-S DER** ECDSA over the BIP-143 sighash (`script-templates-ts/signing.ts`,
  `tx-builder/wire.ts`), which `OP_CHECKSIG` verifies inside the interpreter.

## Derivation context (every field is mandatory)

`gid` (game id) · `j` (card / output index) · `role`. A missing or wrong field is a derivation
failure, not a silently different key. The context is what makes each on-chain key unique and
unlinkable; see `KEY_LIFECYCLE.md` for why reuse is forbidden.

## Positive / negative claims (mapped to tests)

| Claim | Positive | Negative |
|---|---|---|
| A derived public key signs and verifies through the real interpreter. | `script-templates-ts/test/templates.test.ts` (funding/fold/settlement pass) | wrong key → CHECKSIG fails *inside* the interpreter |
| The combined-key spend uses the summed disclosed scalars. | `crypto-mentalpoker` reconstruct/sign path | a zero combined scalar is rejected (`reconstructAndSign` throws) |
| Derivation is deterministic and single-game. | `wallet-custody/test` derive determinism | a different `gid`/`j`/`role` yields a different key |

## WHAT MUST NEVER BE ASSUMED / WHAT BREAKS

- Never derive a spend key from public/deterministic data only — it would be linkable/guessable.
- Never reuse an address/pkh — reuse links spends and can strand funds.
- Never let the stable identity key appear as a spend key or address.

> Status note: Mode A (single-game disclosed-scalar summation) is the Phase-1 path. Mode B
> (non-disclosing) is a TRACKED ASSUMPTION in `custody.ts`. The full per-output unlinkable-address
> discipline above is the design rule; the Phase-1 templates implement the combined-key + fair-play
> path proven on the regtest node (`ONCHAIN_MODEL.md`).
