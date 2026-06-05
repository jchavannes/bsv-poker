/**
 * Network lobby (§A6.3/§A6.4) — framework-free (vanilla DOM): lists open tables from the relay
 * (polled by the app controller + a manual Refresh), lets a player Join one, exposes a Create-table
 * form with a VARIANT PICKER (any of the five variants, seat count within the variant's range, and
 * the Omaha hi-lo toggle), and an always-visible WALLET panel (balance / add / withdraw / history).
 * Pure render + explicit handlers (no <form> submit, REQ-UI-003); validation/meta come from the
 * ui-core network-lobby view-model — this screen never recomputes rules. Variant labels/seat ranges
 * come from app-services VARIANT_INFO / SUPPORTED_VARIANTS.
 */
import { el, type Child } from '@bsv-poker/ui-core/dom';
import {
  VARIANT_INFO,
  SUPPORTED_VARIANTS,
  type OpenTable,
  type WalletState,
} from '@bsv-poker/app-services';
import {
  validateNetworkTable,
  VARIANT_SEAT_RANGE,
  type NetworkTableForm,
  type VariantId,
} from '@bsv-poker/ui-core/view-models';
import { mainnetBanner, walletPanel, variantPicker, type VariantOption } from '@bsv-poker/ui-core/components';

const VARIANT_OPTIONS: readonly VariantOption[] = SUPPORTED_VARIANTS.map((v) => ({
  id: v as VariantId,
  label: VARIANT_INFO[v].label,
  minSeats: VARIANT_INFO[v].minSeats,
  maxSeats: VARIANT_INFO[v].maxSeats,
  note: VARIANT_INFO[v].note,
}));

const numInput = (e: Event): number => Number((e.target as HTMLInputElement).value);
const strInput = (e: Event): string => (e.target as HTMLInputElement).value;

export interface LobbyProps {
  readonly relay: string;
  readonly identityId: string;
  readonly walletState: WalletState;
  readonly createError: string | null;

  readonly tables: readonly OpenTable[];
  readonly loadError: string | null;
  readonly onRefresh: () => void;

  // Create-table form (controlled fields held in the app model).
  readonly form: NetworkTableForm;
  readonly onFieldChange: <K extends keyof NetworkTableForm>(key: K, value: NetworkTableForm[K]) => void;

  // Wallet form (controlled fields held in the app model).
  readonly addAmount: number;
  readonly onAddAmountChange: (n: number) => void;
  readonly onAddFunds: (amount: number) => void;
  readonly withdrawAmount: number;
  readonly onWithdrawAmountChange: (n: number) => void;
  readonly withdrawDest: string;
  readonly onWithdrawDestChange: (s: string) => void;
  readonly onWithdraw: (amount: number, dest: string) => void;

  readonly onCreate: (form: NetworkTableForm) => void;
  readonly onJoin: (table: OpenTable) => void;
  readonly onDisconnect: () => void;
}

