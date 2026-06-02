/**
 * A tiny unidirectional store (REQ-APP-050). It holds NO business logic: it only stores a
 * render snapshot and notifies subscribers. The game logic lives in the engine (via the
 * app-services client); this store just projects that output into render state and lets React
 * subscribe via useSyncExternalStore.
 */

export interface Store<T> {
  getSnapshot(): T;
  setSnapshot(next: T): void;
  subscribe(listener: () => void): () => void;
}

export function createStore<T>(initial: T): Store<T> {
  let snapshot = initial;
  const listeners = new Set<() => void>();
  return {
    getSnapshot() {
      return snapshot;
    },
    setSnapshot(next: T) {
      snapshot = next;
      for (const l of listeners) l();
    },
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}
