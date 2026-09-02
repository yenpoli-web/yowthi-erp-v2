export type OperationalLocale = 'zh-TW' | 'th-TH';
export type ProcessingSourceKind = 'SUPPLIER' | 'FARMERS_COMBINED';

export interface ProcessingSourceSelectionRequest {
  sourceKind: ProcessingSourceKind;
  supplierId: string | null;
}

export interface ProcessingScaleMeasurementRequest {
  observedScaleReading: number;
  actualContainerCount: number | null;
}

export interface ProcessingOutputMeasurementRequest {
  processingModuleOutputId: string;
  observedScaleReading: number | null;
  actualContainerCount: number | null;
  completedQuantity: number | null;
  outputStorageLocationId: string | null;
}

export interface ConfirmProcessingExecutionRequest {
  workDate: string;
  employeeId: string;
  procurementBatchId: string;
  processingModuleId: string;
  source: ProcessingSourceSelectionRequest | null;
  inputScale: ProcessingScaleMeasurementRequest | null;
  inputStorageLocationId: string | null;
  outputs: ProcessingOutputMeasurementRequest[];
}

export interface ConfirmProcessingExecutionResult {
  processingExecutionId: string;
  inventoryOperationId: string;
  processingExecutionRowVersion: number;
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

interface ConfirmOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export async function confirmProcessingExecution(
  request: ConfirmProcessingExecutionRequest,
  options: ConfirmOptions,
): Promise<ConfirmProcessingExecutionResult> {
  const headers = new Headers({
    'Accept-Language': options.locale,
    'Content-Type': 'application/json',
    'Idempotency-Key': options.idempotencyKey,
  });

  const csrfToken = readCsrfToken();
  if (csrfToken !== null) {
    headers.set('X-CSRF-TOKEN', csrfToken);
  }

  const response = await fetch('/api/v1/processing/executions', {
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

  return (await response.json()) as ConfirmProcessingExecutionResult;
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
    return { status: response.status, title: response.statusText };
  }

  try {
    return (await response.json()) as ApiProblemDetails;
  } catch {
    return { status: response.status, title: response.statusText };
  }
}