export function networkLobbyScreen(p: LobbyProps): HTMLElement {
  const validation = validateNetworkTable(p.form);
  const seatRange = VARIANT_SEAT_RANGE[p.form.variant];

  const tablesSection = el('section', { style: { border: '1px solid #444', borderRadius: '8px', padding: '12px' } },
    el('div', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center' } },
      el('h2', { style: { margin: '0', fontSize: '18px' } }, 'Open tables'),
      el('button', { type: 'button', onClick: p.onRefresh }, 'Refresh')),
    p.loadError ? el('div', { style: { color: '#f88', fontSize: '13px' } }, `Failed to load tables: ${p.loadError}`) : false as Child,
    p.tables.length === 0
      ? el('p', { style: { color: '#999' } }, 'No open tables yet — create one below.')
      : el('ul', { style: { listStyle: 'none', padding: '0', display: 'grid', gap: '8px' } },
          ...p.tables.map((t) =>
            el('li', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', border: '1px solid #555', borderRadius: '6px', padding: '8px' } },
              el('span', {},
                el('strong', {}, t.meta.name), ' ',
                el('span', { style: { color: '#aaa' } },
                  `— ${VARIANT_INFO[t.meta.variant]?.label ?? t.meta.variant}` +
                  `${(t.meta as { hiLo?: boolean }).hiLo ? ' Hi-Lo' : ''} · blinds ${t.meta.smallBlind}/${t.meta.bigBlind}, ` +
                  `stack ${t.meta.startingStack}, ${t.meta.maxSeats} seats · ${t.members} present`)),
              el('button', { type: 'button', onClick: () => p.onJoin(t) }, 'Join'),
            )),
        ),
  );

  const createSection = el('section', {
    role: 'group', 'aria-label': 'create table',
    style: { border: '1px solid #444', borderRadius: '8px', padding: '12px', display: 'grid', gap: '10px' },
  },
    el('h2', { style: { margin: '0', fontSize: '18px' } }, 'Create a table'),
    el('label', {}, 'Name ',
      el('input', { type: 'text', 'aria-label': 'table name', value: p.form.name, onInput: (e: Event) => p.onFieldChange('name', strInput(e)), style: { width: '240px' } })),

    variantPicker({
      options: VARIANT_OPTIONS,
      value: p.form.variant,
      onChange: (v) => p.onFieldChange('variant', v),
      hiLo: Boolean(p.form.hiLo),
      onHiLoChange: (b) => p.onFieldChange('hiLo', b),
    }),

    el('label', {}, 'Small blind ',
      el('input', { type: 'number', min: '1', 'aria-label': 'small blind', value: p.form.smallBlind, onInput: (e: Event) => p.onFieldChange('smallBlind', numInput(e)) })),
    el('label', {}, 'Big blind ',
      el('input', { type: 'number', min: '2', 'aria-label': 'big blind', value: p.form.bigBlind, onInput: (e: Event) => p.onFieldChange('bigBlind', numInput(e)) })),
    el('label', {}, 'Starting stack ',
      el('input', { type: 'number', min: '4', 'aria-label': 'starting stack', value: p.form.startingStack, onInput: (e: Event) => p.onFieldChange('startingStack', numInput(e)) })),
    el('label', {}, `Seats (${seatRange.minSeats}–${seatRange.maxSeats}) `,
      el('input', { type: 'number', min: String(seatRange.minSeats), max: String(seatRange.maxSeats), 'aria-label': 'seats', value: p.form.maxSeats, onInput: (e: Event) => p.onFieldChange('maxSeats', numInput(e)) })),

    !validation.ok
      ? el('ul', { style: { color: '#f88', margin: '0' } }, ...validation.errors.map((e) => el('li', {}, e)))
      : false as Child,
    el('p', { style: { color: '#888', fontSize: '12px', margin: '0' } },
      `Joining buys in for the starting stack from your wallet (${p.walletState.balance} chips available).`),
    el('button', {
      type: 'button', disabled: !validation.ok, onClick: () => p.onCreate(p.form),
      style: { padding: '8px 16px', fontSize: '16px' },
    }, 'Create & open waiting room'),
  );

  return el('div', { style: { maxWidth: '880px', margin: '24px auto', padding: '16px', display: 'grid', gap: '16px' } },
    mainnetBanner(true),

    el('div', { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '12px' } },
      el('h1', { style: { margin: '0' } }, 'Lobby'),
      el('div', { style: { display: 'flex', gap: '8px', alignItems: 'center' } },
        el('span', { style: { color: '#888', fontSize: '13px' } }, p.relay, ' · ', el('code', {}, p.identityId)),
        el('button', { type: 'button', onClick: p.onDisconnect }, 'Disconnect'),
      )),

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
    }),

    p.createError ? el('div', { role: 'alert', style: { color: '#f88', fontSize: '13px' } }, p.createError) : false as Child,

    tablesSection,
    createSection,

    el('section', { style: { border: '1px solid #444', borderRadius: '8px', padding: '12px' } },
      el('h2', { style: { margin: '0', fontSize: '18px' } }, 'Bots'),
      el('p', { style: { color: '#999', marginTop: '6px' } },
        'Bots are separate remote players — run one in its own window and it joins your table over the relay like any opponent: ',
        el('code', {}, `node tools/bot-daemon.ts --relay ${p.relay} --gui 9100`)),
    ),
  );
}
