# Peer-to-peer mesh, game protocol, and chat

There is **no server** anywhere — no relay, no indexer, no directory service. Players talk directly.

## Gossip mesh (`P2PNode`)

- A node is a `TcpListener` plus outbound dials to peers given as `IP:port`.
- Messages are **flooded with deduplication**: each frame has an id, is forwarded to all other peers
  once, and is dropped if already seen. A frame therefore reaches the whole connected component exactly
  once per node (verified A—B—C in tests).
- **Presence and tables** are a serverless directory: nodes periodically announce themselves and the
  tables they host; peers learn of tables purely from gossip (verified across the mesh in tests).
- **Hardening:** a maximum connection count, per-peer inbound rate limiting, and anti-eviction so an
  established peer is not dropped for a flood of new connection attempts.

## Networked game protocol (`NetGame`)

A table is a channel keyed by its id. The id encodes the variant as `t-<hex>~<Variant>`, so when a peer
joins it already knows which of the six games to play with no extra handshake message.

Flow over the table channel:

1. `hello` — peers announce their public keys; **seats are assigned deterministically** by sorting the
   public keys, so both sides agree on who is in seat 0 without negotiation.
2. `commit` — each peer sends `SHA-256(entropy)`.
3. `reveal` — each peer sends `entropy`; both compose the identical dealerless deck
   (see [MENTAL_POKER.md](MENTAL_POKER.md)).
4. `act` — betting actions drive the shared `HoldemEngine` state.

A test plays a full networked hand and asserts both peers reach the same result with chips conserved.

## Encrypted chat (`ChatService`)

Direct messages and group chats, the Telegram/WhatsApp-style functions, with no server.

- **No key reuse, ever.** For each message and **each recipient**, a fresh ephemeral ECDH keypair is
  generated: `shared = ECDH(ephemeral_priv, recipient_pub)` → HKDF-SHA256 → AES-256-GCM. A group message
  is sealed independently for every member; a DM is a two-member conversation.
- **The wire is ciphertext.** A test wire-taps the DM topic and asserts the plaintext never appears,
  while the recipient still receives the decrypted message.
- **Topics** are derived deterministically from the members so all participants subscribe to the same
  conversation without a coordinator.
- **Persistence.** Conversations and messages are saved per-profile (atomic write) and reloaded on
  startup, so chat history survives a restart (verified in tests). Peers you can DM come from mesh
  presence.
