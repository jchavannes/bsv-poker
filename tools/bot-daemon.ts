/**
 * Bot daemon — a SIMULATED REMOTE PLAYER, run as its own process, that connects to a table over the
 * relay socket exactly like a human's client (core §8, D4). The bot is NOT in the main app and has
 * no in-process access to anyone else's state (that would be a cheat): it has its own identity +
 * entropy, joins the waiting room over the relay, and plays its seat by submitting actions over the
 * same networked protocol a human uses. Run two windows (app + bot, or two bots) to really test
 * multiplayer over the wire.
 *
 *   node tools/bot-daemon.ts --relay http://127.0.0.1:8091 [--table <id>] [--name alice] [--strategy passive|aggressive]
 */

import { randomBytes } from 'node:crypto';
import { createServer } from 'node:http';
import {
  RelayClient,
  LobbyClient,
  InteractiveNetworkedTableClient,
  type ClientUpdate,
  type OpenTable,
} from '@bsv-poker/app-services';
import { genKeyPair } from '@bsv-poker/script-templates-ts';
import { bytesToHex, cardToString, type Action, type GameState, type LegalActions } from '@bsv-poker/protocol-types';

function arg(name: string, fallback?: string): string | undefined {
  const i = process.argv.indexOf(name);
  return i >= 0 && i + 1 < process.argv.length ? process.argv[i + 1] : fallback;
}

const RELAY = arg('--relay', 'http://127.0.0.1:8091')!;
const NAME = arg('--name', `bot-${Math.random().toString(36).slice(2, 6)}`)!;
const WANT_TABLE = arg('--table');
const STRATEGY = arg('--strategy', 'passive')!;
const MAX_HANDS = Number(arg('--hands', '100'));
const GUI_PORT = arg('--gui') ? Number(arg('--gui')) : 0;

// Live view the GUI polls — so you can WATCH the bot play in its own browser window.
const view: { seat: number; status: string; state: GameState | null; log: string[] } = { seat: -1, status: 'starting', state: null, log: [] };
const log = (m: string): void => {
  console.log(`[bot ${NAME}] ${m}`);
  view.log.push(`${new Date().toLocaleTimeString()}  ${m}`);
  if (view.log.length > 100) view.log.shift();
};

function startGui(port: number): void {
  createServer((req, res) => {
    if (req.url === '/state') {
      const s = view.state;
      const body = {
        name: NAME,
        seat: view.seat,
        status: view.status,
        phase: s?.phase ?? '—',
        handComplete: s?.handComplete ?? false,
        hole: s && view.seat >= 0 ? (s.hole?.[view.seat] ?? []).map(cardToString) : [],
        board: (s?.board ?? []).map(cardToString),
        seats: (s?.seats ?? []).map((x) => ({ seat: x.seat, stack: x.stack, folded: x.folded })),
        pot: (s?.pots ?? []).reduce((a, p) => a + p.amount, 0),
        log: view.log.slice(-30),
      };
      res.writeHead(200, { 'content-type': 'application/json', 'access-control-allow-origin': '*' });
      res.end(JSON.stringify(body));
      return;
    }
    res.writeHead(200, { 'content-type': 'text/html' });
    res.end(`<!doctype html><meta charset=utf8><title>BOT ${NAME}</title>
<style>body{background:#0b3d2e;color:#eee;font:14px system-ui;margin:0;padding:16px}
.card{display:inline-block;background:#fff;color:#111;border-radius:4px;padding:4px 7px;margin:2px;font-weight:700}
h1{font-size:18px}.seat{padding:2px 0}.log{white-space:pre-wrap;background:#06241b;border-radius:6px;padding:8px;height:240px;overflow:auto;font-family:monospace;font-size:12px}</style>
<h1>🤖 BOT "${NAME}" — remote player over the socket</h1>
<div id=hdr></div><div>Your hand: <span id=hole></span></div><div>Board: <span id=board></span></div>
<div id=seats style=margin:8px:0></div><div class=log id=log></div>
<script>
async function tick(){try{const r=await fetch('/state');const s=await r.json();
document.getElementById('hdr').textContent='seat '+s.seat+' · '+s.status+' · phase '+s.phase+' · pot '+s.pot+(s.handComplete?' · HAND COMPLETE':'');
const cards=a=>a.map(c=>'<span class=card>'+c+'</span>').join('')||'—';
document.getElementById('hole').innerHTML=cards(s.hole);
document.getElementById('board').innerHTML=cards(s.board);
document.getElementById('seats').innerHTML=s.seats.map(x=>'<div class=seat>seat '+x.seat+(x.seat===s.seat?' (me)':'')+': '+x.stack+(x.folded?' folded':'')+'</div>').join('');
document.getElementById('log').textContent=s.log.join('\\n');
}catch(e){}}
setInterval(tick,500);tick();
</script>`);
  }).listen(port, '127.0.0.1', () => console.log(`[bot ${NAME}] GUI: watch this bot at http://127.0.0.1:${port}/`));
}

