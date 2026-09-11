import { getApiJson, postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';
import { prepareDeletionReauthentication } from '../../app/security/authSession';

export { ApiProblemError } from '../../app/api/apiTransport';

export type SalesProductGroupMasterStatus = 'all' | 'active' | 'inactive' | 'deleted';

export interface SalesProductGroupMasterItem {
  id: string;
  nameZhTw: string | null;
  nameThTh: string | null;
  active: boolean;
  rowVersion: number;
  createdAt: string;
  deletedAt: string | null;
}

export interface SalesProductGroupMasterPage {
  items: SalesProductGroupMasterItem[];
  nextOffset: number | null;
}

export interface SalesProductGroupDraftRequest {
  nameZhTw: string | null;
  nameThTh: string | null;
  active: boolean;
}

export type CreateSalesProductGroupRequest = SalesProductGroupDraftRequest;

export interface UpdateSalesProductGroupRequest extends SalesProductGroupDraftRequest {
  expectedRowVersion: number;
}

export interface SalesProductGroupWriteResult {
  salesProductGroupId: string;
  rowVersion: number;
}

interface SalesProductGroupQuery {
  locale: OperationalLocale;
  search?: string;
  status?: SalesProductGroupMasterStatus;
  offset?: number;
  limit?: number;
  signal?: AbortSignal;
}

interface SalesProductGroupCommandOptions {
  locale: OperationalLocale;
  idempotencyKey: string;
  signal?: AbortSignal;
}

export function listSalesProductGroups(query: SalesProductGroupQuery): Promise<SalesProductGroupMasterPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.status) parameters.set('status', query.status);
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<SalesProductGroupMasterPage>(`/api/v1/product/sales-product-groups${suffix}`, {
    locale: query.locale,
    signal: query.signal,
  });
}

export function createSalesProductGroup(
  request: CreateSalesProductGroupRequest,
  options: SalesProductGroupCommandOptions,
): Promise<SalesProductGroupWriteResult> {
  return postApiCommand<CreateSalesProductGroupRequest, SalesProductGroupWriteResult>(
    '/api/v1/product/sales-product-groups',
    request,
    options,
  );
}

export function updateSalesProductGroup(
  salesProductGroupId: string,
  request: UpdateSalesProductGroupRequest,
  options: SalesProductGroupCommandOptions,
): Promise<SalesProductGroupWriteResult> {
  return postApiCommand<UpdateSalesProductGroupRequest, SalesProductGroupWriteResult>(
    `/api/v1/product/sales-product-groups/${salesProductGroupId}/update`,
    request,
    options,
  );
}

export type SalesProductGroupLifecycleAction = 'soft-delete' | 'restore';

export async function changeSalesProductGroupLifecycle(
  salesProductGroupId: string,
  action: SalesProductGroupLifecycleAction,
  expectedRowVersion: number,
  options: SalesProductGroupCommandOptions,
): Promise<{ salesProductGroupId: string; rowVersion: number; deleted: boolean }> {
  if (action === 'soft-delete') await prepareDeletionReauthentication();
  return postApiCommand<{ expectedRowVersion: number }, { salesProductGroupId: string; rowVersion: number; deleted: boolean }>(
    `/api/v1/product/sales-product-groups/${salesProductGroupId}/${action}`,
    { expectedRowVersion },
    options,
  );
}
