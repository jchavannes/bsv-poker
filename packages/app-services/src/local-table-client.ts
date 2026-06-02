/**
 * LocalTableClient — the client-side app-services seam (§A2.3) for Phase-1 hot-seat-vs-bot play.
 *
 * It owns ONE game-holdem module instance + the current HoldemState and exposes a small,
 * UI-facing surface: getState / legalActions / timeout / apply / startHand. It uses the real
 * engine for ALL game logic (betting/pots/FSM/settlement) — no rules are reimplemented here.
 *
 * Seams that are STUBBED for this phase (clearly, per §A2.3 — the relay/indexer/custody wiring
 * is a later phase):
 *   - connection/sync client: there is no peer; the single client is authoritative locally.
 *   - custody/signing client: no key, no transaction; "signing" is a confirm step in the UI.
 *   - shuffle: single-party CSPRNG Fisher–Yates (see shuffle.ts), NOT mental-poker.
 *
 * The human is the hero seat; the other seat is driven by the trivial bot policy. After the
 * hero's move is applied, the client auto-plays the bot whenever the bot is on the clock, so a
 * single human can drive a full hand to showdown + settlement.
 */

import type { Action, Card, LegalActions, Ruleset } from '@bsv-poker/protocol-types';
import type { TimeoutResolution, SeatInit } from '@bsv-poker/engine';
import { createHoldem, type HoldemModule, type HoldemState } from '@bsv-poker/game-holdem';
import { shuffleDeck } from './shuffle.ts';
import { botAction } from './bot.ts';

export interface LocalTableConfig {
  readonly ruleset: Ruleset;
  /** Which seat the human plays (0 or 1 for heads-up). */
  readonly heroSeat: number;
  /** Optional deck injector (tests pass a fixed deck); defaults to a CSPRNG shuffle. */
  readonly makeDeck?: () => Card[];
  /** Optional button index passed to createHoldem (rotation across hands). */
  readonly buttonIndex?: number;
}

export class LocalTableClient {
  private readonly ruleset: Ruleset;
  private readonly heroSeat: number;
  private readonly seatInits: SeatInit[];
  private readonly makeDeck: () => Card[];
  private buttonIndex: number;

  private module: HoldemModule;
  private state: HoldemState;
  private startingStacks: Map<number, number>;

  constructor(config: LocalTableConfig) {
    this.ruleset = config.ruleset;
    this.heroSeat = config.heroSeat;
    this.makeDeck = config.makeDeck ?? shuffleDeck;
    this.buttonIndex = config.buttonIndex ?? 0;
    this.seatInits = [
      { seat: 0, stack: config.ruleset.minBuyIn },
      { seat: 1, stack: config.ruleset.minBuyIn },
    ];
    this.module = createHoldem({ deck: this.makeDeck(), buttonIndex: this.buttonIndex });
    this.state = this.module.init(this.ruleset, this.seatInits);
    this.startingStacks = new Map(this.seatInits.map((s) => [s.seat, s.stack]));
    // If the bot is first to act (e.g. hero is not the button preflop), let it move.
    this.runBots();
  }

  getHeroSeat(): number {
    return this.heroSeat;
  }

  getBotSeat(): number {
    return this.heroSeat === 0 ? 1 : 0;
  }

  getState(): HoldemState {
    return this.state;
  }

  /** Engine-known hole cards for a seat (custody-bound; only ever shown for the hero in the UI). */
  getHole(seat: number): readonly Card[] {
    return this.state.hole[seat] ?? [];
  }

  getStartingStacks(): ReadonlyMap<number, number> {
    return this.startingStacks;
  }

  /** Legal actions for a seat — straight from the engine (the UI never computes legality). */
  legalActions(seat: number): LegalActions {
    return this.module.getLegalActions(this.state, seat);
  }

  /** Timeout resolution for the seat on the clock (consequence text source, core §6.4). */
  timeout(): TimeoutResolution | null {
    // `now` is unused by the holdem module's eligibility (it reports the safe default); pass 0.
    return this.module.isTimeoutEligible(this.state, 0);
  }

  isHeroTurn(): boolean {
    return !this.state.handComplete && this.state.betting.toAct === this.heroSeat;
  }

  /** Apply the hero's action, then auto-play the bot while it is on the clock. */
  apply(action: Action): HoldemState {
    if (action.seat !== this.heroSeat) {
      throw new Error('LocalTableClient.apply only accepts the hero seat; bots are automatic');
    }
    this.state = this.module.apply(this.state, action);
    this.runBots();
    return this.state;
  }

  /** Drive the bot for as long as it is the seat on the clock and the hand is live. */
  private runBots(): void {
    const botSeat = this.getBotSeat();
    while (!this.state.handComplete && this.state.betting.toAct === botSeat) {
      const legal = this.module.getLegalActions(this.state, botSeat);
      this.state = this.module.apply(this.state, botAction(botSeat, legal));
    }
  }

  /** Start a fresh hand (rotating the button), reshuffling the deck. */
  startHand(): HoldemState {
    // Carry forward current stacks as the next hand's buy-ins (so chips persist across hands).
    const stacks = new Map(this.state.seats.map((s) => [s.seat, s.stack]));
    const seatInits: SeatInit[] = this.seatInits.map((s) => ({
      seat: s.seat,
      stack: stacks.get(s.seat) ?? s.stack,
    }));
    this.buttonIndex = (this.buttonIndex + 1) % seatInits.length;
    this.module = createHoldem({ deck: this.makeDeck(), buttonIndex: this.buttonIndex });
    this.state = this.module.init(this.ruleset, seatInits);
    this.startingStacks = new Map(seatInits.map((s) => [s.seat, s.stack]));
    this.runBots();
    return this.state;
  }

  /** State hash (replay/branch-binding; exposed for debugging the transcript). */
  stateHash(): string {
    return this.module.stateHash(this.state);
  }
}
