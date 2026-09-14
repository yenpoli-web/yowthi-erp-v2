import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export interface ProcessingWorkspaceListItem {
  id: string;
  workDate: string;
  employeeId: string;
  employeeDisplayName: string;
  procurementBatchId: string;
  procurementDate: string;
  procurementProductId: string;
  procurementProductDisplayName: string;
  processingModuleId: string;
  processingModuleDisplayName: string;
  executionMode: string;
  rowVersion: number;
  recordedAt: string;
  deletedAt: string | null;
}

export interface ProcessingWorkspaceInput {
  processingExecutionId: string;
  consumptionBasis: string;
  consumedQuantity: number;
  observedScaleReading: number | null;
  actualContainerCount: number | null;
  tareWeightSnapshot: number | null;
  derivedNetQuantity: number | null;
  inventoryObjectKind: string | null;
  inventoryObjectId: string | null;
  inventoryObjectDisplayName: string | null;
  storageLocationId: string | null;
  storageLocationDisplayName: string | null;
  rowVersion: number;
  deletedAt: string | null;
}

export interface ProcessingWorkspaceOutput {
  id: string;
  processingModuleOutputId: string;
  outputSequence: number;
  outputKind: string;
  targetId: string | null;
  targetDisplayName: string | null;
  configuredWageRate: number;
  observedScaleReading: number | null;
  actualContainerCount: number | null;
  tareWeightSnapshot: number | null;
  derivedNetQuantity: number | null;
  completedQuantity: number | null;
  packagingWeightSnapshot: number | null;
  sourceConsumptionQuantity: number | null;
  storageLocationId: string | null;
  storageLocationDisplayName: string | null;
  rowVersion: number;
  deletedAt: string | null;
}

export interface ProcessingWorkspace {
  id: string;
  workDate: string;
  employeeId: string;
  employeeDisplayName: string;
  procurementBatchId: string;
  procurementDate: string;
  procurementProductId: string;
  procurementProductDisplayName: string;
  processingRouteVersionId: string;
  processingModuleId: string;
  processingModuleDisplayName: string;
  executionMode: string;
  sourceKind: string | null;
  supplierId: string | null;
  supplierDisplayName: string | null;
  rowVersion: number;
  recordedAt: string;
  deletedAt: string | null;
  input: ProcessingWorkspaceInput | null;
  outputs: ProcessingWorkspaceOutput[];
}

export type ProcessingWorkspaceStatusFilter = 'all' | 'active' | 'deleted';

interface ProcessingWorkspaceListQuery {
  locale: OperationalLocale;
  search?: string;
  status?: ProcessingWorkspaceStatusFilter;
  offset?: number;
  limit?: number;
  signal?: AbortSignal;
}

interface ProcessingWorkspaceListResponse {
  items: ProcessingWorkspaceListItem[];
  nextOffset: number | null;
}

export function listProcessingWorkspace(
  query: ProcessingWorkspaceListQuery,
): Promise<ProcessingWorkspaceListResponse> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.status !== undefined && query.status !== 'all') parameters.set('status', query.status);
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;

  return getApiJson<ProcessingWorkspaceListResponse>(
    `/api/v1/processing/workspace/${suffix}`,
    { locale: query.locale, signal: query.signal },
  );
}

export function getProcessingWorkspace(
  processingExecutionId: string,
  locale: OperationalLocale,
  signal?: AbortSignal,
): Promise<ProcessingWorkspace> {
  return getApiJson<ProcessingWorkspace>(
    `/api/v1/processing/workspace/${encodeURIComponent(processingExecutionId)}`,
    { locale, signal },
  );
}
