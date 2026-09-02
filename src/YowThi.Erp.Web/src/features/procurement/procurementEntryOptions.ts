import {
  ApiProblemError,
  type OperationalLocale,
  type ProcurementSourceType,
} from './confirmProcurementEntry';

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
  displayName: string;
}

export interface ProcurementSourceOptionsResponse {
  sourceType: ProcurementSourceType;
  items: ProcurementSourceOption[];
  nextCursor: string | null;
}

export interface ProcurementReceiptStorageLocationOption {
  id: string;
  displayName: string;
  code: string | null;
  warehouseId: string;
  isProductDefault: boolean;
}

export interface ProcurementReceiptStorageLocationOptionsResponse {
  procurementProductId: string;
  defaultStorageLocationId: string | null;
  items: ProcurementReceiptStorageLocationOption[];
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

export async function listProcurementReceiptStorageLocationOptions(
  procurementProductId: string,
  query: OptionQuery,
): Promise<ProcurementReceiptStorageLocationOptionsResponse> {
  return getJson<ProcurementReceiptStorageLocationOptionsResponse>(
    '/api/v1/procurement/entry-options/storage-locations',
    query,
    { procurementProductId },
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

  const response = await fetch(`${path}?${parameters.toString()}`, {
    method: 'GET',
    credentials: 'include',
    headers: {
      'Accept-Language': query.locale,
    },
    signal: query.signal,
  });

  if (!response.ok) {
    const problem = await readProblemDetails(response);
    throw new ApiProblemError(
      response.status,
      problem.code ?? `http.${response.status}`,
      problem,
    );
  }

  return (await response.json()) as T;
}

async function readProblemDetails(response: Response) {
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('application/problem+json') && !contentType.includes('application/json')) {
    return {
      status: response.status,
      title: response.statusText,
    };
  }

  try {
    return (await response.json()) as {
      type?: string;
      title?: string;
      status?: number;
      detail?: string;
      instance?: string;
      code?: string;
      traceId?: string;
    };
  } catch {
    return {
      status: response.status,
      title: response.statusText,
    };
  }
}
