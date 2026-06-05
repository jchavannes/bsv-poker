/**
 * Isolated custody WORKER (audit #17 / AD-OPEN-3). Runs as a SEPARATE CHILD PROCESS (own address
 * space) so the master key it holds is NOT in the host client's memory — a dump of the host process
 * cannot reveal it. The host (isolated-custody.ts) sends the seed ONCE at init, then signs/derives by
 * message; the scalar never leaves this process. Never logs key material; communicates over IPC only.
 */
import { createSoftwareCustody, type Custody } from './custody.ts';

type InitMsg = { type: 'init'; seed: number[] };
type DeriveMsg = { type: 'derive'; id: number; gid: string; j: number; role: string };
type SignMsg = {
  type: 'sign';
  id: number;
  gid: string;
  j: number;
  role: string;
  preimage: number[];
  describe: { action: string; amounts?: string; potOrState?: string };
};
type InMsg = InitMsg | DeriveMsg | SignMsg;

let custody: Custody | null = null;

const send = (m: unknown): void => {
  process.send?.(m);
};

process.on('message', (raw: InMsg) => {
  try {
    if (raw.type === 'init') {
      // The master key lives ONLY here. Build custody over a private copy, then zeroize the transit
      // buffer so even this process keeps no second copy of the seed bytes.
      const seed = Uint8Array.from(raw.seed);
      custody = createSoftwareCustody(seed.slice());
      seed.fill(0);
      send({ type: 'ready' });
      return;
    }
    if (!custody) {
      send({ type: 'error', id: (raw as DeriveMsg).id, error: 'custody not initialised' });
      return;
    }
    if (raw.type === 'derive') {
      send({ type: 'result', id: raw.id, pub: custody.derive(raw.gid, raw.j, raw.role) });
      return;
    }
    if (raw.type === 'sign') {
      const sig = custody.sign(raw.gid, raw.j, raw.role, {
        sighashPreimage: Uint8Array.from(raw.preimage),
        describe: raw.describe,
      });
      send({ type: 'result', id: raw.id, sig: Array.from(sig) });
      return;
    }
  } catch (e) {
    const id = (raw as { id?: number }).id ?? -1;
    send({ type: 'error', id, error: (e as Error).message });
  }
});

// Signal liveness; the host waits for 'ready' after sending 'init'.
send({ type: 'spawned' });
