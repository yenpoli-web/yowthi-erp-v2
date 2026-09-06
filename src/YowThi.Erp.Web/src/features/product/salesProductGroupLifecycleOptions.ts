import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export interface SalesProductGroupLifecycleOption {
  id: string;
  displayName: string;
  active: boolean;
  rowVersion: number;
  deleted: boolean;
  deletedAt: string | null;
}

interface SalesProductGroupLifecycleOptionPage {
  items: SalesProductGroupLifecycleOption[];
  nextCursor: string | null;
}

interface SalesProductGroupLifecycleOptionsQuery {
  locale: OperationalLocale;
  search?: string;
  cursor?: string | null;
  limit?: number;
  signal?: AbortSignal;
}

export function listSalesProductGroupLifecycleOptions(
  query: SalesProductGroupLifecycleOptionsQuery,
): Promise<SalesProductGroupLifecycleOptionPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.cursor) parameters.set('cursor', query.cursor);
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;

  return getApiJson<SalesProductGroupLifecycleOptionPage>(
    `/api/v1/product/lifecycle-options/sales-product-groups${suffix}`,
    { locale: query.locale, signal: query.signal },
  );
}
