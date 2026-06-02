/**
 * Shared presentational primitives (REQ-APP-052). Pure render of props — NO business logic,
 * no legality computation. Suits carry a letter glyph so no information is colour-only (a11y,
 * §A3.5 / core §5.5.1 no suit precedence).
 */
import React from 'react';
import type { CardVM } from '../view-models/table.ts';

const SUIT_GLYPH: Record<string, string> = { c: '♣', d: '♦', h: '♥', s: '♠' };
const SUIT_RED = new Set(['d', 'h']);

export function CardChip(props: { card: CardVM }): React.JSX.Element {
  const { card } = props;
  const red = SUIT_RED.has(card.suit);
  return (
    <span
      role="img"
      aria-label={`${card.rank} of ${card.suit}`}
      style={{
        display: 'inline-flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        width: 40,
        height: 56,
        borderRadius: 6,
        background: '#fff',
        border: '1px solid #333',
        color: red ? '#c01818' : '#111',
        fontWeight: 700,
        margin: 2,
      }}
    >
      <span style={{ fontSize: 18 }}>{card.rank}</span>
      <span style={{ fontSize: 16 }}>
        {SUIT_GLYPH[card.suit] ?? card.suit}
        {card.suit}
      </span>
    </span>
  );
}

export function CardBack(): React.JSX.Element {
  return (
    <span
      aria-label="concealed card"
      style={{
        display: 'inline-block',
        width: 40,
        height: 56,
        borderRadius: 6,
        background: 'repeating-linear-gradient(45deg,#2b3a67,#2b3a67 4px,#1f2b4d 4px,#1f2b4d 8px)',
        border: '1px solid #333',
        margin: 2,
      }}
    />
  );
}

export function Banner(props: {
  children: React.ReactNode;
  tone?: 'warn' | 'info' | 'error';
}): React.JSX.Element {
  const bg = props.tone === 'error' ? '#7a1f1f' : props.tone === 'info' ? '#1f4d7a' : '#7a5a1f';
  return (
    <div
      role="status"
      style={{
        background: bg,
        color: '#fff',
        padding: '6px 12px',
        borderRadius: 4,
        fontSize: 13,
      }}
    >
      {props.children}
    </div>
  );
}
