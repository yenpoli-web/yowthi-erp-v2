import { getApiJson, postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';
import { prepareDeletionReauthentication } from '../../app/security/authSession';

export type MasterStatus = 'all' | 'active' | 'inactive' | 'deleted';
export type ProductMasterLifecycleAction = 'soft-delete' | 'restore';

interface QueryOptions {
  locale: OperationalLocale;
  search?: string;
  status?: MasterStatus;
  offset?: number;
  limit?: number;
  signal?: AbortSignal;
}

interface CommandOptions {
  locale: OperationalLocale;
  idempotencyKey: string;
  signal?: AbortSignal;
}

export interface ProcurementProductItem {
  id: string;
  nameZhTw: string | null;
  nameThTh: string | null;
  unitCode: string;
  defaultStorageLocationId: string | null;
  defaultStorageLocationDisplayName: string | null;
  active: boolean;
  rowVersion: number;
  createdAt: string;
  deletedAt: string | null;
}

export interface ProcurementProductPage { items: ProcurementProductItem[]; nextOffset: number | null }
export interface ProcurementProductDraft { nameZhTw: string | null; nameThTh: string | null; unitCode: string; defaultStorageLocationId: string | null; active: boolean }
export interface ProcurementProductWriteResult { procurementProductId: string; rowVersion: number }
export interface ProcurementProductLifecycleResult { procurementProductId: string; rowVersion: number; deleted: boolean }

export interface StorageLocationOption {
  id: string;
  displayName: string;
  code: string | null;
  warehouseId: string;
  warehouseDisplayName: string;
}
export interface StorageLocationOptions { items: StorageLocationOption[] }

export function listProcurementProducts(query: QueryOptions): Promise<ProcurementProductPage> {
  return getApiJson(`/api/v1/product/procurement-products${queryString(query)}`, { locale: query.locale, signal: query.signal });
}
export function createProcurementProduct(request: ProcurementProductDraft, options: CommandOptions): Promise<ProcurementProductWriteResult> {
  return postApiCommand('/api/v1/product/procurement-products', request, options);
}
export function updateProcurementProduct(id: string, request: ProcurementProductDraft & { expectedRowVersion: number }, options: CommandOptions): Promise<ProcurementProductWriteResult> {
  return postApiCommand(`/api/v1/product/procurement-products/${id}/update`, request, options);
}
export async function changeProcurementProductLifecycle(
  id: string,
  action: ProductMasterLifecycleAction,
  expectedRowVersion: number,
  options: CommandOptions,
): Promise<ProcurementProductLifecycleResult> {
  if (action === 'soft-delete') await prepareDeletionReauthentication();
  return postApiCommand<{ expectedRowVersion: number }, ProcurementProductLifecycleResult>(
    `/api/v1/product/procurement-products/${id}/${action}`,
    { expectedRowVersion },
    options,
  );
}
export function listProductStorageLocations(locale: OperationalLocale, signal?: AbortSignal): Promise<StorageLocationOptions> {
  return getApiJson('/api/v1/product/procurement-products/storage-location-options?limit=200', { locale, signal });
}

export interface SalesProductItem {
  id: string;
  salesProductGroupId: string;
  salesProductGroupDisplayName: string;
  nameZhTw: string | null;
  nameThTh: string | null;
  pricingBasis: 'WEIGHT_BASED_UNIT' | 'UNIT_BASED';
  packagingWeight: number | null;
  salesWeight: number | null;
  defaultStorageLocationId: string | null;
  defaultStorageLocationDisplayName: string | null;
  active: boolean;
  rowVersion: number;
  createdAt: string;
  deletedAt: string | null;
}
export interface SalesProductPage { items: SalesProductItem[]; nextOffset: number | null }
export interface SalesProductDraft {
  salesProductGroupId: string;
  nameZhTw: string | null;
  nameThTh: string | null;
  pricingBasis: 'WEIGHT_BASED_UNIT' | 'UNIT_BASED';
  packagingWeight: number | null;
  salesWeight: number | null;
  defaultStorageLocationId: string | null;
  active: boolean;
}
export interface SalesProductWriteResult { salesProductId: string; rowVersion: number }
export interface SalesProductLifecycleResult { salesProductId: string; rowVersion: number; deleted: boolean }
export interface SalesProductGroupOption { id: string; displayName: string }
export interface SalesProductGroupOptions { items: SalesProductGroupOption[] }

export function listSalesProducts(query: QueryOptions): Promise<SalesProductPage> {
  return getApiJson(`/api/v1/product/sales-products${queryString(query)}`, { locale: query.locale, signal: query.signal });
}
export function createSalesProduct(request: SalesProductDraft, options: CommandOptions): Promise<SalesProductWriteResult> {
  return postApiCommand('/api/v1/product/sales-products', request, options);
}
export function updateSalesProduct(id: string, request: SalesProductDraft & { expectedRowVersion: number }, options: CommandOptions): Promise<SalesProductWriteResult> {
  return postApiCommand(`/api/v1/product/sales-products/${id}/update`, request, options);
}
export async function changeSalesProductLifecycle(
  id: string,
  action: ProductMasterLifecycleAction,
  expectedRowVersion: number,
  options: CommandOptions,
): Promise<SalesProductLifecycleResult> {
  if (action === 'soft-delete') await prepareDeletionReauthentication();
  return postApiCommand<{ expectedRowVersion: number }, SalesProductLifecycleResult>(
    `/api/v1/product/sales-products/${id}/${action}`,
    { expectedRowVersion },
    options,
  );
}
export function listSalesProductGroupsForMaster(locale: OperationalLocale, signal?: AbortSignal): Promise<SalesProductGroupOptions> {
  return getApiJson('/api/v1/product/sales-products/group-options?limit=200', { locale, signal });
}
export function listSalesProductStorageLocations(locale: OperationalLocale, signal?: AbortSignal): Promise<StorageLocationOptions> {
  return getApiJson('/api/v1/product/sales-products/storage-location-options?limit=200', { locale, signal });
}

function queryString(query: QueryOptions): string {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.status) parameters.set('status', query.status);
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  return parameters.size ? `?${parameters.toString()}` : '';
}