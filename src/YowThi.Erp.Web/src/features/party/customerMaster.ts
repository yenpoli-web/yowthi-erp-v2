import { getApiJson, postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export type CustomerMasterStatus = 'all' | 'active' | 'inactive' | 'deleted';

export interface CustomerMasterItem {
  id: string;
  nameZhTw: string | null;
  nameThTh: string | null;
  phone: string | null;
  active: boolean;
  rowVersion: number;
  createdAt: string;
  deletedAt: string | null;
}

export interface CustomerMasterPage {
  items: CustomerMasterItem[];
  nextOffset: number | null;
}

export interface CustomerDraftRequest {
  nameZhTw: string | null;
  nameThTh: string | null;
  phone: string | null;
  active: boolean;
}

export type CreateCustomerRequest = CustomerDraftRequest;

export interface UpdateCustomerRequest extends CustomerDraftRequest {
  expectedRowVersion: number;
}

export interface CustomerWriteResult {
  customerId: string;
  rowVersion: number;
}

interface CustomerQuery {
  locale: OperationalLocale;
  search?: string;
  status?: CustomerMasterStatus;
  offset?: number;
  limit?: number;
  signal?: AbortSignal;
}

interface CustomerCommandOptions {
  locale: OperationalLocale;
  idempotencyKey: string;
  signal?: AbortSignal;
}

export function listCustomers(query: CustomerQuery): Promise<CustomerMasterPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.status) parameters.set('status', query.status);
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<CustomerMasterPage>(`/api/v1/party/customers${suffix}`, {
    locale: query.locale,
    signal: query.signal,
  });
}

export function createCustomer(
  request: CreateCustomerRequest,
  options: CustomerCommandOptions,
): Promise<CustomerWriteResult> {
  return postApiCommand<CreateCustomerRequest, CustomerWriteResult>(
    '/api/v1/party/customers',
    request,
    options,
  );
}

export function updateCustomer(
  customerId: string,
  request: UpdateCustomerRequest,
  options: CustomerCommandOptions,
): Promise<CustomerWriteResult> {
  return postApiCommand<UpdateCustomerRequest, CustomerWriteResult>(
    `/api/v1/party/customers/${customerId}/update`,
    request,
    options,
  );
}
