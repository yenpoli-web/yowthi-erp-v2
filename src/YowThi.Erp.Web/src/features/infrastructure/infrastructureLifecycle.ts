import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';
import { prepareDeletionReauthentication } from '../../app/security/authSession';
import type { InfrastructureLifecycleKind } from './infrastructureLifecycleOptions';

export { ApiProblemError } from '../../app/api/apiTransport';

export type InfrastructureLifecycleAction = 'soft-delete' | 'restore';

export interface InfrastructureLifecycleRequest {
  expectedRowVersion: number;
}

export interface InfrastructureLifecycleResult {
  rowVersion: number;
  deleted: boolean;
}

interface CommandOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export async function changeInfrastructureLifecycle(
  kind: InfrastructureLifecycleKind,
  id: string,
  action: InfrastructureLifecycleAction,
  request: InfrastructureLifecycleRequest,
  options: CommandOptions,
): Promise<InfrastructureLifecycleResult> {
  if (action === 'soft-delete') await prepareDeletionReauthentication();
  const url = `/api/v1/infrastructure/${kind}/${id}/${action}`;
  return postApiCommand<InfrastructureLifecycleRequest, InfrastructureLifecycleResult>(url, request, options);
}
