/**
 * Test/e2e helper: stand up a real, multi-NODE peer-to-peer mesh with NO server. Each logical player
 * gets its OWN `P2PTransport` node on its own ephemeral loopback port; the nodes interconnect as a
 * star through node 0 (a star is a connected graph, so a frame published at any node floods to every
 * other node — node 0 is just a peer, not a server, and could be any participant). This replaces the
 * old `startService('apps/relay-go', …)` + `new RelayClient(url)` pattern: every e2e that used a
 * central relay now uses faithful peer nodes instead, and each transport is a structural `Relay`
 * passed DIRECTLY to `LobbyClient`/`InteractiveNetworkedTableClient` (no cast, no server process).
 */
import { P2PTransport } from '@bsv-poker/adapters/p2p-transport';

export interface P2PMesh {
  /** One transport per node, index-aligned with the requested count (node 0 is the star hub peer). */
  readonly transports: P2PTransport[];
  /** Tear the whole mesh down (every node leaves; no lingering listeners). */
  close(): void;
}

/**
 * Build an `n`-node P2P mesh (star through node 0) and wait until every node is connected, so a
 * subsequent publish reaches all peers. Ephemeral ports → no cross-e2e port collisions.
 */
export async function p2pMesh(n: number, timeoutMs = 10_000): Promise<P2PMesh> {
  if (!Number.isInteger(n) || n < 1) throw new Error('p2pMesh needs n >= 1');
  const hub = new P2PTransport(0);
  await hub.start([]);
  const hubPort = hub.boundPort();
  const transports: P2PTransport[] = [hub];
  for (let i = 1; i < n; i++) {
    const t = new P2PTransport(0);
    await t.start([{ host: '127.0.0.1', port: hubPort }]);
    transports.push(t);
  }
  // Wait for the star to fully form: hub sees n-1 peers, each spoke sees the hub.
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const ok = hub.peerCount() >= n - 1 && transports.slice(1).every((t) => t.peerCount() >= 1);
    if (ok) break;
    if (Date.now() > deadline) {
      for (const t of transports) t.close();
      throw new Error(`p2p mesh did not form within ${timeoutMs}ms (hub peers=${hub.peerCount()})`);
    }
    await new Promise((r) => setTimeout(r, 25));
  }
  return {
    transports,
    close: () => {
      for (const t of transports) t.close();
    },
  };
}
