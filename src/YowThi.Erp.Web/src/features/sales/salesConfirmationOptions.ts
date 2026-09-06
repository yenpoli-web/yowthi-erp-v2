import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export interface SalesConfirmationSaleOption {
  id: string;
  salesDate: string;
  customerDisplayName: string;
  rowVersion: number;
}

export interface SalesConfirmationOptionsResponse {
  items: SalesConfirmationSaleOption[];
  nextCursor: string | null;
}

export interface SalesConfirmationDetail {
  id: string;
  lineNumber: number;
  salesProductId: string;
  productDisplayName: string;
  quantity: number;
  pricingBasis: string;
  salesWeight: number | null;
  unitPrice: number;
  amountThb: number;
}

export interface SalesConfirmationWorkspace {
  salesId: string;
  salesDate: string;
  customerId: string;
  customerDisplayName: string;
  rowVersion: number;
  details: SalesConfirmationDetail[];
}

interface SalesOptionsQuery {
  locale: OperationalLocale;
  search?: string;
  cursor?: string | null;
  limit?: number;
  signal?: AbortSignal;
}

export async function listSalesConfirmationOptions(
  query: SalesOptionsQuery,
): Promise<SalesConfirmationOptionsResponse> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.cursor) parameters.set('cursor', query.cursor);
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<SalesConfirmationOptionsResponse>(
    `/api/v1/sales/confirmation-options/sales${suffix}`,
    { locale: query.locale, signal: query.signal },
  );
}

export async function getSalesConfirmationWorkspace(
  salesId: string,
  locale: OperationalLocale,
  signal?: AbortSignal,
): Promise<SalesConfirmationWorkspace> {
  return getApiJson<SalesConfirmationWorkspace>(
    `/api/v1/sales/${salesId}/confirmation-workspace`,
    { locale, signal },
  );
}
