/**
 * Networked table (§A6.5/§A7) — framework-free (vanilla DOM). REAL multiplayer play over the relay.
 * This module is a PURE render of the table render-model the app controller projects from the
 * InteractiveNetworkedTableClient's onUpdate stream: it renders seats/board/pot/timer via ui-core,
 * and on the hero's turn raises the signing modal (no silent signing, §A6.7) before the controller
 * calls client.submitAction(). After settlement it shows the showdown + settlement. No game logic
 * here — legality is read from the engine via the controller's legal-action descriptor (REQ-APP-052).
 */
import { el, type Child } from '@bsv-poker/ui-core/dom';
import {
  mainnetBanner,
  pokerTable,
  actionBar,
  timerBanner,
  signingModal,
  showdownPanel,
  settlementSummary,
  walletPanel,
} from '@bsv-poker/ui-core/components';
import type {
  TableViewModel,
  SeatVM,
  ShowdownViewModel,
  SettlementViewModel,
  SigningPromptVM,
} from '@bsv-poker/ui-core/view-models';
import type { WalletState } from '@bsv-poker/app-services';

export interface TableProps {
  readonly tableName: string;
  readonly smallBlind: number;
  readonly bigBlind: number;
  readonly regtest: boolean;
  readonly phase: string | null;

  readonly vm: TableViewModel | null;
  readonly status: string;
  readonly error: string | null;
  readonly yourTurn: boolean;
  readonly handComplete: boolean;
  readonly showdown: ShowdownViewModel | null;
  readonly settlement: SettlementViewModel | null;
  readonly seatLabel: (seat: SeatVM) => string;

  readonly betAmount: number;
  readonly onBetAmountChange: (n: number) => void;
  readonly onAction: (choice: 'fold' | 'check' | 'call' | 'bet' | 'raise', amount: number) => void;

  readonly prompt: SigningPromptVM | null;
  readonly onConfirm: () => void;
  readonly onCancel: () => void;

  readonly walletState: WalletState;
  readonly addAmount: number;
  readonly onAddAmountChange: (n: number) => void;
  readonly onAddFunds: (amount: number) => void;
  readonly withdrawAmount: number;
  readonly onWithdrawAmountChange: (n: number) => void;
  readonly withdrawDest: string;
  readonly onWithdrawDestChange: (s: string) => void;
  readonly onWithdraw: (amount: number, dest: string) => void;

  readonly heroStack: number;
  readonly onLeave: (heroStack: number) => void;
}

export function networkTableScreen(p: TableProps): HTMLElement {
  const vm = p.vm;

  const playArea: Child = vm
    ? el('div', { style: { display: 'grid', gap: '12px' } },
        pokerTable(vm, p.seatLabel),
        timerBanner(vm.timer),
        !p.handComplete
          ? (p.yourTurn
              ? actionBar({
                  vm: vm.actionBar,
                  heroSeat: vm.heroSeat,
                  betAmount: p.betAmount,
                  onBetAmountChange: p.onBetAmountChange,
                  onAction: p.onAction,
                  pot: vm.totalPot,
                })
              : el('div', { role: 'group', 'aria-label': 'actions', style: { color: '#999', padding: '8px' } },
                  'Waiting for the other player(s)…'))
          : el('div', { style: { display: 'grid', gap: '8px' } },
              p.showdown ? showdownPanel(p.showdown) : false as Child,
              p.settlement ? settlementSummary(p.settlement) : false as Child,
              el('p', { style: { color: '#aaa', fontSize: '13px' } },
                'Hand complete. Return to the lobby to play another, or stay seated for the next hand.'),
              el('button', { type: 'button', onClick: () => p.onLeave(p.heroStack), style: { padding: '8px 16px', fontSize: '16px' } },
                'Cash out & back to lobby'),
          ),
      )
    : false as Child;

  const modal = signingModal(p.prompt, p.onConfirm, p.onCancel);

  return el('div', { style: { maxWidth: '860px', margin: '20px auto', padding: '16px', display: 'grid', gap: '12px' } },
    mainnetBanner(p.regtest),

    el('div', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center' } },
      el('h2', { style: { margin: '0' } },
        `${p.tableName} — blinds ${p.smallBlind}/${p.bigBlind}${p.phase ? ` (phase ${p.phase})` : ''}`),
      el('button', { type: 'button', onClick: () => p.onLeave(p.heroStack) }, 'Cash out & leave')),

    walletPanel({
      snapshot: p.walletState,
      addAmount: p.addAmount,
      onAddAmountChange: p.onAddAmountChange,
      onAddFunds: p.onAddFunds,
      withdrawAmount: p.withdrawAmount,
      onWithdrawAmountChange: p.onWithdrawAmountChange,
      withdrawDest: p.withdrawDest,
      onWithdrawDestChange: p.onWithdrawDestChange,
      onWithdraw: p.onWithdraw,
      compact: true,
    }),

    p.error ? el('div', { role: 'alert', style: { color: '#f88' } }, `Table error: ${p.error}`) : false as Child,
    (p.status && !p.error) ? el('div', { style: { color: '#aaa' } }, p.status) : false as Child,

    playArea,
    modal ?? (false as Child),
  );
}
