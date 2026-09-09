import { getApiJson, postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export type InfrastructureMasterStatus = 'all' | 'active' | 'inactive' | 'deleted';
export type InventoryOrigin = 'IN_HOUSE' | 'OUTSOURCED';
export type InventoryObjectKind = 'PROCUREMENT_PRODUCT' | 'PROCESS_MATERIAL' | 'SALES_PRODUCT';
export type InventoryRawSourceKind = 'SUPPLIER' | 'FARMERS_COMBINED';

interface RequestOptions {
  locale: OperationalLocale;
  signal?: AbortSignal;
}

interface CommandOptions extends RequestOptions {
  idempotencyKey: string;
}

export interface InventoryPositionItem {
  id: string;
  origin: InventoryOrigin;
  sourceBatchId: string;
  objectKind: InventoryObjectKind;
  objectId: string;
  objectDisplayName: string;
  warehouseId: string;
  warehouseDisplayName: string;
  storageLocationId: string;
  storageLocationDisplayName: string;
  rawSourceKind: InventoryRawSourceKind | null;
  supplierId: string | null;
  supplierDisplayName: string | null;
  balanceQuantity: number;
  rowVersion: number;
}

export interface InventoryPositionPage {
  items: InventoryPositionItem[];
  nextOffset: number | null;
}

export interface InventoryPositionQuery extends RequestOptions {
  search?: string;
  origin?: InventoryOrigin | '';
  objectKind?: InventoryObjectKind | '';
  includeZeroBalance?: boolean;
  offset?: number;
  limit?: number;
}

export function listInventoryPositions(query: InventoryPositionQuery): Promise<InventoryPositionPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.origin) parameters.set('origin', query.origin);
  if (query.objectKind) parameters.set('objectKind', query.objectKind);
  if (query.includeZeroBalance) parameters.set('includeZeroBalance', 'true');
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size ? `?${parameters.toString()}` : '';
  return getApiJson(`/api/v1/inventory/positions${suffix}`, { locale: query.locale, signal: query.signal });
}

export interface WarehouseMasterItem {
  id: string;
  code: string | null;
  nameZhTw: string | null;
  nameThTh: string | null;
  active: boolean;
  rowVersion: number;
  createdAt: string;
  deletedAt: string | null;
}
export interface WarehouseMasterPage { items: WarehouseMasterItem[]; nextOffset: number | null }
export interface WarehouseDraft { code: string | null; nameZhTw: string | null; nameThTh: string | null; active: boolean }
export interface WarehouseWriteResult { warehouseId: string; rowVersion: number }

export function listWarehouses(query: { locale: OperationalLocale; search?: string; status?: InfrastructureMasterStatus; limit?: number; signal?: AbortSignal }): Promise<WarehouseMasterPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim(); if (search) parameters.set('search', search);
  if (query.status) parameters.set('status', query.status);
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size ? `?${parameters.toString()}` : '';
  return getApiJson(`/api/v1/infrastructure/warehouses${suffix}`, { locale: query.locale, signal: query.signal });
}
export function createWarehouse(request: WarehouseDraft, options: CommandOptions): Promise<WarehouseWriteResult> {
  return postApiCommand('/api/v1/infrastructure/warehouses', request, options);
}
export function updateWarehouse(id: string, request: WarehouseDraft & { expectedRowVersion: number }, options: CommandOptions): Promise<WarehouseWriteResult> {
  return postApiCommand(`/api/v1/infrastructure/warehouses/${id}/update`, request, options);
}

export interface StorageLocationMasterItem {
  id: string;
  warehouseId: string;
  warehouseDisplayName: string;
  code: string | null;
  nameZhTw: string | null;
  nameThTh: string | null;
  active: boolean;
  rowVersion: number;
  createdAt: string;
  deletedAt: string | null;
}
export interface StorageLocationMasterPage { items: StorageLocationMasterItem[]; nextOffset: number | null }
export interface StorageLocationDraft { warehouseId: string; code: string | null; nameZhTw: string | null; nameThTh: string | null; active: boolean }
export interface StorageLocationWriteResult { storageLocationId: string; rowVersion: number }
export interface WarehouseOption { id: string; displayName: string; code: string | null }
export interface WarehouseOptions { items: WarehouseOption[] }

export function listStorageLocations(query: { locale: OperationalLocale; search?: string; status?: InfrastructureMasterStatus; warehouseId?: string; limit?: number; signal?: AbortSignal }): Promise<StorageLocationMasterPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim(); if (search) parameters.set('search', search);
  if (query.status) parameters.set('status', query.status);
  if (query.warehouseId) parameters.set('warehouseId', query.warehouseId);
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size ? `?${parameters.toString()}` : '';
  return getApiJson(`/api/v1/infrastructure/storage-locations${suffix}`, { locale: query.locale, signal: query.signal });
}
export function listStorageLocationWarehouseOptions(locale: OperationalLocale, signal?: AbortSignal): Promise<WarehouseOptions> {
  return getApiJson(`/api/v1/infrastructure/storage-locations/warehouse-options?locale=${encodeURIComponent(locale)}&limit=200`, { locale, signal });
}
export function createStorageLocation(request: StorageLocationDraft, options: CommandOptions): Promise<StorageLocationWriteResult> {
  return postApiCommand('/api/v1/infrastructure/storage-locations', request, options);
}
export function updateStorageLocation(id: string, request: StorageLocationDraft & { expectedRowVersion: number }, options: CommandOptions): Promise<StorageLocationWriteResult> {
  return postApiCommand(`/api/v1/infrastructure/storage-locations/${id}/update`, request, options);
}
