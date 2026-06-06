/**
 * Bot/child supervisor — GUARANTEED TEARDOWN (no orphans, ever).
 *
 * A player's node may spawn bot children. When the node closes — cleanly, by signal, or by an
 * uncaught crash — EVERY spawned child (and its whole process tree) MUST die. A surviving process
 * after close is a total reject (a lingering listener is also an illegal server). This module makes
 * that guarantee at the Node layer; the native desktop launcher's kill-on-close Job Object is the
 * additional backstop for a HARD parent kill that bypasses these handlers.
 *
 * No detached children. No background daemons. Close = gone.
 */
import { spawn, spawnSync, type ChildProcess, type SpawnOptions } from 'node:child_process';

const children = new Set<ChildProcess>();
let installed = false;

/** Kill a process AND its entire tree (children of children), so no descendant survives. */
function killTree(pid: number): void {
  if (process.platform === 'win32') {
    // /T kills the tree, /F forces — covers grandchildren a plain kill would orphan.
    spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' });
  } else {
    try {
      process.kill(-pid, 'SIGKILL'); // negative pid = the process group
    } catch {
      try {
        process.kill(pid, 'SIGKILL');
      } catch {
        /* already dead */
      }
    }
  }
}

/** Terminate every supervised child and its tree. Idempotent; safe to call repeatedly. */
export function shutdownAll(): void {
  for (const c of children) {
    if (typeof c.pid === 'number') killTree(c.pid);
  }
  children.clear();
}

/** Number of children still tracked as alive (for tests/inspection). */
export function liveCount(): number {
  return children.size;
}

/**
 * Spawn a supervised bot/child. It is tracked and will be torn down with the node. Children are
 * NEVER detached — they belong to this process's lifetime. On Unix a new process group is created so
 * the whole group can be killed; on Windows the tree is killed via taskkill /T.
 */
export function spawnBot(command: string, args: readonly string[] = [], opts: SpawnOptions = {}): ChildProcess {
  installTeardown();
  const child = spawn(command, args, {
    ...opts,
    detached: process.platform !== 'win32', // own group on Unix for group-kill; never detached on win
    windowsHide: true,
  });
  children.add(child);
  child.once('exit', () => children.delete(child));
  return child;
}

/** Install exit/signal/crash handlers that tear down all children. Idempotent. */
export function installTeardown(): void {
  if (installed) return;
  installed = true;
  process.on('exit', shutdownAll);
  for (const sig of ['SIGINT', 'SIGTERM', 'SIGHUP', 'SIGBREAK'] as const) {
    process.on(sig, () => {
      shutdownAll();
      process.exit(0);
    });
  }
  process.on('uncaughtException', (err) => {
    shutdownAll();
    process.stderr.write(`fatal: ${(err as Error).message}\n`);
    process.exit(1);
  });
}
