/**
 * Network-selection gate (core REQ-PROD-012; RT-02 F3). The platform runs the SAME model on regtest,
 * testnet, and real (mainnet) BSV — ONE code path; the network is only a configuration tag + which
 * node you connect to, never a branch in the protocol/funding/recovery/settlement (BSV consensus —
 * BIP-143/FORKID sighash, nLockTime finality, script rules — is identical across all three). The
 * platform is research/regtest by default; mainnet (and ONLY mainnet) is reachable behind an explicit,
 * typed acknowledgement because it is the one network with real funds at risk. Every selection
 * surfaces a banner the UI MUST display. This makes the network selection a tested code path, not a
 * convention.
 */

/** Every supported BSV network. `play-regtest`/`regtest`/`testnet` carry no real value; `mainnet`
 *  does. The MODEL is identical for all — only this tag + the node endpoint differ. */
export type Network = 'play-regtest' | 'regtest' | 'testnet' | 'mainnet';

export interface NetworkSelection {
  readonly network: Network;
  /** Human-facing banner the UI must show (REQ-PROD-012). */
  readonly banner: string;
  /** True only when mainnet was explicitly, correctly acknowledged. */
  readonly mainnetEnabled: boolean;
  readonly realFunds: boolean;
}

/** The exact token a caller must pass to enable mainnet — no funds move without it. */
export const MAINNET_ACK_TOKEN = 'I-UNDERSTAND-MAINNET-USES-REAL-FUNDS';

const LOOPBACK = /^(127(?:\.\d{1,3}){3}|::1|localhost)$/;

/**
 * Desktop services (node/relay/indexer) bind to loopback by default (REQ-APP-106). A non-loopback
 * bind exposes the local node to the network and is REFUSED unless explicitly opted in.
 */
export function resolveBindHost(opts?: { host?: string; allowNonLoopback?: boolean }): string {
  const host = opts?.host ?? '127.0.0.1';
  if (!LOOPBACK.test(host) && opts?.allowNonLoopback !== true) {
    throw new Error(`refusing to bind local services to non-loopback host "${host}" without explicit allowNonLoopback (REQ-APP-106)`);
  }
  return host;
}

export function isLoopback(host: string): boolean {
  return LOOPBACK.test(host);
}

export function selectNetwork(opts?: { network?: Network; mainnetAck?: string }): NetworkSelection {
  const requested: Network = opts?.network ?? 'play-regtest';
  if (requested === 'mainnet') {
    if (opts?.mainnetAck !== MAINNET_ACK_TOKEN) {
      throw new Error(
        'mainnet is disabled by default and requires the explicit acknowledgement token ' +
          '(mainnetAck = MAINNET_ACK_TOKEN); refusing — this build is research/regtest only',
      );
    }
    return { network: 'mainnet', banner: '⚠ MAINNET — REAL FUNDS AT RISK (research use only)', mainnetEnabled: true, realFunds: true };
  }
  const banner =
    requested === 'testnet'
      ? '● TESTNET — test coins only, no real value'
      : requested === 'regtest'
        ? '● REGTEST — test coins only, no real value'
        : '● PLAY-MONEY (regtest) — no real value';
  return { network: requested, banner, mainnetEnabled: false, realFunds: false };
}
