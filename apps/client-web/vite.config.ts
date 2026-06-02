import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Vite/esbuild compiles the workspace TS sources (packages export ./src/*.ts directly).
// Only the browser-safe packages are imported by the app (see App.tsx) — the node:crypto
// packages (crypto-mentalpoker, tx-builder, wallet-custody, script-templates-ts) are NOT
// referenced anywhere in this bundle.
export default defineConfig({
  plugins: [react()],
  build: {
    target: 'es2022',
    outDir: 'dist',
  },
});
