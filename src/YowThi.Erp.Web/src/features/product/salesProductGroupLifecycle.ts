import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export type SalesProductGroupLifecycleAction = 'soft-delete' | 'restore';

export interface SalesProductGroupLifecycleRequest {
  expectedRowVersion: number;
}

export interface SalesProductGroupLifecycleResult {
  rowVersion: number;
  deleted: boolean;
}

interface CommandOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export function changeSalesProductGroupLifecycle(
  id: string,
  action: SalesProductGroupLifecycleAction,
  request: SalesProductGroupLifecycleRequest,
  options: CommandOptions,
): Promise<SalesProductGroupLifecycleResult> {
  return postApiCommand<SalesProductGroupLifecycleRequest, SalesProductGroupLifecycleResult>(
    `/api/v1/product/sales-product-groups/${id}/${action}`,
    request,
    options,
  );
}
