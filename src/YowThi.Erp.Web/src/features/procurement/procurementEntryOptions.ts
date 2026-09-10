import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';
import type { ProcurementSourceType } from './confirmProcurementEntry';

export interface ProcurementProductOption {
  id: string;
  displayName: string;
  unitCode: string;
}

export interface ProcurementProductOptionsResponse {
  items: ProcurementProductOption[];
  nextCursor: string | null;
}

export interface ProcurementSourceOption {
  id: string;
  code: string | null;
  displayName: string;
}

export interface ProcurementSourceOptionsResponse {
  sourceType: ProcurementSourceType;
  items: ProcurementSourceOption[];
  nextCursor: string | null;
}

interface OptionQuery {
  locale: OperationalLocale;
  search?: string;
  cursor?: string | null;
  limit?: number;
  signal?: AbortSignal;
}

export async function listProcurementProductOptions(
  query: OptionQuery,
): Promise<ProcurementProductOptionsResponse> {
  return getJson<ProcurementProductOptionsResponse>(
    '/api/v1/procurement/entry-options/products',
    query,
  );
}

export async function listProcurementSourceOptions(
  sourceType: ProcurementSourceType,
  query: OptionQuery,
): Promise<ProcurementSourceOptionsResponse> {
  return getJson<ProcurementSourceOptionsResponse>(
    '/api/v1/procurement/entry-options/sources',
    query,
    { sourceType },
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
