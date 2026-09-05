import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';
export type { ApiProblemDetails } from '../../app/api/apiTransport';
export type { OperationalLocale } from '../../app/i18n/locale';
export type ProcurementSourceType = 'SUPPLIER' | 'FARMER';

export interface ConfirmProcurementEntryRequest {
  procurementDate: string;
  procurementProductId: string;
  sourceType: ProcurementSourceType;
  supplierId: string | null;
  farmerId: string | null;
  netQuantity: number;
  unitPrice: number;
  companyPickup: boolean;
  receiptStorageLocationId: string | null;
}

export interface ConfirmProcurementEntryResult {
  procurementEntryId: string;
  procurementBatchId: string;
  inventoryOperationId: string;
  payableId: string;
  companyPickupTransportBasisId: string | null;
  receiptStorageLocationId: string;
  amountThb: number;
  procurementEntryRowVersion: number;
}

interface ConfirmProcurementEntryOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export async function confirmProcurementEntry(
  request: ConfirmProcurementEntryRequest,
  options: ConfirmProcurementEntryOptions,
): Promise<ConfirmProcurementEntryResult> {
  return postApiCommand<ConfirmProcurementEntryRequest, ConfirmProcurementEntryResult>(
    '/api/v1/procurement/entries',
    request,
    options,
  );
}
