/**
 * Table-screen presentational components (REQ-APP-052). All pure render of view-model props.
 * Explicit onClick/onChange handlers, NO <form> submit (REQ-UI-003). No business logic: the
 * ActionBar reads the legal-action descriptor from the engine and never computes legality —
 * the bet/raise slider bounds and quick-button amounts come from the pure bet-sizing view-model
 * which itself only clamps to the engine's legal range.
 *
 * <PokerTable> is the centrepiece: a green felt oval with the pot + community board in the middle
 * and seats positioned around the ellipse (see seatPositions in the table-layout view-model). It
 * is responsive (percentage-positioned) and keyboard/AT accessible (the to-act seat is announced
 * via aria-current + aria-live; cards carry suit names and letters so nothing is colour-only).
 */
import React from 'react';
import { PlayingCard, CardBack, CardChip, Banner, ChipStack } from './primitives.tsx';
import { seatPositions } from '../view-models/table-layout.ts';
import { sizerRange, clampToRange, quickButtons } from '../view-models/bet-sizing.ts';
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
    <div
      aria-label="community cards"
      style={{ display: 'flex', minHeight: 64, justifyContent: 'center', alignItems: 'center', gap: 2 }}
    >
      {props.board.length === 0 ? (
        <span style={{ color: 'rgba(255,255,255,0.55)', fontStyle: 'italic', fontSize: 13 }}>
          (no community cards yet)
        </span>
      ) : (
        props.board.map((c) => <PlayingCard key={c.code} card={c} size="md" />)
      )}
    </div>
  );
}

