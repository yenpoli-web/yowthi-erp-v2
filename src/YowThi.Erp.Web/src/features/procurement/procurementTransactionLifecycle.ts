import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export type ProcurementLifecycleTarget = 'batch' | 'entry';
export type ProcurementLifecycleAction = 'soft-delete' | 'restore' | 'hard-delete';

export interface ProcurementLifecycleRequest {
  expectedRowVersion: number;
}

export interface ProcurementLifecycleResult {
  id?: string;
  procurementBatchId?: string;
  procurementEntryId?: string;
  rowVersion?: number;
  deleted?: boolean;
}

interface CommandOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export function changeProcurementLifecycle(
  target: ProcurementLifecycleTarget,
  id: string,
  action: ProcurementLifecycleAction,
  request: ProcurementLifecycleRequest,
  options: CommandOptions,
): Promise<ProcurementLifecycleResult> {
  const segment = target === 'batch' ? 'batches' : 'entries';
  return postApiCommand<ProcurementLifecycleRequest, ProcurementLifecycleResult>(
    `/api/v1/procurement/${segment}/${encodeURIComponent(id)}/${action}`,
    request,
    options,
  );
}