function chooseAction(legal: LegalActions, seat: number): Action {
  if (STRATEGY === 'aggressive') {
    if (legal.raise) return { kind: 'raise', seat, amount: legal.raise.max };
    if (legal.bet) return { kind: 'bet', seat, amount: legal.bet.max };
  }
  if (legal.check) return { kind: 'check', seat, amount: 0 };
  if (legal.draw) return { kind: 'stand', seat, amount: 0 };
  if (legal.call) return { kind: 'call', seat, amount: legal.call.amount };
  return { kind: 'fold', seat, amount: 0 };
}

async function findTable(lobby: LobbyClient): Promise<OpenTable> {
  if (process.argv.includes('--create')) {
    const meta = {
      name: arg('--name', 'bot-table')!,
      variant: (arg('--variant', 'holdem') as OpenTable['meta']['variant']),
      smallBlind: Number(arg('--sb', '1')),
      bigBlind: Number(arg('--bb', '2')),
      startingStack: Number(arg('--stack', '100')),
      maxSeats: Number(arg('--seats', '2')),
    };
    const id = await lobby.createTable(meta);
    log(`hosting a new table ${id} (${meta.variant}, ${meta.maxSeats} seats)`);
    return { id, meta, members: 0 };
  }
  const deadline = Date.now() + 120000;
  for (;;) {
    const tables = await lobby.listTables().catch(() => [] as OpenTable[]);
    const t = WANT_TABLE ? tables.find((x) => x.id === WANT_TABLE) : tables[0];
    if (t) return t;
    if (Date.now() > deadline) throw new Error('no open table to join (timed out)');
    log('waiting for an open table…');
    await new Promise((r) => setTimeout(r, 1000));
  }
}

async function main(): Promise<void> {
  const relay = new RelayClient(RELAY);
  const lobby = new LobbyClient(relay);
  const id = `${NAME}-${Math.random().toString(36).slice(2, 8)}`;
  const pub = bytesToHex(genKeyPair().pubCompressed); // the bot's OWN identity key

  log(`connecting to relay ${RELAY} as a remote player…`);
  const table = await findTable(lobby);
  log(`joining table ${table.id} (${table.meta.variant}, ${table.meta.maxSeats} seats) over the socket`);

  if (GUI_PORT) startGui(GUI_PORT);
  view.status = 'joining';

  const { seated } = lobby.joinWaitingRoom(table.id, { id, pub }, table.meta, (players) =>
    log(`waiting room: ${players.length}/${table.meta.maxSeats} players`),
  );
  const s = await seated;
  view.seat = s.mySeat;
  view.status = 'playing';
  log(`SEATED at seat ${s.mySeat}; opponents are remote over the relay`);

  const client = new InteractiveNetworkedTableClient({
    relay,
    tableId: table.id,
    mySeat: s.mySeat,
    seats: s.seats,
    ruleset: s.ruleset,
    entropy: new Uint8Array(randomBytes(32)),
  });

  client.onUpdate((u: ClientUpdate) => {
    view.state = u.state;
    if (u.legal) {
      const a = chooseAction(u.legal, s.mySeat);
      client.submitAction(a);
      log(`my turn → ${a.kind}${a.amount ? ' ' + a.amount : ''}`);
    }
  });

  log(`playing up to ${MAX_HANDS} hands over the wire…`);
  await client.playSession({ maxHands: MAX_HANDS });
  view.status = 'ended';
  log('session ended (busted, table empty, or hand cap reached)');
  // Keep the GUI up after the session so you can still watch the final table.
  if (GUI_PORT) await new Promise<void>(() => {});
}

main().then(() => process.exit(0), (e) => { console.error(`[bot ${NAME}] FAIL:`, (e as Error).message); process.exit(1); });
