/**
 * Error taxonomy (REQ-APP-110; codes per Appendix III). Every surfaced error carries a stable code,
 * a category, and a recoverable flag, so the UI can react consistently and logs/metrics can be keyed
 * by code. An unknown code degrades to a non-recoverable internal error (fail-closed).
 */

export type ErrorCategory = 'network' | 'protocol' | 'custody' | 'persistence' | 'user' | 'internal';

export interface AppError {
  readonly code: string;
  readonly category: ErrorCategory;
  readonly message: string;
  readonly recoverable: boolean;
}

const TABLE: Record<string, { category: ErrorCategory; recoverable: boolean }> = {
  NET_DISCONNECTED: { category: 'network', recoverable: true },
  NET_RELAY_UNREACHABLE: { category: 'network', recoverable: true },
  PROTO_INVALID_ENVELOPE: { category: 'protocol', recoverable: false },
  PROTO_OUT_OF_TURN: { category: 'protocol', recoverable: false },
  PROTO_REPLAY: { category: 'protocol', recoverable: false },
  CUSTODY_SIGN_REFUSED: { category: 'custody', recoverable: false },
  PERSIST_CORRUPT_RECORD: { category: 'persistence', recoverable: true },
  USER_INSUFFICIENT_FUNDS: { category: 'user', recoverable: true },
  USER_ACTION_ILLEGAL: { category: 'user', recoverable: true },
  INTERNAL: { category: 'internal', recoverable: false },
};

export const ERROR_CODES: readonly string[] = Object.keys(TABLE);

export function makeError(code: string, message?: string): AppError {
  const entry = TABLE[code] ?? TABLE.INTERNAL!;
  const resolvedCode = TABLE[code] ? code : 'INTERNAL';
  return { code: resolvedCode, category: entry.category, recoverable: entry.recoverable, message: message ?? resolvedCode };
}
