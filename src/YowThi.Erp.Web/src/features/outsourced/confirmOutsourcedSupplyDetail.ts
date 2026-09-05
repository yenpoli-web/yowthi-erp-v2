import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';
export type { ApiProblemDetails } from '../../app/api/apiTransport';
export type { OperationalLocale } from '../../app/i18n/locale';

export interface ConfirmOutsourcedSupplyDetailRequest {
  supplyDate: string;
  outsourcedVendorId: string;
  salesProductId: string;
  quantity: number;
  unitPrice: number;
  receiptStorageLocationId: string | null;
}

export interface ConfirmOutsourcedSupplyDetailResult {
  outsourcedSupplyDetailId: string;
  outsourcedSupplyBatchId: string;
  inventoryOperationId: string;
  payableId: string;
  receiptStorageLocationId: string;
  amountThb: number;
  outsourcedSupplyDetailRowVersion: number;
}

interface ConfirmOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export async function confirmOutsourcedSupplyDetail(
  request: ConfirmOutsourcedSupplyDetailRequest,
  options: ConfirmOptions,
): Promise<ConfirmOutsourcedSupplyDetailResult> {
  return postApiCommand<ConfirmOutsourcedSupplyDetailRequest, ConfirmOutsourcedSupplyDetailResult>(
    '/api/v1/outsourced/supply-details',
    request,
    options,
  );
}
