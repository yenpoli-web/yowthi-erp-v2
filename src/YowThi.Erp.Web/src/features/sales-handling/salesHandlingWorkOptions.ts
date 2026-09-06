import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export interface SalesHandlingSaleOption {
  id: string;
  salesDate: string;
  customerDisplayName: string;
  status: 'DRAFT' | 'CONFIRMED';
}

export interface SalesHandlingEmployeeOption {
  id: string;
  displayName: string;
}

export interface SalesHandlingPackagingItemOption {
  id: string;
  displayName: string;
}

interface OptionPage<T> {
  items: T[];
  nextCursor: string | null;
}

interface WorkOptionsQuery {
  locale: OperationalLocale;
  search?: string;
  cursor?: string | null;
  limit?: number;
  signal?: AbortSignal;
}

export async function listSalesHandlingSalesOptions(
  query: WorkOptionsQuery,
): Promise<OptionPage<SalesHandlingSaleOption>> {
  return getWorkOptions<SalesHandlingSaleOption>('sales', query);
}

export async function listSalesHandlingEmployeeOptions(
  query: WorkOptionsQuery,
): Promise<OptionPage<SalesHandlingEmployeeOption>> {
  return getWorkOptions<SalesHandlingEmployeeOption>('employees', query);
}

export async function listSalesHandlingPackagingItemOptions(
  query: WorkOptionsQuery,
): Promise<OptionPage<SalesHandlingPackagingItemOption>> {
  return getWorkOptions<SalesHandlingPackagingItemOption>('packaging-items', query);
}

async function getWorkOptions<T>(
  resource: 'sales' | 'employees' | 'packaging-items',
  query: WorkOptionsQuery,
): Promise<OptionPage<T>> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.cursor) parameters.set('cursor', query.cursor);
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;

  return getApiJson<OptionPage<T>>(
    `/api/v1/sales-handling/work-options/${resource}${suffix}`,
    { locale: query.locale, signal: query.signal },
  );
}
