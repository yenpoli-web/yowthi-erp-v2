import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export interface OutsourcedWorkspaceListItem {
  id: string;
  supplyDate: string;
  outsourcedVendorId: string;
  outsourcedVendorDisplayName: string;
  lifecycleStatus: string;
  rowVersion: number;
  createdAt: string;
  closedAt: string | null;
  deletedAt: string | null;
}

export interface OutsourcedWorkspaceDetail {
  id: string;
  salesProductId: string;
  salesProductDisplayName: string;
  quantity: number;
  pricingBasis: string;
  unitPrice: number;
  amountThb: number;
  rowVersion: number;
  recordedAt: string;
  deletedAt: string | null;
}

export interface OutsourcedWorkspace {
  id: string;
  supplyDate: string;
  outsourcedVendorId: string;
  outsourcedVendorDisplayName: string;
  lifecycleStatus: string;
  rowVersion: number;
  createdAt: string;
  closedAt: string | null;
  deletedAt: string | null;
  details: OutsourcedWorkspaceDetail[];
}

interface ListQuery {
  locale: OperationalLocale;
  search?: string;
  offset?: number;
  limit?: number;
  signal?: AbortSignal;
}

interface ListResponse {
  items: OutsourcedWorkspaceListItem[];
  nextOffset: number | null;
}

export function listOutsourcedWorkspace(query: ListQuery): Promise<ListResponse> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<ListResponse>(`/api/v1/outsourced/workspace/${suffix}`, {
    locale: query.locale,
    signal: query.signal,
  });
}

export function getOutsourcedWorkspace(
  outsourcedSupplyBatchId: string,
  locale: OperationalLocale,
  signal?: AbortSignal,
): Promise<OutsourcedWorkspace> {
  return getApiJson<OutsourcedWorkspace>(
    `/api/v1/outsourced/workspace/${encodeURIComponent(outsourcedSupplyBatchId)}`,
    { locale, signal },
  );
}
