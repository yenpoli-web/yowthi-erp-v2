import { getApiJson, postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';
import { prepareDeletionReauthentication } from '../../app/security/authSession';

export { ApiProblemError } from '../../app/api/apiTransport';

export type SalesPackagingItemMasterStatus = 'all' | 'active' | 'inactive' | 'deleted';

export interface SalesPackagingItemMasterItem {
  id: string;
  nameZhTw: string | null;
  nameThTh: string | null;
  active: boolean;
  rowVersion: number;
  createdAt: string;
  deletedAt: string | null;
}

export interface SalesPackagingItemMasterPage {
  items: SalesPackagingItemMasterItem[];
  nextOffset: number | null;
}

export interface SalesPackagingItemDraftRequest {
  nameZhTw: string | null;
  nameThTh: string | null;
  active: boolean;
}

export type CreateSalesPackagingItemRequest = SalesPackagingItemDraftRequest;

export interface UpdateSalesPackagingItemRequest extends SalesPackagingItemDraftRequest {
  expectedRowVersion: number;
}

export interface SalesPackagingItemWriteResult {
  salesPackagingItemId: string;
  rowVersion: number;
}

interface SalesPackagingItemQuery {
  locale: OperationalLocale;
  search?: string;
  status?: SalesPackagingItemMasterStatus;
  offset?: number;
  limit?: number;
  signal?: AbortSignal;
}

interface SalesPackagingItemCommandOptions {
  locale: OperationalLocale;
  idempotencyKey: string;
  signal?: AbortSignal;
}

export function listSalesPackagingItems(query: SalesPackagingItemQuery): Promise<SalesPackagingItemMasterPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.status) parameters.set('status', query.status);
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<SalesPackagingItemMasterPage>(`/api/v1/sales-handling/packaging-items${suffix}`, {
    locale: query.locale,
    signal: query.signal,
  });
}

export function createSalesPackagingItem(
  request: CreateSalesPackagingItemRequest,
  options: SalesPackagingItemCommandOptions,
): Promise<SalesPackagingItemWriteResult> {
  return postApiCommand<CreateSalesPackagingItemRequest, SalesPackagingItemWriteResult>(
    '/api/v1/sales-handling/packaging-items',
    request,
    options,
  );
}

export function updateSalesPackagingItem(
  salesPackagingItemId: string,
  request: UpdateSalesPackagingItemRequest,
  options: SalesPackagingItemCommandOptions,
): Promise<SalesPackagingItemWriteResult> {
  return postApiCommand<UpdateSalesPackagingItemRequest, SalesPackagingItemWriteResult>(
    `/api/v1/sales-handling/packaging-items/${salesPackagingItemId}/update`,
    request,
    options,
  );
}

export type SalesPackagingItemLifecycleAction = 'soft-delete' | 'restore';

export async function changeSalesPackagingItemLifecycle(
  salesPackagingItemId: string,
  action: SalesPackagingItemLifecycleAction,
  expectedRowVersion: number,
  options: SalesPackagingItemCommandOptions,
): Promise<{ salesPackagingItemId: string; rowVersion: number; deleted: boolean }> {
  if (action === 'soft-delete') await prepareDeletionReauthentication();
  return postApiCommand<{ expectedRowVersion: number }, { salesPackagingItemId: string; rowVersion: number; deleted: boolean }>(
    `/api/v1/sales-handling/packaging-items/${salesPackagingItemId}/${action}`,
    { expectedRowVersion },
    options,
  );
}
