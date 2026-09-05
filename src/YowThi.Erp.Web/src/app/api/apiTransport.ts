import type { OperationalLocale } from '../i18n/locale';

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

interface ApiRequestOptions {
  locale: OperationalLocale;
  signal?: AbortSignal;
}

interface ApiCommandOptions extends ApiRequestOptions {
  idempotencyKey: string;
}

export async function getApiJson<T>(
  url: string,
  options: ApiRequestOptions,
): Promise<T> {
  const response = await fetch(url, {
    method: 'GET',
    credentials: 'include',
    headers: {
      'Accept-Language': options.locale,
    },
    signal: options.signal,
  });

  return readApiJson<T>(response);
}

export async function postApiCommand<TRequest, TResult>(
  url: string,
  request: TRequest,
  options: ApiCommandOptions,
): Promise<TResult> {
  const headers = new Headers({
    'Accept-Language': options.locale,
    'Content-Type': 'application/json',
    'Idempotency-Key': options.idempotencyKey,
  });

  const csrfToken = readCsrfToken();
  if (csrfToken !== null) {
    headers.set('X-CSRF-TOKEN', csrfToken);
  }

  const response = await fetch(url, {
    method: 'POST',
    credentials: 'include',
    headers,
    body: JSON.stringify(request),
    signal: options.signal,
  });

  return readApiJson<TResult>(response);
}

async function readApiJson<T>(response: Response): Promise<T> {
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
