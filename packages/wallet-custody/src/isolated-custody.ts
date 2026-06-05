/**
 * Isolated custody boundary (audit finding #17 / AD-OPEN-3 — custody-trusted operations move to an
 * isolated worker). `createIsolatedCustody` runs the secret-key operations in a SEPARATE CHILD PROCESS
 * (a distinct OS address space), so the master key is NOT in the host client's memory: a dump of the
 * host process cannot recover the key or forge a signature. The host holds only a child handle and
 * pending-request map — never the scalar.
 *
 * The seed crosses to the child exactly ONCE at init; `createIsolatedCustody` then zeroizes the
 * caller's seed buffer (it consumes it) so no live copy lingers host-side. Signing/derivation are
 * async (they round-trip to the child), so this is the async counterpart of the sync `Custody`.
 */
import { fork, type ChildProcess } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import type { SignIntent } from './custody.ts';

/** Async custody whose keys live in a separate process (a memory-isolated boundary). */
export interface IsolatedCustody {
  /** HKDF-derived PUBLIC key for (gid, j, role) — computed in the worker; the scalar never crosses. */
  derive(gid: string, j: number, role: string): Promise<string>;
  /** Sign exactly the intent's bytes with the (gid,j,role) key, in the worker. */
  sign(gid: string, j: number, role: string, intent: SignIntent): Promise<Uint8Array>;
  /** The child process id (distinct from the host's — proves a separate address space). */
  readonly workerPid: number | undefined;
  /** Terminate the worker (and with it, the in-memory key). */
  dispose(): void;
}

type ResultMsg =
  | { type: 'result'; id: number; pub?: string; sig?: number[] }
  | { type: 'error'; id: number; error: string };

/**
 * Spawn the isolated custody worker, hand it the seed once, and return an async custody whose keys
 * live only in that worker process. CONSUMES `seed`: it is zeroized once delivered.
 */
export async function createIsolatedCustody(seed: Uint8Array): Promise<IsolatedCustody> {
  if (seed.length < 16) throw new Error('master key too short');
  const workerPath = fileURLToPath(new URL('./isolated-custody-worker.ts', import.meta.url));
  // Do NOT inherit `--test` (or other test flags) into the child, or the worker module would be run
  // as a test. Node 24 strips .ts types by default, so no extra exec flags are needed.
  const execArgv = process.execArgv.filter((a) => a !== '--test' && !a.startsWith('--test-'));
  const child: ChildProcess = fork(workerPath, [], { stdio: ['ignore', 'ignore', 'inherit', 'ipc'], execArgv });

  const pending = new Map<number, { resolve: (m: ResultMsg) => void; reject: (e: Error) => void }>();
  let nextId = 1;
  let disposed = false;

  child.on('message', (m: ResultMsg | { type: string }) => {
    if ((m as ResultMsg).type === 'result' || (m as ResultMsg).type === 'error') {
      const rm = m as ResultMsg;
      const p = pending.get(rm.id);
      if (!p) return;
      pending.delete(rm.id);
      if (rm.type === 'error') p.reject(new Error(rm.error));
      else p.resolve(rm);
    }
  });
  child.on('exit', () => {
    for (const p of pending.values()) p.reject(new Error('isolated custody worker exited'));
    pending.clear();
  });

  // Deliver the seed once, await readiness, then zeroize the host-side copy.
  await new Promise<void>((resolve, reject) => {
    const onReady = (m: { type: string }): void => {
      if (m.type === 'ready') {
        child.off('message', onReady);
        resolve();
      }
    };
    child.on('message', onReady);
    child.once('error', reject);
    child.send({ type: 'init', seed: Array.from(seed) });
  });
  seed.fill(0); // forward secrecy: the host keeps no copy of the key material

  const request = (msg: object): Promise<ResultMsg> => {
    if (disposed) return Promise.reject(new Error('isolated custody disposed'));
    const id = nextId++;
    return new Promise<ResultMsg>((resolve, reject) => {
      pending.set(id, { resolve, reject });
      child.send({ ...msg, id });
    });
  };

  return {
    workerPid: child.pid,
    async derive(gid, j, role) {
      const r = await request({ type: 'derive', gid, j, role });
      if (r.type !== 'result' || typeof r.pub !== 'string') throw new Error('derive failed');
      return r.pub;
    },
    async sign(gid, j, role, intent) {
      const r = await request({
        type: 'sign',
        gid,
        j,
        role,
        preimage: Array.from(intent.sighashPreimage),
        describe: intent.describe,
      });
      if (r.type !== 'result' || !r.sig) throw new Error('sign failed');
      return Uint8Array.from(r.sig);
    },
    dispose() {
      disposed = true;
      child.kill();
    },
  };
}
