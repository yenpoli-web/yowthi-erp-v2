import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export interface ConfirmSalesRequest {
  expectedRowVersion: number;
  manualAllocationOverrides: SalesManualAllocationOverrideRequest[];
}

export interface SalesManualAllocationOverrideRequest {
  salesDetailId: string;
  origin: 'IN_HOUSE' | 'OUTSOURCED';
  procurementBatchId: string | null;
  outsourcedSupplyBatchId: string | null;
  allocatedQuantity: number;
}

export interface ConfirmSalesResult {
  salesId: string;
  rowVersion: number;
  receivableId: string;
  allocationRevisionId: string;
  inventoryOperationId: string;
}

interface ConfirmSalesOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export async function confirmSales(
  salesId: string,
  request: ConfirmSalesRequest,
  options: ConfirmSalesOptions,
): Promise<ConfirmSalesResult> {
  return postApiCommand<ConfirmSalesRequest, ConfirmSalesResult>(
    `/api/v1/sales/${salesId}/confirm`,
    request,
    options,
  );
}
