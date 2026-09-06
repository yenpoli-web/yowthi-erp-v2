import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export interface InventoryPositionIdentityRequest {
  origin: 'IN_HOUSE' | 'OUTSOURCED';
  procurementBatchId: string | null;
  outsourcedSupplyBatchId: string | null;
  inventoryObjectKind: 'PROCUREMENT_PRODUCT' | 'PROCESS_MATERIAL' | 'SALES_PRODUCT';
  procurementProductId: string | null;
  processMaterialId: string | null;
  salesProductId: string | null;
  rawSourceKind: 'SUPPLIER' | 'FARMERS_COMBINED' | null;
  supplierId: string | null;
}

export interface TransferInventoryRequest {
  inventoryIdentity: InventoryPositionIdentityRequest;
  sourceStorageLocationId: string;
  destinationStorageLocationId: string;
  quantity: number;
}

export interface TransferInventoryResult {
  inventoryOperationId: string;
  transferOutMovementId: string;
  transferInMovementId: string;
  quantity: number;
}

export interface AdjustInventoryRequest {
  inventoryIdentity: InventoryPositionIdentityRequest;
  storageLocationId: string;
  quantityDelta: number;
  reasonText: string;
}

export interface AdjustInventoryResult {
  inventoryOperationId: string;
  adjustmentMovementId: string;
  quantityDelta: number;
}

interface InventoryCommandOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export function transferInventory(
  request: TransferInventoryRequest,
  options: InventoryCommandOptions,
): Promise<TransferInventoryResult> {
  return postApiCommand<TransferInventoryRequest, TransferInventoryResult>(
    '/api/v1/inventory/transfers',
    request,
    options,
  );
}

export function adjustInventory(
  request: AdjustInventoryRequest,
  options: InventoryCommandOptions,
): Promise<AdjustInventoryResult> {
  return postApiCommand<AdjustInventoryRequest, AdjustInventoryResult>(
    '/api/v1/inventory/adjustments',
    request,
    options,
  );
}
