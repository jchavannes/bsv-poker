/**
 * App shell — a small screen state machine for REAL relay-backed multiplayer plus the offline
 * practice flow:
 *
 *   Connect → Lobby → WaitingRoom → NetworkTable      (real multiplayer over the relay)
 *   Connect/Lobby → Practice (local Table vs bot)     (the existing offline engine flow)
 *
 * IMPORTANT (browser-bundle scope): this app imports ONLY the browser-safe workspace packages
 * via ui-core / app-services: protocol-types (pure-TS sha256), hand-eval, engine, game-holdem,
 * and the relay transport (fetch/SSE in network.ts). It NEVER imports crypto-mentalpoker /
 * script-templates-ts / tx-builder / wallet-custody — those use node:crypto and are the Node SDK
 * path (multi-party mental-poker shuffle + on-chain signing/transactions), a later phase (§A2.3).
 *
 * What is REAL here: table discovery, the waiting room, the per-hand entropy commit/reveal
 * handshake, and interactive play — all over the relay (two browsers play each other). What is a
 * STUB for this phase: the per-session "pub" is a random hex for seat ordering (not a real key),
 * and there is no on-chain transaction/custody (the SigningModal authorises an engine move only).
 */
import React, { useCallback, useMemo, useRef, useState } from 'react';
import {
  LobbyClient,
  LocalTableClient,
  RelayClient,
  type OpenTable,
  type SeatedResult,
  type TableMeta,
} from '@bsv-poker/app-services';
import {
  rulesetFromForm,
  generateIdentity,
  type NetworkTableForm,
  type SessionIdentity,
} from '@bsv-poker/ui-core/view-models';
import type { Ruleset } from '@bsv-poker/protocol-types';
import { Connect } from './screens/Connect.tsx';
import { NetworkLobby } from './screens/NetworkLobby.tsx';
import { WaitingRoom } from './screens/WaitingRoom.tsx';
import { NetworkTable } from './screens/NetworkTable.tsx';
import { Lobby } from './screens/Lobby.tsx';
import { Table } from './screens/Table.tsx';
import type { TableCreateForm } from '@bsv-poker/ui-core/view-models';

type Screen =
  | { kind: 'connect' }
  | { kind: 'lobby' }
  | { kind: 'waiting' }
  | { kind: 'networkTable'; tableId: string; tableName: string; seated: SeatedResult }
  | { kind: 'practiceForm' }
  | { kind: 'practiceTable'; client: LocalTableClient; ruleset: Ruleset };

function metaFromForm(form: NetworkTableForm): TableMeta {
  return {
    name: form.name.trim(),
    variant: 'holdem',
    smallBlind: form.smallBlind,
    bigBlind: form.bigBlind,
    startingStack: form.startingStack,
    maxSeats: form.maxSeats,
  };
}

export function App(): React.JSX.Element {
  const identity = useMemo<SessionIdentity>(() => generateIdentity(), []);

  const [screen, setScreen] = useState<Screen>({ kind: 'connect' });
  const [relay, setRelay] = useState('http://localhost:8091');
  const [connecting, setConnecting] = useState(false);
  const [connectError, setConnectError] = useState<string | null>(null);
  const lobbyRef = useRef<LobbyClient | null>(null);

  // Waiting-room state (lives in App so it survives across renders while the join is in flight).
  const [waitName, setWaitName] = useState('');
  const [waitCapacity, setWaitCapacity] = useState(2);
  const [waitPlayers, setWaitPlayers] = useState<{ id: string; pub: string }[]>([]);
  const [waitError, setWaitError] = useState<string | null>(null);
  const abortRef = useRef<(() => void) | null>(null);

  const connect = useCallback(
    async (base: string): Promise<void> => {
      setConnecting(true);
      setConnectError(null);
      const lobby = new LobbyClient(new RelayClient(base));
      try {
        // listTables doubles as a connectivity check (CORS / relay reachable).
        await lobby.listTables();
        lobbyRef.current = lobby;
        setRelay(base);
        setScreen({ kind: 'lobby' });
      } catch (e) {
        setConnectError(`Could not reach relay: ${(e as Error).message}`);
      } finally {
        setConnecting(false);
      }
    },
    [],
  );

  const enterWaitingRoom = useCallback(
    (tableId: string, meta: TableMeta): void => {
      const lobby = lobbyRef.current;
      if (!lobby) return;
      setWaitName(meta.name);
      setWaitCapacity(meta.maxSeats);
      setWaitPlayers([{ id: identity.id, pub: identity.pub }]);
      setWaitError(null);
      setScreen({ kind: 'waiting' });

      const { seated, abort } = lobby.joinWaitingRoom(
        tableId,
        { id: identity.id, pub: identity.pub },
        meta,
        (players) => setWaitPlayers([...players]),
      );
      abortRef.current = abort;
      seated.then(
        (result) => {
          abortRef.current = null;
          setScreen({ kind: 'networkTable', tableId, tableName: meta.name, seated: result });
        },
        (e) => setWaitError((e as Error).message),
      );
    },
    [identity],
  );

  const createTable = useCallback(
    async (form: NetworkTableForm): Promise<void> => {
      const lobby = lobbyRef.current;
      if (!lobby) return;
      const meta = metaFromForm(form);
      try {
        const tableId = await lobby.createTable(meta);
        enterWaitingRoom(tableId, meta);
      } catch (e) {
        setConnectError(`Create failed: ${(e as Error).message}`);
      }
    },
    [enterWaitingRoom],
  );

  const joinTable = useCallback(
    (table: OpenTable): void => {
      enterWaitingRoom(table.id, table.meta);
    },
    [enterWaitingRoom],
  );

  const cancelWaiting = useCallback((): void => {
    abortRef.current?.();
    abortRef.current = null;
    setScreen({ kind: 'lobby' });
  }, []);

  function startPractice(form: TableCreateForm): void {
    const ruleset = rulesetFromForm(form);
    const client = new LocalTableClient({ ruleset, heroSeat: 0 });
    setScreen({ kind: 'practiceTable', client, ruleset });
  }

  switch (screen.kind) {
    case 'connect':
      return (
        <Connect
          defaultRelay={relay}
          identityId={identity.id}
          connecting={connecting}
          error={connectError}
          onConnect={(base) => void connect(base)}
          onPractice={() => setScreen({ kind: 'practiceForm' })}
        />
      );

    case 'lobby':
      return (
        <NetworkLobby
          lobby={lobbyRef.current!}
          relay={relay}
          identityId={identity.id}
          onCreate={(form) => void createTable(form)}
          onJoin={joinTable}
          onPractice={() => setScreen({ kind: 'practiceForm' })}
          onDisconnect={() => {
            lobbyRef.current = null;
            setScreen({ kind: 'connect' });
          }}
        />
      );

    case 'waiting':
      return (
        <WaitingRoom
          tableName={waitName}
          capacity={waitCapacity}
          players={waitPlayers}
          myId={identity.id}
          error={waitError}
          onCancel={cancelWaiting}
        />
      );

    case 'networkTable':
      return (
        <NetworkTable
          relay={relay}
          tableId={screen.tableId}
          tableName={screen.tableName}
          seated={screen.seated}
          onLeave={() => setScreen({ kind: 'lobby' })}
        />
      );

    case 'practiceForm':
      return <Lobby onStart={startPractice} />;

    case 'practiceTable':
      return (
        <Table
          client={screen.client}
          ruleset={screen.ruleset}
          onLeave={() => setScreen(lobbyRef.current ? { kind: 'lobby' } : { kind: 'connect' })}
        />
      );
  }
}
