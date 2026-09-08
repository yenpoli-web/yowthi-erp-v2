import { getApiJson, postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export type FarmerMasterStatus = 'all' | 'active' | 'inactive' | 'deleted';

export interface FarmerMasterItem {
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

export interface FarmerMasterPage {
  items: FarmerMasterItem[];
  nextOffset: number | null;
}

export interface FarmerDraftRequest {
  nameZhTw: string | null;
  nameThTh: string | null;
  bankName: string | null;
  bankAccount: string | null;
  phone: string | null;
  address: string | null;
  active: boolean;
}

export type CreateFarmerRequest = FarmerDraftRequest;

export interface UpdateFarmerRequest extends FarmerDraftRequest {
  expectedRowVersion: number;
}

export interface FarmerWriteResult {
  farmerId: string;
  rowVersion: number;
}

interface FarmerQuery {
  locale: OperationalLocale;
  search?: string;
  status?: FarmerMasterStatus;
  offset?: number;
  limit?: number;
  signal?: AbortSignal;
}

interface FarmerCommandOptions {
  locale: OperationalLocale;
  idempotencyKey: string;
  signal?: AbortSignal;
}

export function listFarmers(query: FarmerQuery): Promise<FarmerMasterPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.status) parameters.set('status', query.status);
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<FarmerMasterPage>(`/api/v1/party/farmers${suffix}`, {
    locale: query.locale,
    signal: query.signal,
  });
}

export function createFarmer(
  request: CreateFarmerRequest,
  options: FarmerCommandOptions,
): Promise<FarmerWriteResult> {
  return postApiCommand<CreateFarmerRequest, FarmerWriteResult>(
    '/api/v1/party/farmers',
    request,
    options,
  );
}

export function updateFarmer(
  farmerId: string,
  request: UpdateFarmerRequest,
  options: FarmerCommandOptions,
): Promise<FarmerWriteResult> {
  return postApiCommand<UpdateFarmerRequest, FarmerWriteResult>(
    `/api/v1/party/farmers/${farmerId}/update`,
    request,
    options,
  );
}
