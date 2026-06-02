/**
 * Wallet-panel view-model (REQ-APP-051; core §9 / §A6.2) — PURE projection of a wallet snapshot
 * into render props, plus pure validation for the add/withdraw/buy-in flows.
 *
 * ui-core must NOT import app-services, so the wallet shape is mirrored structurally here
 * (matches app-services WalletService.state()). No React / no I/O — strip-friendly for
 * `node --test`.
 */

/** Structural mirror of app-services FundsEventKind. */
export type WalletEventKind = 'deposit' | 'withdraw' | 'buy-in' | 'cash-out';

export interface WalletEventVM {
  readonly kind: WalletEventKind;
  readonly amount: number;
  readonly balanceAfter: number;
  readonly at: number;
  readonly memo?: string;
}

/** Structural mirror of app-services WalletState. */
export interface WalletSnapshot {
  readonly network: string;
  readonly balance: number;
  readonly history: readonly WalletEventVM[];
}

export interface WalletRow {
  readonly kind: WalletEventKind;
  /** Signed-for-display label, e.g. "+100" deposit, "-40" buy-in. */
  readonly signedAmount: string;
  readonly balanceAfter: number;
  readonly memo: string;
  /** True for inflows (deposit / cash-out) — render green; outflows render red. */
  readonly inflow: boolean;
}

export interface WalletPanelVM {
  readonly network: string;
  readonly balance: number;
  /** Whether this is play-money (drives the banner). */
  readonly playMoney: boolean;
  /** Most-recent-first history rows (capped for display). */
  readonly rows: readonly WalletRow[];
}

const INFLOW: ReadonlySet<WalletEventKind> = new Set<WalletEventKind>(['deposit', 'cash-out']);

/** Project a wallet snapshot into panel render props (newest history first, capped to `limit`). */
export function walletPanelVM(snap: WalletSnapshot, limit = 8): WalletPanelVM {
  const rows: WalletRow[] = snap.history
    .slice()
    .reverse()
    .slice(0, limit)
    .map((e) => {
      const inflow = INFLOW.has(e.kind);
      return {
        kind: e.kind,
        signedAmount: `${inflow ? '+' : '-'}${e.amount}`,
        balanceAfter: e.balanceAfter,
        memo: e.memo ?? '',
        inflow,
      };
    });
  return {
    network: snap.network,
    balance: snap.balance,
    playMoney: snap.network === 'play-regtest',
    rows,
  };
}

export interface AmountValidation {
  readonly ok: boolean;
  readonly error: string | null;
}

/** Validate a positive integer amount (satoshis / play chips; INV-BS-1 no fractions). */
export function validateAmount(amount: number): AmountValidation {
  if (!Number.isFinite(amount) || !Number.isInteger(amount)) {
    return { ok: false, error: 'Enter a whole number of chips.' };
  }
  if (amount <= 0) return { ok: false, error: 'Amount must be greater than zero.' };
  return { ok: true, error: null };
}

/** Validate a withdrawal: positive int AND within the available balance. */
export function validateWithdraw(amount: number, balance: number, dest: string): AmountValidation {
  const a = validateAmount(amount);
  if (!a.ok) return a;
  if (dest.trim().length === 0) return { ok: false, error: 'Enter a destination address.' };
  if (amount > balance) return { ok: false, error: `Insufficient balance (have ${balance}).` };
  return { ok: true, error: null };
}

export interface BuyInCheck {
  /** Whether the player can afford the table's required buy-in. */
  readonly canAfford: boolean;
  /** The required buy-in (the table's starting stack). */
  readonly required: number;
  /** Clear message when blocked (empty when affordable). */
  readonly message: string;
}

/** Can the player buy in for `required` from `balance`? Blocks join with a clear message. */
export function buyInCheck(balance: number, required: number): BuyInCheck {
  const canAfford = balance >= required && required > 0;
  return {
    canAfford,
    required,
    message: canAfford
      ? ''
      : `You need ${required} chips to buy in but have ${balance}. Add funds first.`,
  };
}
