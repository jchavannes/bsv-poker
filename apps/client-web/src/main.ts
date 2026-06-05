/**
 * Browser entry point — framework-free (vanilla DOM). Builds the app controller and appends its root
 * element into `#root`. No React, no createRoot, no StrictMode: `createApp()` returns a live DOM tree
 * that re-renders itself via the in-tree `mount` store.
 */
import { createApp } from './app.ts';

const host = document.getElementById('root');
if (!host) throw new Error('#root not found');
host.appendChild(createApp());
