# ADR 0004 — Framework-free clients: vanilla DOM + in-tree build (web), native Win32 + WebView2 (desktop)

Status: **Accepted**. Supersedes the React/Vite (web) and Tauri/Rust (desktop) choices implied by the
original app architecture (§A3, §A5).

## Context

This is reference cryptographic infrastructure with a standalone mandate: it must rely on **no
external system and no external code** beyond the language toolchain and operating-system components.
The clients had drifted from that bar:

- The **web client** used **React + react-dom** (view) and **Vite + @vitejs/plugin-react** (build).
- The **desktop shell** was a **Tauri v2 (Rust)** app — and, by its own README, *could not even be
  compiled in this environment*.

A UI framework and a bundler are large external dependency graphs whose internals an auditor cannot
fully see; Tauri adds an entire second language toolchain (Rust + a crate graph). None of this is
security-critical, but the standalone mandate is repo-wide: "stand alone or not done."

## Decision

**Web — vanilla DOM + in-tree build.** The view layer is an in-tree ~90-line toolkit,
`ui-core/src/dom.ts` (`el` / `mount` / `text`), over the standard browser DOM API: no virtual DOM, no
reconciliation, no framework. Screens are pure renders of a model slice; the controller
(`apps/client-web/src/app.ts`) owns all state and re-renders via a tiny store. The build
(`apps/client-web/build.ts`) is `tsc` type-strip + a generated ES-module **import map** — the
browser's own module loader is the runtime; there is no Rollup/esbuild/Vite.

**Desktop — native Win32 + WebView2.** The desktop client is a true native Windows application in C
(`apps/client-desktop/native/main.c`) against the Windows SDK + Microsoft Edge **WebView2**. It hosts
the SAME audited web client (one core, not a fork), maps it from the local build folder
(`SetVirtualHostNameToFolderMapping` — no HTTP server), and supervises the Go services under a
kill-on-close Job Object. The supervisor lifecycle policy is a pure, unit-tested C module
(`native/lifecycle.c` + `native/test-lifecycle.c`).

## Why (and why the alternatives were rejected)

- **Auditability over convenience.** Every line of the view layer and the build is now in-tree and
  reviewable. A framework/bundler hides behaviour behind thousands of lines an auditor must take on
  trust.
- **No XSS surface by construction.** `dom.ts` sets text via `textContent` only (never `innerHTML`),
  so relay/user-derived strings cannot become markup. This is an executable claim — `verify-dom.ts`
  asserts it (positive + negative) in a real headless browser.
- **One core, not a fork.** Both clients render the identical TypeScript engine; the desktop shell
  does not re-implement the security core in C (which would *increase* audit surface, not reduce it).
- **Rejected: keep React/Vite behind a "view-layer-only" caveat.** The mandate is repo-wide; a
  "not security-critical, so it's fine" caveat is exactly the kind of implicit exception the standard
  forbids.
- **Rejected: hand-roll the WebView2 COM ABI** to avoid vendoring a header. The official MIDL header
  is the authoritative ABI contract (with inline IIDs); hand-declaring ~40 vtable slots by hand is a
  real ABI-correctness hazard. Vendoring Microsoft's header + static loader (provenance in
  `native/THIRD-PARTY.md`) is the same category as depending on `windows.h`.
- **Rejected: a full native GDI re-rendering of the table.** It would duplicate the entire client UI
  and game projection in C — more code, a second thing to audit, and divergence risk — for no
  security gain. WebView2 renders the one audited client natively.

## Consequences

- Removed deps: `react`, `react-dom`, `@types/react`, `@types/react-dom`, `vite`,
  `@vitejs/plugin-react`, `@tauri-apps/cli`, and the entire Rust/Tauri `src-tauri` tree (pruned from
  `pnpm-lock.yaml`).
- New toolchain needs: the **MSVC C++ Build Tools** (`cl.exe`) + the **WebView2 runtime** for the
  desktop build (Windows only; CI skips these stages where the toolchain is absent).
- CI gains: view-layer typecheck (`tsconfig.web.json`), in-tree web build, headless web render check,
  headless DOM view-core tests, native desktop build, native lifecycle unit tests, and a headless
  WebView2 render check — all green.
- A single render bar — "the lobby actually mounts" — is enforced identically for the web bundle and
  the native host.
