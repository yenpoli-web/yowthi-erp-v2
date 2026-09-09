import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export type SalesLifecycleTarget = 'sale' | 'detail';
export type SalesLifecycleAction = 'soft-delete' | 'restore' | 'hard-delete';

export interface SalesLifecycleRequest {
  expectedRowVersion: number;
}

export interface SalesLifecycleResult {
  id: string;
  rowVersion?: number;
  deleted?: boolean;
}

interface CommandOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export function changeSalesLifecycle(
  target: SalesLifecycleTarget,
  id: string,
  action: SalesLifecycleAction,
  request: SalesLifecycleRequest,
  options: CommandOptions,
): Promise<SalesLifecycleResult> {
  const base = target === 'sale'
    ? `/api/v1/sales/${encodeURIComponent(id)}`
    : `/api/v1/sales/details/${encodeURIComponent(id)}`;
  return postApiCommand<SalesLifecycleRequest, SalesLifecycleResult>(
    `${base}/${action}`,
    request,
    options,
  );
}
