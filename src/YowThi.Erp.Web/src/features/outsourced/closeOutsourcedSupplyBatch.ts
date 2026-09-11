import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export interface CloseOutsourcedSupplyBatchResult {
  outsourcedSupplyBatchId: string;
  closedRowVersion: number;
}

interface CloseOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export function closeOutsourcedSupplyBatch(
  outsourcedSupplyBatchId: string,
  expectedRowVersion: number,
  options: CloseOptions,
): Promise<CloseOutsourcedSupplyBatchResult> {
  return postApiCommand<{ expectedRowVersion: number }, CloseOutsourcedSupplyBatchResult>(
    `/api/v1/outsourced/batches/${encodeURIComponent(outsourcedSupplyBatchId)}/close`,
    { expectedRowVersion },
    options,
  );
}
