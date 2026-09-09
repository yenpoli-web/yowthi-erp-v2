import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export interface SalesWorkspaceListItem {
  id: string;
  salesDate: string;
  customerId: string;
  customerDisplayName: string;
  status: string;
  rowVersion: number;
  createdAt: string;
  confirmedAt: string | null;
  deletedAt: string | null;
}

export interface SalesWorkspaceDetail {
  id: string;
  lineNumber: number;
  salesProductId: string;
  productDisplayName: string;
  quantity: number;
  pricingBasis: string;
  salesWeight: number | null;
  unitPrice: number;
  amountThb: number;
  rowVersion: number;
  createdAt: string;
  deletedAt: string | null;
}

export interface SalesWorkspace {
  id: string;
  salesDate: string;
  customerId: string;
  customerDisplayName: string;
  status: string;
  rowVersion: number;
  createdAt: string;
  confirmedAt: string | null;
  deletedAt: string | null;
  details: SalesWorkspaceDetail[];
}

interface ListQuery {
  locale: OperationalLocale;
  search?: string;
  offset?: number;
  limit?: number;
  signal?: AbortSignal;
}

interface ListResponse {
  items: SalesWorkspaceListItem[];
  nextOffset: number | null;
}

export function listSalesWorkspace(query: ListQuery): Promise<ListResponse> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<ListResponse>(`/api/v1/sales/workspace${suffix}`, {
    locale: query.locale,
    signal: query.signal,
  });
}

export function getSalesWorkspace(
  salesId: string,
  locale: OperationalLocale,
  signal?: AbortSignal,
): Promise<SalesWorkspace> {
  return getApiJson<SalesWorkspace>(
    `/api/v1/sales/workspace/${encodeURIComponent(salesId)}`,
    { locale, signal },
  );
}
