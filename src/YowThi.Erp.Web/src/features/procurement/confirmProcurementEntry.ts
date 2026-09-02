export type OperationalLocale = 'zh-TW' | 'th-TH';
export type ProcurementSourceType = 'SUPPLIER' | 'FARMER';

export interface ConfirmProcurementEntryRequest {
  procurementDate: string;
  procurementProductId: string;
  sourceType: ProcurementSourceType;
  supplierId: string | null;
  farmerId: string | null;
  netQuantity: number;
  unitPrice: number;
  companyPickup: boolean;
  receiptStorageLocationId: string | null;
}

export interface ConfirmProcurementEntryResult {
  procurementEntryId: string;
  procurementBatchId: string;
  inventoryOperationId: string;
  payableId: string;
  companyPickupTransportBasisId: string | null;
  receiptStorageLocationId: string;
  amountThb: number;
  procurementEntryRowVersion: number;
}

export interface ApiProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  code?: string;
  traceId?: string;
}

export class ApiProblemError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly problem: ApiProblemDetails,
  ) {
    super(problem.title ?? code);
    this.name = 'ApiProblemError';
  }
}

interface ConfirmProcurementEntryOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export async function confirmProcurementEntry(
  request: ConfirmProcurementEntryRequest,
  options: ConfirmProcurementEntryOptions,
): Promise<ConfirmProcurementEntryResult> {
  const headers = new Headers({
    'Accept-Language': options.locale,
    'Content-Type': 'application/json',
    'Idempotency-Key': options.idempotencyKey,
  });

  const csrfToken = readCsrfToken();
  if (csrfToken !== null) {
    headers.set('X-CSRF-TOKEN', csrfToken);
  }

  const response = await fetch('/api/v1/procurement/entries', {
    method: 'POST',
    credentials: 'include',
    headers,
    body: JSON.stringify(request),
    signal: options.signal,
  });

  if (!response.ok) {
    const problem = await readProblemDetails(response);
    throw new ApiProblemError(
      response.status,
      problem.code ?? `http.${response.status}`,
      problem,
    );
  }

  return (await response.json()) as ConfirmProcurementEntryResult;
}

function readCsrfToken(): string | null {
  const metaToken = document
    .querySelector<HTMLMetaElement>('meta[name="csrf-token"]')
    ?.content.trim();

  if (metaToken) {
    return metaToken;
  }

  const cookie = document.cookie
    .split(';')
    .map((item) => item.trim())
    .find((item) => item.startsWith('XSRF-TOKEN='));

  if (!cookie) {
    return null;
  }

  return decodeURIComponent(cookie.slice('XSRF-TOKEN='.length));
}

async function readProblemDetails(response: Response): Promise<ApiProblemDetails> {
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('application/problem+json') && !contentType.includes('application/json')) {
    return {
      status: response.status,
      title: response.statusText,
    };
  }

  try {
    return (await response.json()) as ApiProblemDetails;
  } catch {
    return {
      status: response.status,
      title: response.statusText,
    };
  }
}
