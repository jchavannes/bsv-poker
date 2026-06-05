/**
 * Waiting room (§A7) — framework-free (vanilla DOM): after Create/Join we are in
 * `LobbyClient.joinWaitingRoom`; the joined-players list updates live via the app's onPlayers
 * callback. When the room fills, `seated` resolves in the app controller and we advance to the
 * table. Pure render of the ui-core waitingRoomVM.
 */
import { el, type Child } from '@bsv-poker/ui-core/dom';
import { mainnetBanner } from '@bsv-poker/ui-core/components';
import { waitingRoomVM } from '@bsv-poker/ui-core/view-models';

export interface WaitingRoomProps {
  readonly tableName: string;
  readonly capacity: number;
  readonly players: readonly { id: string; pub: string }[];
  readonly myId: string;
  readonly error: string | null;
  readonly onCancel: () => void;
}

export function waitingRoomScreen(p: WaitingRoomProps): HTMLElement {
  const vm = waitingRoomVM(p.players, p.capacity);

  return el('div', { style: { maxWidth: '520px', margin: '40px auto', padding: '16px', display: 'grid', gap: '12px' } },
    mainnetBanner(true),
    el('h1', { style: { margin: '0' } }, p.tableName),
    el('div', { 'aria-live': 'polite', style: { fontSize: '18px' } },
      p.error ? el('span', { style: { color: '#f88' } }, p.error) : vm.statusText),

    el('ul', { style: { listStyle: 'none', padding: '0', display: 'grid', gap: '6px' } },
      ...vm.players.map((pl) =>
        el('li', { style: { border: '1px solid #555', borderRadius: '6px', padding: '6px 10px' } },
          `${pl.id} `,
          pl.id === p.myId ? el('span', { style: { color: '#8f8' } }, '(you)') : false as Child,
        )),
    ),

    el('button', { type: 'button', onClick: p.onCancel, style: { padding: '8px 16px', width: 'fit-content' } },
      'Leave waiting room'),
  );
}
