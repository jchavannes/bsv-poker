/**
 * @bsv-poker/ui-core — shared UI core (one core, two shells; §A5).
 *
 * Subpath exports:
 *   "@bsv-poker/ui-core/view-models" — pure projections (REQ-APP-051), React-free.
 *   "@bsv-poker/ui-core/components"  — presentational React (REQ-APP-052).
 *   "@bsv-poker/ui-core/store"       — tiny unidirectional store (REQ-APP-050).
 *
 * NOTE: this root entry intentionally re-exports ONLY the React-free view-models and store so
 * that consumers (e.g. app-services, and the root `tsc` typecheck) can import the package
 * without pulling JSX/React into a Node type-strip context. React components live behind the
 * "./components" subpath.
 */
export * from './view-models/index.ts';
export * from './store/index.ts';
