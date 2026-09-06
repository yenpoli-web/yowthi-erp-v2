import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export interface InventoryOperationIdentityOption {
  origin: 'IN_HOUSE' | 'OUTSOURCED';
  procurementBatchId: string | null;
  outsourcedSupplyBatchId: string | null;
  batchDate: string;
  inventoryObjectKind: 'PROCUREMENT_PRODUCT' | 'PROCESS_MATERIAL' | 'SALES_PRODUCT';
  procurementProductId: string | null;
  processMaterialId: string | null;
  salesProductId: string | null;
  objectDisplayName: string;
  rawSourceKind: 'SUPPLIER' | 'FARMERS_COMBINED' | null;
  supplierId: string | null;
  rawSourceDisplayName: string | null;
}

export interface InventoryTransferSourceOption {
  inventoryPositionId: string;
  inventoryIdentity: InventoryOperationIdentityOption;
  sourceStorageLocationId: string;
  sourceStorageLocationDisplayName: string;
  sourceStorageLocationActive: boolean;
  balanceQuantity: number;
}

export interface InventoryStorageLocationOption {
  id: string;
  displayName: string;
  active: boolean;
}

interface OptionPage<T> {
  items: T[];
  nextCursor: string | null;
}

interface InventoryOptionsQuery {
  locale: OperationalLocale;
  search?: string;
  cursor?: string | null;
  limit?: number;
  signal?: AbortSignal;
}

export function listInventoryTransferSources(query: InventoryOptionsQuery): Promise<OptionPage<InventoryTransferSourceOption>> {
  return getOptions<InventoryTransferSourceOption>('transfer-sources', query);
}

export function listInventoryAdjustmentIdentities(query: InventoryOptionsQuery): Promise<OptionPage<InventoryOperationIdentityOption>> {
  return getOptions<InventoryOperationIdentityOption>('adjustment-identities', query);
}

export function listInventoryTransferDestinations(query: InventoryOptionsQuery): Promise<OptionPage<InventoryStorageLocationOption>> {
  return getOptions<InventoryStorageLocationOption>('transfer-destinations', query);
}

export function listInventoryAdjustmentLocations(query: InventoryOptionsQuery): Promise<OptionPage<InventoryStorageLocationOption>> {
  return getOptions<InventoryStorageLocationOption>('adjustment-locations', query);
}

function getOptions<T>(resource: string, query: InventoryOptionsQuery): Promise<OptionPage<T>> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.cursor) parameters.set('cursor', query.cursor);
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<OptionPage<T>>(
    `/api/v1/inventory/operation-options/${resource}${suffix}`,
    { locale: query.locale, signal: query.signal },
  );
}
