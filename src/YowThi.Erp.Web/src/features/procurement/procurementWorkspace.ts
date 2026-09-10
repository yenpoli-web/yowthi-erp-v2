import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export interface ProcurementBatchListItem {
  id: string;
  procurementDate: string;
  procurementProductId: string;
  procurementProductDisplayName: string;
  unitCode: string;
  procurementStatus: string;
  lifecycleStatus: string;
  rowVersion: number;
  createdAt: string;
  deletedAt: string | null;
}

export interface ProcurementBatchListResponse {
  items: ProcurementBatchListItem[];
  nextOffset: number | null;
}

export interface ProcurementReceiptDestination {
  storageLocationId: string;
  warehouseId: string;
  warehouseDisplayName: string;
  resolutionSource: string;
}

export interface ProcurementBatchEntry {
  id: string;
  sourceType: string;
  sourceId: string;
  sourceCode: string | null;
  sourceDisplayName: string;
  netQuantity: number;
  unitCodeSnapshot: string;
  unitPrice: number;
  amountThb: number;
  companyPickup: boolean;
  rowVersion: number;
  recordedAt: string;
  deletedAt: string | null;
}

export interface ProcurementBatchWorkspace {
  id: string;
  procurementDate: string;
  procurementProductId: string;
  procurementProductDisplayName: string;
  unitCode: string;
  receiptStorageLocationId: string | null;
  warehouseId: string | null;
  warehouseDisplayName: string | null;
  procurementStatus: string;
  lifecycleStatus: string;
  rowVersion: number;
  createdAt: string;
  completedAt: string | null;
  closedAt: string | null;
  deletedAt: string | null;
  entries: ProcurementBatchEntry[];
}

interface ListQuery {
  locale: OperationalLocale;
  search?: string;
  offset?: number;
  limit?: number;
  signal?: AbortSignal;
}

export async function getProcurementReceiptDestination(
  procurementDate: string,
  procurementProductId: string,
  locale: OperationalLocale,
  signal?: AbortSignal,
): Promise<ProcurementReceiptDestination> {
  const parameters = new URLSearchParams({ procurementDate, procurementProductId });
  return getApiJson<ProcurementReceiptDestination>(
    `/api/v1/procurement/receipt-destination?${parameters.toString()}`,
    { locale, signal },
  );
}

export async function listProcurementBatches(query: ListQuery): Promise<ProcurementBatchListResponse> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));

  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<ProcurementBatchListResponse>(`/api/v1/procurement/batches${suffix}`, {
    locale: query.locale,
    signal: query.signal,
  });
}

export async function getProcurementBatchWorkspace(
  procurementBatchId: string,
  locale: OperationalLocale,
  signal?: AbortSignal,
): Promise<ProcurementBatchWorkspace> {
  return getApiJson<ProcurementBatchWorkspace>(
    `/api/v1/procurement/batches/${encodeURIComponent(procurementBatchId)}`,
    { locale, signal },
  );
}
