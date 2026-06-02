import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createStore } from '../src/store/index.ts';

test('store holds the initial snapshot and projects new snapshots (unidirectional) — REQ-APP-050', () => {
  const s = createStore({ n: 1 });
  assert.deepEqual(s.getSnapshot(), { n: 1 });
  s.setSnapshot({ n: 2 });
  assert.deepEqual(s.getSnapshot(), { n: 2 });
});

test('store notifies every subscriber on setSnapshot and stops after unsubscribe', () => {
  const s = createStore(0);
  let a = 0;
  let b = 0;
  const offA = s.subscribe(() => { a++; });
  s.subscribe(() => { b++; });
  s.setSnapshot(1);
  assert.equal(a, 1);
  assert.equal(b, 1);
  offA();
  s.setSnapshot(2);
  assert.equal(a, 1, 'unsubscribed listener no longer fires');
  assert.equal(b, 2, 'remaining listener still fires');
});

test('the store carries no business logic — it stores exactly what it is given', () => {
  const s = createStore<{ tag: string }>({ tag: 'init' });
  const next = { tag: 'projected' };
  s.setSnapshot(next);
  assert.equal(s.getSnapshot(), next, 'snapshot is the exact projected object (reducers project, store does not transform)');
});
