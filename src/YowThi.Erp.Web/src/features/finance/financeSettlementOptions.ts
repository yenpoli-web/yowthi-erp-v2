import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export interface FinancePayableSettlementOption {
  payableId: string;
  payableKind: string;
  sourceDisplayName: string;
  outstandingThb: number;
  outstandingVersion: number;
  updatedAt: string;
}

export interface FinanceReceivableSettlementOption {
  receivableId: string;
  salesId: string;
  salesDate: string;
  customerDisplayName: string;
  outstandingThb: number;
  outstandingVersion: number;
  updatedAt: string;
}

interface OptionPage<T> {
  items: T[];
  nextCursor: string | null;
}

interface SettlementOptionsQuery {
  locale: OperationalLocale;
  search?: string;
  cursor?: string | null;
  limit?: number;
  signal?: AbortSignal;
}

export function listFinancePayableOptions(
  query: SettlementOptionsQuery,
): Promise<OptionPage<FinancePayableSettlementOption>> {
  return getSettlementOptions<FinancePayableSettlementOption>('payables', query);
}

export function listFinanceReceivableOptions(
  query: SettlementOptionsQuery,
): Promise<OptionPage<FinanceReceivableSettlementOption>> {
  return getSettlementOptions<FinanceReceivableSettlementOption>('receivables', query);
}

function getSettlementOptions<T>(
  resource: 'payables' | 'receivables',
  query: SettlementOptionsQuery,
): Promise<OptionPage<T>> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.cursor) parameters.set('cursor', query.cursor);
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;

  return getApiJson<OptionPage<T>>(
    `/api/v1/finance/settlement-options/${resource}${suffix}`,
    { locale: query.locale, signal: query.signal },
  );
}