export function PotDisplay(props: { pots: readonly PotVM[]; total: number }): React.JSX.Element {
  const sidePots = props.pots.length > 1 ? props.pots : [];
  return (
    <div aria-label="pots" style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4 }}>
      <ChipStack amount={props.total} label="Pot" color="#2e7d32" />
      {sidePots.length > 0 && (
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', justifyContent: 'center' }}>
          {sidePots.map((p, i) => (
            <span
              key={i}
              title={`eligible: ${p.eligible.join(', ')}`}
              style={{ fontSize: 11, color: 'rgba(255,255,255,0.8)' }}
            >
              {i === 0 ? 'Main' : `Side ${i}`}: {p.amount}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

/** Cards a seat shows: hero face-up, everyone else face-down backs (custody boundary). */
function SeatCards(props: { seat: SeatVM }): React.JSX.Element {
  const { seat } = props;
  const backs = Math.max(2, seat.holeCards.length || 2);
  return (
    <div aria-label={`seat ${seat.seat} cards`} style={{ display: 'flex', justifyContent: 'center' }}>
      {seat.isHero && seat.holeCards.length > 0
        ? seat.holeCards.map((c) => <PlayingCard key={c.code} card={c} size="sm" />)
        : Array.from({ length: backs }, (_, i) => <CardBack key={i} size="sm" />)}
    </div>
  );
}

/** One seat pod placed on the rail. Shows name, stack, button, to-act ring, state + cards. */
function SeatPod(props: {
  seat: SeatVM;
  label: string;
  xPct: number;
  yPct: number;
}): React.JSX.Element {
  const { seat, label } = props;
  return (
    <div
      aria-current={seat.isToAct ? 'true' : undefined}
      style={{
        position: 'absolute',
        left: `${props.xPct}%`,
        top: `${props.yPct}%`,
        transform: 'translate(-50%, -50%)',
        width: 132,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 4,
      }}
    >
      <SeatCards seat={seat} />
      <div
        style={{
          width: '100%',
          textAlign: 'center',
          borderRadius: 10,
          padding: '5px 6px',
          background: seat.isHero
            ? 'linear-gradient(180deg,#1c4a2e,#13301f)'
            : 'linear-gradient(180deg,#2a2a2e,#191919)',
          border: seat.isToAct ? '2px solid #ffd24d' : '1px solid rgba(255,255,255,0.18)',
          boxShadow: seat.isToAct
            ? '0 0 0 3px rgba(255,210,77,0.35), 0 0 14px rgba(255,210,77,0.5)'
            : '0 2px 6px rgba(0,0,0,0.5)',
          opacity: seat.folded ? 0.45 : 1,
          color: '#fff',
          fontSize: 12,
        }}
      >
        <div style={{ fontWeight: 700, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 4 }}>
          <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: 96 }}>
            {label}
          </span>
          {seat.isButton && (
            <span
              aria-label="dealer button"
              style={{
                display: 'inline-flex',
                width: 16,
                height: 16,
                borderRadius: '50%',
                background: '#fff',
                color: '#111',
                fontSize: 9,
                fontWeight: 800,
                alignItems: 'center',
                justifyContent: 'center',
                border: '1px solid #aaa',
              }}
            >
              D
            </span>
          )}
        </div>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 4, marginTop: 2 }}>
          <span aria-label="chip stack" style={{ color: '#ffd24d', fontWeight: 700 }}>
            {seat.stack}
          </span>
          {seat.folded && <span style={{ color: '#e88' }}>· folded</span>}
          {seat.allIn && <span style={{ color: '#8ce' }}>· all-in</span>}
        </div>
      </div>
      {seat.committedThisRound > 0 && (
        <div style={{ marginTop: 2 }}>
          <ChipStack amount={seat.committedThisRound} color="#2e74c4" />
        </div>
      )}
    </div>
  );
}

/**
 * The realistic card table: a green felt oval on a dark backdrop, seats fanned around the rail,
 * pot + board in the centre. `seatLabel` overrides the per-seat name (opponent ids in networked
 * play). Hero is anchored bottom-centre.
 */
export function PokerTable(props: {
  vm: TableViewModel;
  seatLabel?: (seat: SeatVM) => string;
}): React.JSX.Element {
  const { vm } = props;
  const label = props.seatLabel ?? ((s: SeatVM) => (s.isHero ? 'You' : 'Bot'));
  const order = vm.seats.map((s) => s.seat);
  const positions = seatPositions({ count: vm.seats.length, heroSeat: vm.heroSeat, seatOrder: order });
  const posBySeat = new Map(positions.map((p) => [p.seat, p]));

  return (
    <div
      role="group"
      aria-label="poker table"
      style={{
        position: 'relative',
        width: '100%',
        maxWidth: 820,
        margin: '0 auto',
        aspectRatio: '16 / 10',
        background: 'radial-gradient(ellipse at center, #0d1117 0%, #05070b 100%)',
        borderRadius: 24,
        padding: 8,
      }}
    >
      {/* The felt oval + rail */}
      <div
        style={{
          position: 'absolute',
          inset: '9%',
          borderRadius: '50%',
          background: 'radial-gradient(ellipse at 50% 38%, #2f9e57 0%, #1f7d42 55%, #145c30 100%)',
          border: '14px solid #5b3a1f',
          boxShadow:
            'inset 0 0 40px rgba(0,0,0,0.55), 0 0 0 3px #3a2412, 0 10px 30px rgba(0,0,0,0.6)',
        }}
      >
        {/* Centre: pot above, community board below */}
        <div
          style={{
            position: 'absolute',
            top: '50%',
            left: '50%',
            transform: 'translate(-50%, -50%)',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: 8,
            width: '70%',
          }}
        >
          <PotDisplay pots={vm.pots} total={vm.totalPot} />
          <Board board={vm.board} />
          <div style={{ fontSize: 11, color: 'rgba(255,255,255,0.55)', textTransform: 'uppercase', letterSpacing: 1 }}>
            {vm.phase}
          </div>
        </div>
      </div>

      {/* Seat pods on the rail */}
      {vm.seats.map((s) => {
        const p = posBySeat.get(s.seat);
        if (!p) return null;
        return <SeatPod key={s.seat} seat={s} label={label(s)} xPct={p.xPct} yPct={p.yPct} />;
      })}
    </div>
  );
}

/**
 * Legacy flat seat list — kept for back-compat (and as a responsive fallback). The screens now
 * render <PokerTable>; this stays available so older call sites / tests don't break.
 */
export function SeatRing(props: {
  seats: readonly SeatVM[];
  seatLabel?: (seat: SeatVM) => string;
}): React.JSX.Element {
  const label = props.seatLabel ?? ((s: SeatVM) => (s.isHero ? '(you)' : '(bot)'));
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
            Seat {s.seat} {label(s)} {s.isButton ? '(D)' : ''}
          </div>
          <div>Stack: {s.stack}</div>
          <div>In front: {s.committedThisRound}</div>
          {s.folded && <div style={{ color: '#e88' }}>folded</div>}
          {s.allIn && <div style={{ color: '#8ce' }}>all-in</div>}
          <div style={{ display: 'flex' }}>
            {s.isHero && s.holeCards.length > 0
              ? s.holeCards.map((c) => <CardChip key={c.code} card={c} />)
              : [0, 1].map((i) => <CardBack key={i} size="sm" />)}
          </div>
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
  /** Current total pot, used only to label the pot-relative quick buttons (sizes still clamp to
   * the engine's legal range — legality is NEVER computed in the UI). */
  readonly pot?: number;
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
  const range = sizerRange(legal);
  const toCall = legal.call ? legal.call.amount : 0;
  const quicks = quickButtons({ range, pot: props.pot ?? 0, toCall });

  const btn: React.CSSProperties = {
    padding: '8px 14px',
    fontSize: 14,
    fontWeight: 700,
    borderRadius: 8,
    border: '1px solid rgba(255,255,255,0.2)',
    cursor: 'pointer',
    color: '#fff',
  };

  return (
    <div
      role="group"
      aria-label="actions"
      style={{
        display: 'flex',
        gap: 10,
        alignItems: 'center',
        flexWrap: 'wrap',
        background: 'linear-gradient(180deg,#23262e,#15171c)',
        border: '1px solid rgba(255,255,255,0.12)',
        borderRadius: 12,
        padding: 12,
      }}
    >
      {legal.fold && (
        <button type="button" onClick={() => onAction('fold', 0)} style={{ ...btn, background: '#7a2222' }}>
          Fold
        </button>
      )}
      {legal.check && (
        <button type="button" onClick={() => onAction('check', 0)} style={{ ...btn, background: '#2e6b3e' }}>
          Check
        </button>
      )}
      {legal.call && (
        <button
          type="button"
          onClick={() => onAction('call', legal.call!.amount)}
          style={{ ...btn, background: '#2e6b3e' }}
        >
          Call {legal.call.amount}
        </button>
      )}

      {range.available && (
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 6,
            background: 'rgba(0,0,0,0.25)',
            borderRadius: 10,
            padding: 8,
          }}
        >
          <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
            {quicks.map((q) => (
              <button
                key={q.key}
                type="button"
                onClick={() => onBetAmountChange(q.amount)}
                style={{ ...btn, background: '#34495e', padding: '4px 8px', fontSize: 12 }}
              >
                {q.label}
              </button>
            ))}
          </div>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <label htmlFor="bet-slider" style={{ color: '#bbb', fontSize: 12 }}>
              Size
            </label>
            <input
              id="bet-slider"
              type="range"
              min={range.min}
              max={range.max}
              value={clampToRange(betAmount, range)}
              onChange={(e) => onBetAmountChange(clampToRange(Number(e.target.value), range))}
              aria-label="bet size slider"
              style={{ flex: 1, minWidth: 120 }}
            />
            <input
              id="bet-sizer"
              type="number"
              min={range.min}
              max={range.max}
              value={betAmount}
              onChange={(e) => onBetAmountChange(clampToRange(Number(e.target.value), range))}
              aria-label="bet size"
              style={{ width: 84 }}
            />
            <small style={{ color: '#aaa' }}>
              ({range.min}–{range.max})
            </small>
          </div>
          <div>
            {legal.bet && (
              <button
                type="button"
                onClick={() => onAction('bet', clampToRange(betAmount, range))}
                style={{ ...btn, background: '#b5701b', width: '100%' }}
              >
                Bet {clampToRange(betAmount, range)}
              </button>
            )}
            {legal.raise && (
              <button
                type="button"
                onClick={() => onAction('raise', clampToRange(betAmount, range))}
                style={{ ...btn, background: '#b5701b', width: '100%' }}
              >
                Raise to {clampToRange(betAmount, range)}
              </button>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

/** Keep HandViewer export (used to render a single seat's cards) for back-compat. */
export function HandViewer(props: { seat: SeatVM }): React.JSX.Element {
  return <SeatCards seat={props.seat} />;
}
