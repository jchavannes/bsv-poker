/**
 * Browser-safe game registry (app §A21.2) — variant → module factory for ALL FIVE variants.
 * Lets the web client and the networked clients play any variant generically (the SDK's registry
 * pulls node:crypto via crypto-mentalpoker and can't run in the browser; the game modules
 * themselves are browser-safe — they use only protocol-types' portable sha256).
 */

import type { Card, GameState, Variant } from '@bsv-poker/protocol-types';
import type { GameModule } from '@bsv-poker/engine';
import { createHoldem } from '@bsv-poker/game-holdem';
import { createOmaha } from '@bsv-poker/game-omaha';
import { createStud } from '@bsv-poker/game-stud';
import { createDraw } from '@bsv-poker/game-draw';
import { createRazz } from '@bsv-poker/game-razz';

export type GenericGameModule = GameModule<GameState> & { stateHash: (s: GameState) => string };

const FACTORIES: Record<Variant, (deck: readonly Card[], buttonIndex: number) => GenericGameModule> = {
  // Hold'em rotates the button across hands (§19.E S13); the others take the deck only
  // (stud/razz use bring-in not a button; draw/omaha default to button 0 here).
  holdem: (deck, buttonIndex) => createHoldem({ deck, buttonIndex }) as unknown as GenericGameModule,
  omaha: (deck) => createOmaha({ deck }) as unknown as GenericGameModule,
  stud: (deck) => createStud({ deck }) as unknown as GenericGameModule,
  draw: (deck) => createDraw({ deck }) as unknown as GenericGameModule,
  razz: (deck) => createRazz({ deck }) as unknown as GenericGameModule,
};

export function createGameModule(
  variant: Variant,
  deck: readonly Card[],
  buttonIndex = 0,
): GenericGameModule {
  const f = FACTORIES[variant];
  if (!f) throw new Error(`no module for variant: ${variant}`);
  return f(deck, buttonIndex);
}

export const SUPPORTED_VARIANTS: readonly Variant[] = ['holdem', 'omaha', 'stud', 'draw', 'razz'];

/** Display metadata per variant for the lobby UI. */
export const VARIANT_INFO: Record<
  Variant,
  { readonly label: string; readonly minSeats: number; readonly maxSeats: number; readonly note: string }
> = {
  holdem: { label: "Texas Hold'em", minSeats: 2, maxSeats: 9, note: '2 hole cards, community board' },
  omaha: { label: 'Omaha (PLO / Hi-Lo)', minSeats: 2, maxSeats: 9, note: '4 hole cards, use exactly 2+3' },
  stud: { label: 'Seven-Card Stud', minSeats: 2, maxSeats: 8, note: 'ante + bring-in, up/down cards' },
  draw: { label: 'Five-Card Draw', minSeats: 2, maxSeats: 6, note: 'discard & draw' },
  razz: { label: 'Razz (ace-to-five low)', minSeats: 2, maxSeats: 8, note: 'lowest hand wins' },
};
