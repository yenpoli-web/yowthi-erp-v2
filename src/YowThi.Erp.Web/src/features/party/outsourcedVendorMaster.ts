import { getApiJson, postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export type OutsourcedVendorMasterStatus = 'all' | 'active' | 'inactive' | 'deleted';

export interface OutsourcedVendorMasterItem {
  id: string;
  code: string | null;
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

export interface OutsourcedVendorMasterPage {
  items: OutsourcedVendorMasterItem[];
  nextOffset: number | null;
}

export interface OutsourcedVendorDraftRequest {
  code: string | null;
  nameZhTw: string | null;
  nameThTh: string | null;
  bankName: string | null;
  bankAccount: string | null;
  phone: string | null;
  address: string | null;
  active: boolean;
}

export type CreateOutsourcedVendorRequest = OutsourcedVendorDraftRequest;

export interface UpdateOutsourcedVendorRequest extends OutsourcedVendorDraftRequest {
  expectedRowVersion: number;
}

export interface OutsourcedVendorWriteResult {
  outsourcedVendorId: string;
  rowVersion: number;
}

interface OutsourcedVendorQuery {
  locale: OperationalLocale;
  search?: string;
  status?: OutsourcedVendorMasterStatus;
  offset?: number;
  limit?: number;
  signal?: AbortSignal;
}

interface OutsourcedVendorCommandOptions {
  locale: OperationalLocale;
  idempotencyKey: string;
  signal?: AbortSignal;
}

export function listOutsourcedVendors(query: OutsourcedVendorQuery): Promise<OutsourcedVendorMasterPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.status) parameters.set('status', query.status);
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<OutsourcedVendorMasterPage>(`/api/v1/party/outsourced-vendors${suffix}`, {
    locale: query.locale,
    signal: query.signal,
  });
}

export function createOutsourcedVendor(
  request: CreateOutsourcedVendorRequest,
  options: OutsourcedVendorCommandOptions,
): Promise<OutsourcedVendorWriteResult> {
  return postApiCommand<CreateOutsourcedVendorRequest, OutsourcedVendorWriteResult>(
    '/api/v1/party/outsourced-vendors',
    request,
    options,
  );
}

export function updateOutsourcedVendor(
  outsourcedVendorId: string,
  request: UpdateOutsourcedVendorRequest,
  options: OutsourcedVendorCommandOptions,
): Promise<OutsourcedVendorWriteResult> {
  return postApiCommand<UpdateOutsourcedVendorRequest, OutsourcedVendorWriteResult>(
    `/api/v1/party/outsourced-vendors/${outsourcedVendorId}/update`,
    request,
    options,
  );
}
