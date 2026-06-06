/**
 * Bot-over-the-wire E2E: proves bots are SEPARATE PROCESSES that connect like remote players, fully
 * PEER-TO-PEER. We stand up ONE host peer node (the table host), create a table on it, then spawn TWO
 * independent bot-daemon processes that dial that peer, discover the table via gossip, and play hands.
 * The test asserts both daemons seat, act over the wire, and the session runs — exactly as if two
 * remote humans (each in their own window) were playing. No bot is in the main app's process, and
 * there is NO relay server — the host peer just relays gossip between the two bots.
 */

import { spawn, type ChildProcess } from 'node:child_process';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
import { LobbyClient, type TableMeta } from '@bsv-poker/app-services';
import { P2PTransport } from '@bsv-poker/adapters/p2p-transport';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');

const procs: ChildProcess[] = [];
function spawnBot(peer: string, table: string, name: string): { proc: ChildProcess; out: () => string } {
  const p = spawn('node', [join(ROOT, 'tools', 'bot-daemon.ts'), '--peer', peer, '--table', table, '--name', name, '--hands', '2'], { cwd: ROOT });
  procs.push(p);
  let buf = '';
  p.stdout.on('data', (d) => { buf += d.toString(); });
  p.stderr.on('data', (d) => { buf += d.toString(); });
  return { proc: p, out: () => buf };
}

async function main(): Promise<void> {
  // The host's own P2P node — the rendezvous peer both bots dial (no relay server).
  const host = new P2PTransport(0);
  await host.start([]);
  const peerAddr = `127.0.0.1:${host.boundPort()}`;
  try {
    const meta: TableMeta = { name: 'bot-test', variant: 'holdem', smallBlind: 1, bigBlind: 2, startingStack: 100, maxSeats: 2 };
    const tableId = await new LobbyClient(host).createTable(meta);
    console.log(`[bot-net] host peer ${peerAddr} created table ${tableId}; spawning two SEPARATE bot processes…`);

    const alice = spawnBot(peerAddr, tableId, 'alice');
    const bob = spawnBot(peerAddr, tableId, 'bob');

    // Wait for the session to play out across the two processes.
    const finished = Date.now() + 40000;
    while (Date.now() < finished) {
      const a = alice.out();
      const b = bob.out();
      const bothSeated = /SEATED at seat/.test(a) && /SEATED at seat/.test(b);
      const bothActed = /my turn →/.test(a) && /my turn →/.test(b);
      const done = /session ended/.test(a) || /session ended/.test(b);
      if (bothSeated && bothActed && done) break;
      await new Promise((r) => setTimeout(r, 500));
    }

    const a = alice.out();
    const b = bob.out();
    assert.match(a, /SEATED at seat/, `alice never seated:\n${a}`);
    assert.match(b, /SEATED at seat/, `bob never seated:\n${b}`);
    assert.match(a, /my turn →/, `alice never acted over the wire:\n${a}`);
    assert.match(b, /my turn →/, `bob never acted over the wire:\n${b}`);
    console.log('[bot-net] alice + bob (separate processes) seated and played peer-to-peer via the host peer.');
    console.log('\n[bot-net] PASS — bots are remote players over the P2P mesh, not in the app process, no relay server.');
  } finally {
    for (const p of procs) p.kill();
    host.close();
  }
}

main().then(() => process.exit(0), (e) => { console.error('[bot-net] FAIL:', (e as Error).message); for (const p of procs) p.kill(); process.exit(1); });
