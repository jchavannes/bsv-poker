/**
 * Lobby / create-local-table screen (§A6.3/§A6.4). Pick blinds/stack → start a local table.
 * No <form> submit — explicit onClick (REQ-UI-003). Validation comes from the ui-core
 * view-model; this screen only renders and emits.
 */
import React, { useState } from 'react';
import {
  validateTableCreate,
  type TableCreateForm,
} from '@bsv-poker/ui-core/view-models';
import { MainnetBanner } from '@bsv-poker/ui-core/components';

export function Lobby(props: { onStart: (form: TableCreateForm) => void }): React.JSX.Element {
  const [smallBlind, setSmallBlind] = useState(1);
  const [bigBlind, setBigBlind] = useState(2);
  const [startingStack, setStartingStack] = useState(100);
  const [decisionMs, setDecisionMs] = useState(30000);

  const form: TableCreateForm = { smallBlind, bigBlind, startingStack, decisionMs };
  const validation = validateTableCreate(form);

  return (
    <div style={{ maxWidth: 520, margin: '40px auto', padding: 16 }}>
      <MainnetBanner regtest={true} />
      <h1>BSV Poker — Practice vs bot (offline)</h1>
      <p style={{ color: '#aaa' }}>
        Heads-up No-Limit Texas Hold'em vs a simple bot, played hot-seat in your browser on the
        real game engine — no relay needed. For real multiplayer over the relay, go back and
        Connect to a relay. The on-chain crypto/transactions are the Node SDK path (§A2.3) and are
        not wired into this browser bundle.
      </p>

      <div role="group" aria-label="create table" style={{ display: 'grid', gap: 10 }}>
        <label>
          Small blind{' '}
          <input
            type="number"
            min={1}
            value={smallBlind}
            onChange={(e) => setSmallBlind(Number(e.target.value))}
          />
        </label>
        <label>
          Big blind{' '}
          <input
            type="number"
            min={2}
            value={bigBlind}
            onChange={(e) => setBigBlind(Number(e.target.value))}
          />
        </label>
        <label>
          Starting stack{' '}
          <input
            type="number"
            min={4}
            value={startingStack}
            onChange={(e) => setStartingStack(Number(e.target.value))}
          />
        </label>
        <label>
          Decision time (ms){' '}
          <input
            type="number"
            min={1000}
            step={1000}
            value={decisionMs}
            onChange={(e) => setDecisionMs(Number(e.target.value))}
          />
        </label>

        {!validation.ok && (
          <ul style={{ color: '#f88' }}>
            {validation.errors.map((e) => (
              <li key={e}>{e}</li>
            ))}
          </ul>
        )}

        <button
          type="button"
          disabled={!validation.ok}
          onClick={() => props.onStart(form)}
          style={{ padding: '8px 16px', fontSize: 16 }}
        >
          Start table
        </button>
      </div>
    </div>
  );
}
