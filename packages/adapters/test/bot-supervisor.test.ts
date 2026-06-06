// Proves guaranteed teardown: after shutdown, ZERO spawned children survive (no orphans).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnBot, shutdownAll, liveCount } from '../src/bot-supervisor.ts';

const RUN_FOREVER = ['-e', 'setInterval(() => {}, 1 << 30)'];
const alive = (pid: number): boolean => {
  try {
    process.kill(pid, 0); // signal 0 = existence check
    return true;
  } catch {
    return false;
  }
};
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

test('shutdownAll kills every spawned child — zero survivors', async () => {
  const bots = [spawnBot(process.execPath, RUN_FOREVER), spawnBot(process.execPath, RUN_FOREVER), spawnBot(process.execPath, RUN_FOREVER)];
  const pids = bots.map((b) => b.pid as number);
  for (const p of pids) assert.equal(typeof p, 'number');
  await sleep(150);
  for (const p of pids) assert.equal(alive(p), true, `bot ${p} should be running before shutdown`);

  shutdownAll();
  await sleep(400); // let taskkill/SIGKILL take effect

  for (const p of pids) assert.equal(alive(p), false, `bot ${p} SURVIVED shutdown — orphan = total fail`);
  assert.equal(liveCount(), 0);
});

test('a child that spawns its own grandchild is torn down whole-tree', async () => {
  // grandchild also runs forever; killing only the parent would orphan it
  const code = "const{spawn}=require('node:child_process'); spawn(process.execPath,['-e','setInterval(()=>{},1<<30)']); setInterval(()=>{},1<<30);";
  const parent = spawnBot(process.execPath, ['-e', code]);
  const ppid = parent.pid as number;
  await sleep(250);
  assert.equal(alive(ppid), true);
  shutdownAll();
  await sleep(500);
  assert.equal(alive(ppid), false, 'tree parent survived');
  assert.equal(liveCount(), 0);
});
