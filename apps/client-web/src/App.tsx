/**
 * App shell — routes between the Lobby (create local table) and the Table (gameplay).
 *
 * IMPORTANT (browser-bundle scope): this app imports ONLY the browser-safe workspace packages
 * via ui-core / app-services: protocol-types (pure-TS sha256), hand-eval, engine, game-holdem.
 * It NEVER imports crypto-mentalpoker / script-templates-ts / tx-builder / wallet-custody —
 * those use node:crypto and are the Node SDK path (multi-party mental-poker shuffle, on-chain
 * signing + transactions), which is a later phase (§A2.3).
 */
import React, { useState } from 'react';
import { LocalTableClient } from '@bsv-poker/app-services';
import { rulesetFromForm, type TableCreateForm } from '@bsv-poker/ui-core/view-models';
import type { Ruleset } from '@bsv-poker/protocol-types';
import { Lobby } from './screens/Lobby.tsx';
import { Table } from './screens/Table.tsx';

interface Session {
  readonly client: LocalTableClient;
  readonly ruleset: Ruleset;
}

export function App(): React.JSX.Element {
  const [session, setSession] = useState<Session | null>(null);

  function start(form: TableCreateForm): void {
    const ruleset = rulesetFromForm(form);
    // Human is seat 0 (the button/SB preflop heads-up).
    const client = new LocalTableClient({ ruleset, heroSeat: 0 });
    setSession({ client, ruleset });
  }

  if (!session) {
    return <Lobby onStart={start} />;
  }
  return (
    <Table
      client={session.client}
      ruleset={session.ruleset}
      onLeave={() => setSession(null)}
    />
  );
}
