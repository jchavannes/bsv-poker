/**
 * Networked table (§A6.5/§A7) — REAL multiplayer play over the relay. It constructs an
 * InteractiveNetworkedTableClient from the seated result, drives React state from its onUpdate
 * stream, renders seats/board/pot/timer via ui-core, and on the hero's turn raises the signing
 * modal (no silent signing, §A6.7) before calling client.submitAction(). client.play() resolves
 * when the hand completes → we show the showdown + settlement. Hole cards are rendered for the
 * hero's own seat (which we know). No game logic here — legality is read from the engine via the
 * update's `legal` / client.legalActions() (REQ-APP-052).
 *
 * The InteractiveNetworkedTableClient plays exactly ONE hand per instance (the entropy
 * commit/reveal handshake is per-hand); after settlement the player returns to the lobby. A
 * multi-hand session would re-run the handshake per hand — that is out of scope here and noted.
 */
import React, { useEffect, useMemo, useRef, useState } from 'react';
import {
  InteractiveNetworkedTableClient,
  type ClientUpdate,
  type SeatedResult,
} from '@bsv-poker/app-services';
import { RelayClient } from '@bsv-poker/app-services';
import type { Action } from '@bsv-poker/protocol-types';
import type { HoldemState } from '@bsv-poker/game-holdem';
import {
  tableViewModel,
  showdownViewModel,
  settlementViewModel,
  signingPromptVM,
  actionFromChoice,
  networkSeatLabel,
  type SigningPromptVM,
} from '@bsv-poker/ui-core/view-models';
import {
  MainnetBanner,
  SeatRing,
  Board,
  PotDisplay,
  ActionBar,
  TimerBanner,
  SigningModal,
  ShowdownPanel,
  SettlementSummary,
} from '@bsv-poker/ui-core/components';

export function NetworkTable(props: {
  relay: string;
  tableId: string;
  tableName: string;
  seated: SeatedResult;
  onLeave: () => void;
}): React.JSX.Element {
  const { seated } = props;
  const heroSeat = seated.mySeat;
  const ruleset = seated.ruleset;

  const startingStacks = useMemo(
    () => new Map(seated.seats.map((s) => [s.seat, s.stack])),
    [seated.seats],
  );

  // Construct the client once (per mount). play() runs the handshake then the hand.
  const clientRef = useRef<InteractiveNetworkedTableClient | null>(null);
  if (clientRef.current === null) {
    const entropy = new Uint8Array(32);
    (globalThis.crypto as Crypto).getRandomValues(entropy);
    clientRef.current = new InteractiveNetworkedTableClient({
      relay: new RelayClient(props.relay),
      tableId: props.tableId,
      mySeat: seated.mySeat,
      seats: seated.seats,
      ruleset: seated.ruleset,
      entropy,
    });
  }
  const client = clientRef.current;

  const [update, setUpdate] = useState<ClientUpdate | null>(null);
  const [finalState, setFinalState] = useState<HoldemState | null>(null);
  const [status, setStatus] = useState('Agreeing the deck (commit/reveal handshake)…');
  const [error, setError] = useState<string | null>(null);

  const [betAmount, setBetAmount] = useState(ruleset.blinds.bigBlind);
  const [prompt, setPrompt] = useState<SigningPromptVM | null>(null);
  const pendingAction = useRef<Action | null>(null);

  useEffect(() => {
    const off = client.onUpdate((u) => {
      setUpdate(u);
      if (!u.complete) setStatus('');
    });
    let cancelled = false;
    client
      .play()
      .then((s) => {
        if (!cancelled) setFinalState(s);
      })
      .catch((e) => {
        if (!cancelled) setError((e as Error).message);
      });
    return () => {
      cancelled = true;
      off();
    };
    // client is stable for the life of this component.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const state = update?.state ?? null;
  const heroHole = state ? (state.hole[heroSeat] ?? []) : [];
  const legal = update?.yourTurn ? client.legalActions() : null;

  const vm = useMemo(() => {
    if (!state) return null;
    return tableViewModel({
      state,
      heroSeat,
      heroHole,
      // When it is not our turn there are no legal actions for us; pass an empty descriptor.
      legal: legal ?? { check: false, fold: false },
      // Timeout/consequence text needs the engine's resolution; the interactive client does not
      // expose it, so we surface a neutral line (the engine still enforces turns over the relay).
      resolution: null,
      decisionMs: ruleset.timeouts.decisionMs,
    });
  }, [state, heroSeat, heroHole, legal, ruleset.timeouts.decisionMs]);

  function requestAction(
    choice: 'fold' | 'check' | 'call' | 'bet' | 'raise',
    amount: number,
  ): void {
    if (!legal) return;
    const action = actionFromChoice(choice, heroSeat, legal, amount);
    pendingAction.current = action;
    const toCall = legal.call ? legal.call.amount : 0;
    setPrompt(signingPromptVM(action, { potBefore: vm?.totalPot ?? 0, toCall }));
  }

  function confirmAction(): void {
    const action = pendingAction.current;
    setPrompt(null);
    pendingAction.current = null;
    if (action) client.submitAction(action);
  }

  function cancelAction(): void {
    setPrompt(null);
    pendingAction.current = null;
  }

  const seatLabel = useMemo(() => networkSeatLabel(seated.players), [seated.players]);

  const showdown = finalState ? showdownViewModel(finalState, startingStacks) : null;
  const settlement = finalState ? settlementViewModel(finalState, startingStacks) : null;

  return (
    <div style={{ maxWidth: 760, margin: '20px auto', padding: 16, display: 'grid', gap: 12 }}>
      <MainnetBanner regtest={ruleset.currency === 'play-regtest'} />

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h2 style={{ margin: 0 }}>
          {props.tableName} — blinds {ruleset.blinds.smallBlind}/{ruleset.blinds.bigBlind}
          {state ? ` (phase ${state.phase})` : ''}
        </h2>
        <button type="button" onClick={props.onLeave}>
          Leave
        </button>
      </div>

      {error && (
        <div role="alert" style={{ color: '#f88' }}>
          Table error: {error}
        </div>
      )}
      {status && !error && <div style={{ color: '#aaa' }}>{status}</div>}

      {vm && (
        <>
          <SeatRing seats={vm.seats} seatLabel={seatLabel} />
          <div>
            <div style={{ color: '#aaa', fontSize: 13 }}>Community</div>
            <Board board={vm.board} />
          </div>
          <PotDisplay pots={vm.pots} total={vm.totalPot} />
          <TimerBanner timer={vm.timer} />

          {!finalState ? (
            update?.yourTurn ? (
              <ActionBar
                vm={vm.actionBar}
                heroSeat={heroSeat}
                betAmount={betAmount}
                onBetAmountChange={setBetAmount}
                onAction={requestAction}
              />
            ) : (
              <div role="group" aria-label="actions" style={{ color: '#999', padding: 8 }}>
                Waiting for the other player(s)…
              </div>
            )
          ) : (
            <div style={{ display: 'grid', gap: 8 }}>
              {showdown && <ShowdownPanel vm={showdown} />}
              {settlement && <SettlementSummary vm={settlement} />}
              <p style={{ color: '#aaa', fontSize: 13 }}>
                Hand complete. A networked table plays one hand per session (the deck handshake is
                per-hand); return to the lobby to play another.
              </p>
              <button type="button" onClick={props.onLeave} style={{ padding: '8px 16px', fontSize: 16 }}>
                Back to lobby
              </button>
            </div>
          )}
        </>
      )}

      <SigningModal prompt={prompt} onConfirm={confirmAction} onCancel={cancelAction} />
    </div>
  );
}
