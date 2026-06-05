/**
 * Connect screen (§A6.3) — framework-free (vanilla DOM): enter the relay base URL and connect to the
 * lobby. Explicit onClick (no <form> submit, REQ-UI-003). The REGTEST/play-money banner is shown
 * here and everywhere. The relay text is a controlled input whose value lives in the app model; the
 * focus-preserving `mount` keeps the caret while typing across re-renders.
 */
import { el, type Child } from '@bsv-poker/ui-core/dom';
import { mainnetBanner } from '@bsv-poker/ui-core/components';

export interface ConnectProps {
  readonly defaultRelay: string;
  readonly relay: string;
  readonly onRelayChange: (value: string) => void;
  readonly identityId: string;
  readonly onConnect: (relay: string) => void;
  readonly connecting: boolean;
  readonly error: string | null;
}

export function connectScreen(p: ConnectProps): HTMLElement {
  const trimmed = p.relay.trim();
  const valid = /^https?:\/\//i.test(trimmed);

  return el('div', { style: { maxWidth: '520px', margin: '40px auto', padding: '16px', display: 'grid', gap: '12px' } },
    mainnetBanner(true),
    el('h1', { style: { margin: '0' } }, 'BSV Poker — Multiplayer'),
    el('p', { style: { color: '#aaa', margin: '0' } },
      'Connect to a relay to find a table and play real opponents over the wire. The waiting room and ' +
      'interactive play are real (over the relay); the on-chain crypto/transactions are the Node SDK ' +
      'path (§A2.3) and are not in this browser bundle.'),
    el('div', { style: { color: '#888', fontSize: '13px' } },
      'Your session identity: ', el('code', {}, p.identityId)),

    el('label', { style: { display: 'grid', gap: '4px' } }, 'Relay base URL',
      el('input', {
        type: 'url', 'aria-label': 'relay base url', value: p.relay, placeholder: 'http://localhost:8091',
        onInput: (e: Event) => p.onRelayChange((e.target as HTMLInputElement).value),
        style: { padding: '6px', fontSize: '14px' },
      })),
    (!valid && trimmed.length > 0) ? el('div', { style: { color: '#f88', fontSize: '13px' } }, 'Enter a http(s):// URL.') : false as Child,
    p.error ? el('div', { style: { color: '#f88', fontSize: '13px' } }, p.error) : false as Child,

    el('div', { style: { display: 'flex', gap: '8px' } },
      el('button', {
        type: 'button', disabled: !valid || p.connecting, onClick: () => p.onConnect(trimmed),
        style: { padding: '8px 16px', fontSize: '16px' },
      }, p.connecting ? 'Connecting…' : 'Connect')),
  );
}
