/**
 * Lobby table-setup choice (human-rules-the-game: core D4, REQ-PROD-002). EVERY table is set up by
 * an explicit HUMAN choice in the lobby — variant, seat count, and opponents. The human MAY choose to
 * play against bots (a valid lobby selection) or against other humans (a waiting room); the device
 * NEVER decides this, and there is no default that starts a game without the human choosing. The
 * human always occupies their own seat — a bot can never take the human's seat or act for them.
 */

export type OpponentMode = 'humans' | 'bots';

export interface TableSetupForm {
  readonly variant: string;
  readonly seats: number; // total seats at the table, INCLUDING the human
  /** The human's explicit opponent choice. `null` = not chosen yet → invalid (must be human-chosen). */
  readonly opponents: OpponentMode | null;
  /** Number of bot opponents (only when opponents === 'bots'). */
  readonly botCount?: number;
}

export interface SeatRange {
  readonly min: number;
  readonly max: number;
}

export interface TableSetupValidation {
  readonly ok: boolean;
  readonly errors: readonly string[];
}

export function validateTableSetup(form: TableSetupForm, range: SeatRange): TableSetupValidation {
  const errors: string[] = [];
  if (form.opponents === null) errors.push('Choose your opponents: other humans, or bots — the game does not start until you decide.');
  if (!(form.seats >= range.min && form.seats <= range.max)) errors.push(`Seats must be between ${range.min} and ${range.max}.`);
  if (form.opponents === 'bots') {
    const max = form.seats - 1; // the human always holds at least one seat
    if (!(typeof form.botCount === 'number' && form.botCount >= 1 && form.botCount <= max)) {
      errors.push(`Choose 1 to ${max} bot opponents (you keep your own seat).`);
    }
  }
  return { ok: errors.length === 0, errors };
}

export interface SeatPlan {
  readonly seat: number;
  readonly isHuman: boolean;
  readonly stack: number;
}

/**
 * Build the seat plan for a human-chosen practice table: seat 0 is the HUMAN (hero), the next
 * `botCount` seats are bots. The human is never a bot and the device never holds the hero seat.
 */
export function buildPracticeSeats(botCount: number, stack: number): SeatPlan[] {
  if (botCount < 1) throw new Error('a practice table needs at least one bot opponent (the human chose bots)');
  const plan: SeatPlan[] = [{ seat: 0, isHuman: true, stack }];
  for (let s = 1; s <= botCount; s++) plan.push({ seat: s, isHuman: false, stack });
  return plan;
}
