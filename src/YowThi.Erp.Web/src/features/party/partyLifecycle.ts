import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';
import type { PartyLifecycleKind } from './partyLifecycleOptions';

export { ApiProblemError } from '../../app/api/apiTransport';

export type PartyLifecycleAction = 'soft-delete' | 'restore';

export interface PartyLifecycleRequest {
  expectedRowVersion: number;
}

export interface PartyLifecycleCommandResult {
  rowVersion: number;
  deleted: boolean;
}

interface PartyLifecycleCommandOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export function changePartyLifecycle(
  kind: PartyLifecycleKind,
  id: string,
  action: PartyLifecycleAction,
  request: PartyLifecycleRequest,
  options: PartyLifecycleCommandOptions,
): Promise<PartyLifecycleCommandResult> {
  return postApiCommand<PartyLifecycleRequest, PartyLifecycleCommandResult>(
    `/api/v1/party/${kind}/${id}/${action}`,
    request,
    options,
  );
}
