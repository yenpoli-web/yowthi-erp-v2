import { getApiJson, postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export type SupplierMasterStatus = 'all' | 'active' | 'inactive' | 'deleted';

export interface SupplierMasterItem {
  id: string;
  nameZhTw: string | null;
  nameThTh: string | null;
  bankName: string | null;
  bankAccount: string | null;
  phone: string | null;
  address: string | null;
  active: boolean;
  rowVersion: number;
  createdAt: string;
  deletedAt: string | null;
}

export interface SupplierMasterPage {
  items: SupplierMasterItem[];
  nextOffset: number | null;
}

export interface SupplierDraftRequest {
  nameZhTw: string | null;
  nameThTh: string | null;
  bankName: string | null;
  bankAccount: string | null;
  phone: string | null;
  address: string | null;
  active: boolean;
}

export type CreateSupplierRequest = SupplierDraftRequest;

export interface UpdateSupplierRequest extends SupplierDraftRequest {
  expectedRowVersion: number;
}

export interface SupplierWriteResult {
  supplierId: string;
  rowVersion: number;
}

interface SupplierQuery {
  locale: OperationalLocale;
  search?: string;
  status?: SupplierMasterStatus;
  offset?: number;
  limit?: number;
  signal?: AbortSignal;
}

interface SupplierCommandOptions {
  locale: OperationalLocale;
  idempotencyKey: string;
  signal?: AbortSignal;
}

export function listSuppliers(query: SupplierQuery): Promise<SupplierMasterPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.status) parameters.set('status', query.status);
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<SupplierMasterPage>(`/api/v1/party/suppliers${suffix}`, {
    locale: query.locale,
    signal: query.signal,
  });
}

export function createSupplier(
  request: CreateSupplierRequest,
  options: SupplierCommandOptions,
): Promise<SupplierWriteResult> {
  return postApiCommand<CreateSupplierRequest, SupplierWriteResult>(
    '/api/v1/party/suppliers',
    request,
    options,
  );
}

export function updateSupplier(
  supplierId: string,
  request: UpdateSupplierRequest,
  options: SupplierCommandOptions,
): Promise<SupplierWriteResult> {
  return postApiCommand<UpdateSupplierRequest, SupplierWriteResult>(
    `/api/v1/party/suppliers/${supplierId}/update`,
    request,
    options,
  );
}
