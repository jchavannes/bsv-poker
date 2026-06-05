# `@bsv-poker/sdk` — Security model

The assembled SDK surface that wires the deterministic core, the crypto/chain layer, and the adapters
into the API the shells use. Its security role is to compose those pieces **without weakening any
boundary** and to fail closed on adversarial use.

## Obligations

- **Compose, don't bypass:** the SDK must route security-critical operations to the REAL
  implementations (`crypto-mentalpoker`, real node), never to fakes (REQ-DEP-004), and must apply the
  same validation the underlying layers require.
- **Fail closed on adversarial input:** an out-of-turn or otherwise illegal request is rejected, not
  best-effort honoured (`adversarial.test.ts`).
- **No new trust:** the SDK adds no authority the layers below do not already grant; it does not, for
  example, accept an action the engine would reject or a tx the node would reject.

## Trust boundary

- **Trusted:** the validated outputs of the layers it composes.
- **Untrusted:** the caller's requests — checked against the same legality/validation the lower layers
  enforce.
- **Recoverable errors:** adversarial/illegal requests → rejection (fail-closed).
- **Side effects:** delegates to the composed layers (crypto, tx, node, services).

## What breaks if violated

If the SDK routed a security-critical op to a fake, or honoured an illegal request the engine would
reject, it would undermine a boundary the lower layers carefully enforce. The adversarial suite guards
the fail-closed behaviour.
