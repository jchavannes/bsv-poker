/**
 * Network lobby (§A6.3/§A6.4) — lists open tables from the relay (poll + manual refresh), lets a
 * player Join one, exposes a Create-table form, and a Practice-vs-bot button (the local flow).
 * Pure render + explicit handlers (no <form> submit, REQ-UI-003); validation/meta come from the
 * ui-core network-lobby view-model — this screen never recomputes rules.
 */
import React, { useCallback, useEffect, useState } from 'react';
import { LobbyClient, type OpenTable } from '@bsv-poker/app-services';
import { validateNetworkTable, type NetworkTableForm } from '@bsv-poker/ui-core/view-models';
import { MainnetBanner } from '@bsv-poker/ui-core/components';

export function NetworkLobby(props: {
  lobby: LobbyClient;
  relay: string;
  identityId: string;
  onCreate: (form: NetworkTableForm) => void;
  onJoin: (table: OpenTable) => void;
  onPractice: () => void;
  onDisconnect: () => void;
}): React.JSX.Element {
  const { lobby } = props;
  const [tables, setTables] = useState<OpenTable[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [name, setName] = useState("Friday night HU");
  const [smallBlind, setSmallBlind] = useState(1);
  const [bigBlind, setBigBlind] = useState(2);
  const [startingStack, setStartingStack] = useState(100);
  const [maxSeats, setMaxSeats] = useState(2);

  const form: NetworkTableForm = { name, smallBlind, bigBlind, startingStack, maxSeats };
  const validation = validateNetworkTable(form);

  const refresh = useCallback(async (): Promise<void> => {
    try {
      const list = await lobby.listTables();
      setTables(list);
      setLoadError(null);
    } catch (e) {
      setLoadError((e as Error).message);
    }
  }, [lobby]);

  useEffect(() => {
    void refresh();
    const id = setInterval(() => void refresh(), 3000);
    return () => clearInterval(id);
  }, [refresh]);

  return (
    <div style={{ maxWidth: 760, margin: '24px auto', padding: 16, display: 'grid', gap: 16 }}>
      <MainnetBanner regtest={true} />

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h1 style={{ margin: 0 }}>Lobby</h1>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <span style={{ color: '#888', fontSize: 13 }}>
            {props.relay} · <code>{props.identityId}</code>
          </span>
          <button type="button" onClick={props.onDisconnect}>
            Disconnect
          </button>
        </div>
      </div>

      <section style={{ border: '1px solid #444', borderRadius: 8, padding: 12 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h2 style={{ margin: 0, fontSize: 18 }}>Open tables</h2>
          <button type="button" onClick={() => void refresh()}>
            Refresh
          </button>
        </div>
        {loadError && <div style={{ color: '#f88', fontSize: 13 }}>Failed to load tables: {loadError}</div>}
        {tables.length === 0 ? (
          <p style={{ color: '#999' }}>No open tables yet — create one below.</p>
        ) : (
          <ul style={{ listStyle: 'none', padding: 0, display: 'grid', gap: 8 }}>
            {tables.map((t) => (
              <li
                key={t.id}
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  border: '1px solid #555',
                  borderRadius: 6,
                  padding: 8,
                }}
              >
                <span>
                  <strong>{t.meta.name}</strong>{' '}
                  <span style={{ color: '#aaa' }}>
                    — {t.meta.variant} NL {t.meta.smallBlind}/{t.meta.bigBlind}, stack{' '}
                    {t.meta.startingStack}, {t.meta.maxSeats} seats · {t.members} present
                  </span>
                </span>
                <button type="button" onClick={() => props.onJoin(t)}>
                  Join
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section
        role="group"
        aria-label="create table"
        style={{ border: '1px solid #444', borderRadius: 8, padding: 12, display: 'grid', gap: 10 }}
      >
        <h2 style={{ margin: 0, fontSize: 18 }}>Create a table (Hold'em)</h2>
        <label>
          Name{' '}
          <input type="text" value={name} onChange={(e) => setName(e.target.value)} style={{ width: 240 }} />
        </label>
        <label>
          Small blind{' '}
          <input type="number" min={1} value={smallBlind} onChange={(e) => setSmallBlind(Number(e.target.value))} />
        </label>
        <label>
          Big blind{' '}
          <input type="number" min={2} value={bigBlind} onChange={(e) => setBigBlind(Number(e.target.value))} />
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
          Max seats{' '}
          <input type="number" min={2} max={9} value={maxSeats} onChange={(e) => setMaxSeats(Number(e.target.value))} />
        </label>
        {!validation.ok && (
          <ul style={{ color: '#f88', margin: 0 }}>
            {validation.errors.map((e) => (
              <li key={e}>{e}</li>
            ))}
          </ul>
        )}
        <button
          type="button"
          disabled={!validation.ok}
          onClick={() => props.onCreate(form)}
          style={{ padding: '8px 16px', fontSize: 16 }}
        >
          Create &amp; open waiting room
        </button>
      </section>

      <section style={{ border: '1px solid #444', borderRadius: 8, padding: 12 }}>
        <h2 style={{ margin: 0, fontSize: 18 }}>Offline</h2>
        <p style={{ color: '#999', marginTop: 6 }}>
          Heads-up vs a simple bot, on the real engine — no relay needed.
        </p>
        <button type="button" onClick={props.onPractice} style={{ padding: '8px 16px', fontSize: 16 }}>
          Practice vs bot
        </button>
      </section>
    </div>
  );
}
