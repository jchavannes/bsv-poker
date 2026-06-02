/**
 * Trivial opponent policy for hot-seat-vs-bot local play. Reads the engine's legal-action
 * descriptor and picks a safe, never-aggressive move: check if possible, else call, else fold.
 * This is a placeholder local policy — NOT an AI and NOT a networked opponent. Multi-client
 * play over the relay is a later phase (§A2.3).
 */

import type { Action, LegalActions } from '@bsv-poker/protocol-types';

export function botAction(seat: number, legal: LegalActions): Action {
  if (legal.check) return { kind: 'check', seat, amount: 0 };
  if (legal.call) return { kind: 'call', seat, amount: legal.call.amount };
  return { kind: 'fold', seat, amount: 0 };
}
