import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export type OutsourcedLifecycleTarget = 'batch' | 'detail';
export type OutsourcedLifecycleAction = 'soft-delete' | 'restore' | 'hard-delete';

export interface OutsourcedLifecycleRequest {
  expectedRowVersion: number;
}

export interface OutsourcedLifecycleResult {
  id: string;
  rowVersion?: number;
  deleted?: boolean;
}

interface CommandOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export function changeOutsourcedLifecycle(
  target: OutsourcedLifecycleTarget,
  id: string,
  action: OutsourcedLifecycleAction,
  request: OutsourcedLifecycleRequest,
  options: CommandOptions,
): Promise<OutsourcedLifecycleResult> {
  const base = target === 'batch'
    ? `/api/v1/outsourced/batches/${encodeURIComponent(id)}`
    : `/api/v1/outsourced/supply-details/${encodeURIComponent(id)}`;
  return postApiCommand<OutsourcedLifecycleRequest, OutsourcedLifecycleResult>(
    `${base}/${action}`,
    request,
    options,
  );
}
