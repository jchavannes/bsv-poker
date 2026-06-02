/**
 * Lobby / table-create view-model (§A6.3, §A6.4) — pure validation + ruleset assembly.
 * Builds a regtest play-money NL Hold'em Ruleset from the create-table form inputs.
 */

import type { Ruleset } from '@bsv-poker/protocol-types';

export interface TableCreateForm {
  readonly smallBlind: number;
  readonly bigBlind: number;
  readonly startingStack: number;
  readonly decisionMs: number;
}

export interface TableCreateValidation {
  readonly ok: boolean;
  readonly errors: readonly string[];
}

export function validateTableCreate(form: TableCreateForm): TableCreateValidation {
  const errors: string[] = [];
  if (!(form.smallBlind > 0)) errors.push('Small blind must be positive.');
  if (!(form.bigBlind > form.smallBlind)) {
    errors.push('Big blind must exceed the small blind.');
  }
  if (!(form.startingStack >= form.bigBlind * 2)) {
    errors.push('Starting stack must be at least two big blinds.');
  }
  if (!(form.decisionMs >= 1000)) errors.push('Decision time must be at least 1s.');
  return { ok: errors.length === 0, errors };
}

/** Assemble the heads-up NL regtest ruleset (D1 Phase-1 reference). */
export function rulesetFromForm(form: TableCreateForm): Ruleset {
  return {
    variant: 'holdem',
    bettingStructure: 'NL',
    forcedBetModel: 'blinds',
    seats: 2,
    blinds: { smallBlind: form.smallBlind, bigBlind: form.bigBlind, ante: 0, bringIn: 0 },
    minBuyIn: form.startingStack,
    maxBuyIn: form.startingStack,
    timeouts: { decisionMs: form.decisionMs, recoveryMs: form.decisionMs * 4 },
    signingMode: 'A',
    currency: 'play-regtest',
    suitTiebreakHouseRule: false,
    hiLo: false,
  };
}
