/**
 * Signing-prompt view-model (REQ-UI-006 / §A6.7; core §11.6).
 *
 * No silent signing: every emitted action must state exactly what is being authorised —
 * the action kind, amounts, and the affected pot/state. In Phase-1 hot-seat play there is no
 * real key/tx (the on-chain crypto + tx-builder is a later phase, §A2.3); this view-model
 * still produces the honest human-readable intent so the modal is wired end-to-end and the
 * "no silent signing" contract is exercised. The exact-bytes field is intentionally absent
 * here and labelled as such.
 */

import type { Action, LegalActions } from '@bsv-poker/protocol-types';

export interface SigningPromptVM {
  readonly title: string;
  /** One human-readable line per fact being authorised (action, amount, pot effect). */
  readonly lines: readonly string[];
  /** The concrete action that will be applied on confirm. */
  readonly action: Action;
  /** Honest note about what is and is NOT signed in this phase. */
  readonly disclosure: string;
}

export function signingPromptVM(
  action: Action,
  ctx: { readonly potBefore: number; readonly toCall: number },
): SigningPromptVM {
  const lines: string[] = [];
  switch (action.kind) {
    case 'fold':
      lines.push('Action: FOLD — you forfeit the hand.');
      lines.push('Your cards are NOT revealed (fold without reveal).');
      break;
    case 'check':
      lines.push('Action: CHECK — wager nothing, pass action.');
      break;
    case 'call':
      lines.push(`Action: CALL — add ${action.amount} to match the bet.`);
      lines.push(`Amount to call: ${ctx.toCall}.`);
      break;
    case 'bet':
      lines.push(`Action: BET ${action.amount}.`);
      break;
    case 'raise':
      lines.push(`Action: RAISE to ${action.amount} (total this round).`);
      break;
    default:
      lines.push(`Action: ${action.kind.toUpperCase()} ${action.amount}.`);
  }
  lines.push(`Pot before your action: ${ctx.potBefore}.`);
  return {
    title: 'Confirm your action',
    lines,
    action,
    disclosure:
      'REGTEST / play-money. No key is used and no transaction is broadcast in this ' +
      'phase — the on-chain signing path (SDK custody + tx-builder) is wired in a later ' +
      'phase (§A2.3). Confirming applies the move to the local engine only.',
  };
}

/** Build the concrete Action a chosen UI control maps to, from the engine legal descriptor. */
export function actionFromChoice(
  choice: 'fold' | 'check' | 'call' | 'bet' | 'raise',
  seat: number,
  legal: LegalActions,
  amount: number,
): Action {
  switch (choice) {
    case 'fold':
      return { kind: 'fold', seat, amount: 0 };
    case 'check':
      return { kind: 'check', seat, amount: 0 };
    case 'call':
      return { kind: 'call', seat, amount: legal.call ? legal.call.amount : 0 };
    case 'bet':
      return { kind: 'bet', seat, amount };
    case 'raise':
      return { kind: 'raise', seat, amount };
    default:
      return { kind: 'check', seat, amount: 0 };
  }
}
