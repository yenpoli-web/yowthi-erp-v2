import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export interface OutsourcedVendorOption {
  id: string;
  displayName: string;
}

export interface OutsourcedVendorOptionsResponse {
  items: OutsourcedVendorOption[];
  nextCursor: string | null;
}

export interface OutsourcedSalesProductOption {
  id: string;
  displayName: string;
  pricingBasis: string;
}

export interface OutsourcedSalesProductOptionsResponse {
  items: OutsourcedSalesProductOption[];
  nextCursor: string | null;
}

export interface OutsourcedReceiptStorageLocationOption {
  id: string;
  displayName: string;
  code: string | null;
  warehouseId: string;
  isProductDefault: boolean;
}

export interface OutsourcedReceiptStorageLocationOptionsResponse {
  salesProductId: string;
  defaultStorageLocationId: string | null;
  items: OutsourcedReceiptStorageLocationOption[];
  nextCursor: string | null;
}

interface OptionQuery {
  locale: OperationalLocale;
  search?: string;
  cursor?: string | null;
  limit?: number;
  signal?: AbortSignal;
}

export async function listOutsourcedVendorOptions(
  query: OptionQuery,
): Promise<OutsourcedVendorOptionsResponse> {
  return getJson<OutsourcedVendorOptionsResponse>(
    '/api/v1/outsourced/supply-detail-options/vendors',
    query,
  );
}

export async function listOutsourcedSalesProductOptions(
  query: OptionQuery,
): Promise<OutsourcedSalesProductOptionsResponse> {
  return getJson<OutsourcedSalesProductOptionsResponse>(
    '/api/v1/outsourced/supply-detail-options/products',
    query,
  );
}

export async function listOutsourcedReceiptStorageLocationOptions(
  salesProductId: string,
  query: OptionQuery,
): Promise<OutsourcedReceiptStorageLocationOptionsResponse> {
  return getJson<OutsourcedReceiptStorageLocationOptionsResponse>(
    '/api/v1/outsourced/supply-detail-options/storage-locations',
    query,
    { salesProductId },
  );
}

async function getJson<T>(
  path: string,
  query: OptionQuery,
  requiredParameters: Record<string, string> = {},
): Promise<T> {
  const parameters = new URLSearchParams(requiredParameters);
  const search = query.search?.trim();

  if (search) {
    parameters.set('search', search);
  }

  if (query.cursor) {
    parameters.set('cursor', query.cursor);
  }

  if (query.limit !== undefined) {
    parameters.set('limit', String(query.limit));
  }

  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<T>(`${path}${suffix}`, {
    locale: query.locale,
    signal: query.signal,
  });
}
