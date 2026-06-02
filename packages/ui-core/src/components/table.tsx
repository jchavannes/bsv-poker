/**
 * Table-screen presentational components (REQ-APP-052). All pure render of view-model props.
 * Explicit onClick/onChange handlers, NO <form> submit (REQ-UI-003). No business logic: the
 * ActionBar reads legalBets from the engine-supplied descriptor and never computes legality.
 */
import React from 'react';
import { CardBack, CardChip, Banner } from './primitives.tsx';
import type {
  SeatVM,
  PotVM,
  ActionBarVM,
  TimerVM,
  TableViewModel,
} from '../view-models/table.ts';

export function MainnetBanner(props: { regtest: boolean }): React.JSX.Element {
  // REQ-VM-007 / §A3.5 — unmissable. Phase-1 is always regtest play-money.
  return (
    <Banner tone={props.regtest ? 'warn' : 'error'}>
      {props.regtest
        ? 'REGTEST — play money. No real funds are at risk.'
        : 'MAINNET RESEARCH MODE — real value at risk.'}
    </Banner>
  );
}

export function Board(props: { board: TableViewModel['board'] }): React.JSX.Element {
  return (
    <div aria-label="community cards" style={{ display: 'flex', minHeight: 60 }}>
      {props.board.length === 0 ? (
        <span style={{ color: '#999', alignSelf: 'center' }}>(no community cards yet)</span>
      ) : (
        props.board.map((c) => <CardChip key={c.code} card={c} />)
      )}
    </div>
  );
}

export function PotDisplay(props: { pots: readonly PotVM[]; total: number }): React.JSX.Element {
  return (
    <div aria-label="pots" style={{ margin: '8px 0' }}>
      <strong>Pot: {props.total}</strong>
      {props.pots.length > 1 && (
        <ul style={{ margin: 4 }}>
          {props.pots.map((p, i) => (
            <li key={i}>
              {i === 0 ? 'Main' : `Side ${i}`}: {p.amount} (eligible: {p.eligible.join(', ')})
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export function HandViewer(props: { seat: SeatVM }): React.JSX.Element {
  // Own cards only (custody-bound). Hero sees faces; everyone else sees backs.
  const { seat } = props;
  return (
    <div aria-label={`seat ${seat.seat} cards`} style={{ display: 'flex' }}>
      {seat.isHero
        ? seat.holeCards.map((c) => <CardChip key={c.code} card={c} />)
        : [0, 1].map((i) => <CardBack key={i} />)}
    </div>
  );
}

export function SeatRing(props: { seats: readonly SeatVM[] }): React.JSX.Element {
  return (
    <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
      {props.seats.map((s) => (
        <div
          key={s.seat}
          aria-current={s.isToAct ? 'true' : undefined}
          style={{
            border: s.isToAct ? '2px solid #ffd24d' : '1px solid #555',
            borderRadius: 8,
            padding: 8,
            minWidth: 150,
            opacity: s.folded ? 0.5 : 1,
            background: s.isHero ? '#13301f' : '#1a1a1a',
          }}
        >
          <div style={{ fontWeight: 700 }}>
            Seat {s.seat} {s.isHero ? '(you)' : '(bot)'} {s.isButton ? '🔘' : ''}
          </div>
          <div>Stack: {s.stack}</div>
          <div>In front: {s.committedThisRound}</div>
          {s.folded && <div style={{ color: '#e88' }}>folded</div>}
          {s.allIn && <div style={{ color: '#8ce' }}>all-in</div>}
          <HandViewer seat={s} />
        </div>
      ))}
    </div>
  );
}

export function TimerBanner(props: { timer: TimerVM }): React.JSX.Element {
  // Surfaces the consequence/default text (core §11.4) — never hidden.
  return (
    <Banner tone="info">
      <span aria-live="polite">{props.timer.consequenceText}</span>
    </Banner>
  );
}

export interface ActionBarProps {
  readonly vm: ActionBarVM;
  readonly heroSeat: number;
  readonly betAmount: number;
  readonly onBetAmountChange: (n: number) => void;
  readonly onAction: (choice: 'fold' | 'check' | 'call' | 'bet' | 'raise', amount: number) => void;
}

export function ActionBar(props: ActionBarProps): React.JSX.Element {
  const { vm, betAmount, onBetAmountChange, onAction } = props;
  if (!vm.isHeroTurn) {
    return (
      <div role="group" aria-label="actions" style={{ color: '#999', padding: 8 }}>
        Not your turn — controls disabled.
      </div>
    );
  }
  const legal = vm.legal;
  const sizer = legal.bet ?? legal.raise;
  return (
    <div role="group" aria-label="actions" style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
      {legal.fold && (
        <button type="button" onClick={() => onAction('fold', 0)}>
          Fold
        </button>
      )}
      {legal.check && (
        <button type="button" onClick={() => onAction('check', 0)}>
          Check
        </button>
      )}
      {legal.call && (
        <button type="button" onClick={() => onAction('call', legal.call!.amount)}>
          Call {legal.call.amount}
        </button>
      )}
      {sizer && (
        <span style={{ display: 'inline-flex', gap: 6, alignItems: 'center' }}>
          <label htmlFor="bet-sizer">Amount</label>
          <input
            id="bet-sizer"
            type="number"
            min={sizer.min}
            max={sizer.max}
            value={betAmount}
            onChange={(e) => onBetAmountChange(Number(e.target.value))}
            style={{ width: 90 }}
          />
          <small style={{ color: '#aaa' }}>
            ({sizer.min}–{sizer.max})
          </small>
          {legal.bet && (
            <button type="button" onClick={() => onAction('bet', betAmount)}>
              Bet
            </button>
          )}
          {legal.raise && (
            <button type="button" onClick={() => onAction('raise', betAmount)}>
              Raise to {betAmount}
            </button>
          )}
        </span>
      )}
    </div>
  );
}
