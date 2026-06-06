# The self-contained runtime ("the VM") — core §10 / D5

A reproducible stack that launches **node(regtest) + the player's own local node + client** with no
external services and **NO central server** (REQ-VM-001/002/003). Regtest by default; mainnet only
behind an explicit research flag (REQ-VM-007).

bsv-poker is fully **peer-to-peer**: there is no relay and no indexer. Every player runs their OWN
local node (`tools/local-node.ts`) — a P2P peer that bridges the browser (loopback HTTP/SSE) to the
mesh — and the nodes connect to **each other** over the mesh. The only infrastructure is the
decentralized BSV chain (the node).

## One-command bootstrap + self-test (no Docker required)

```
node tools/selftest.ts      # or: pnpm selftest
```

This stands up a 2-node P2P mesh (the serverless transport — no relay/indexer), exercises serverless
discovery (presence + the gossiped table directory) and a pub/sub round-trip across the mesh, runs a
full heads-up Hold'em hand through the engine (the client role), prints the transcript + final state
hash + payouts, and tears the mesh down. This is the Phase-0 gate check: "the peer-to-peer stack comes
up end-to-end, self-test passes" (core §17).

To watch two browsers play through their own local nodes (the exact serverless browser path):

```
node tools/browser-transport-e2e.ts
```

## Container packaging (REQ-VM-003)

```
docker compose -f vm/docker-compose.yml up --build
```

- `node-regtest` (:18332) — Phase-0 placeholder; the real **bonded-subsat-channel** embedded node
  binds next (D6, §10.2). The chain is the only infrastructure.
- `local-node` (:8090) — the player's OWN node: a P2P peer that exposes the browser's HTTP/SSE
  transport on loopback and bridges it to the mesh. NOT a central relay — one per player. This
  single-machine compose runs one local node (one peer) for a local demo; for real multiplayer each
  player runs their own and dials a peer with `--peer host:port`.
- `client` (:8080) — placeholder; the real `apps/client-web` (framework-free vanilla DOM, in-tree
  build — no Vite) ships via `apps/client-web/Dockerfile`, and talks ONLY to the player's own
  local-node on loopback.

Builds are reproducible (pinned toolchains, distroless/static images; REQ-VM-006). A literal
hypervisor image (OVA/qcow2) is an optional extra artifact from the same composition (D5, REQ-VM-005)
— not built by default.
