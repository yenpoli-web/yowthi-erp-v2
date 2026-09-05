import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export type ProcessingExecutionMode = 'SOURCE_TRACKED' | 'POOLED_OUTPUT' | 'FINAL_PACKAGING';
export type ProcessingOutputKind = 'PROCESS_MATERIAL' | 'SALES_PRODUCT';

export interface ProcessingEmployeeOption {
  id: string;
  displayName: string;
}

export interface ProcessingEmployeeOptionsResponse {
  items: ProcessingEmployeeOption[];
  nextCursor: string | null;
}

export interface ProcessingBatchOption {
  id: string;
  procurementDate: string;
  procurementProductId: string;
  procurementProductDisplayName: string;
  processingRouteVersionId: string;
}

export interface ProcessingBatchOptionsResponse {
  items: ProcessingBatchOption[];
  nextCursor: string | null;
}

export interface ProcessingModuleOption {
  id: string;
  displayName: string;
  executionMode: ProcessingExecutionMode;
  inputProcessMaterialId: string | null;
  inputUsesContainer: boolean | null;
  inputContainerId: string | null;
  defaultInputContainerCount: number | null;
}

export interface ProcessingBatchModuleOptionsResponse {
  procurementBatchId: string;
  processingRouteVersionId: string;
  items: ProcessingModuleOption[];
  nextCursor: string | null;
}

export interface ProcessingSupplierOption {
  id: string;
  displayName: string;
}

export interface ProcessingSupplierOptionsResponse {
  items: ProcessingSupplierOption[];
  nextCursor: string | null;
}

export interface ProcessingStorageLocationOption {
  id: string;
  displayName: string;
  code: string | null;
  warehouseId: string;
}

export interface ProcessingStorageLocationOptionsResponse {
  items: ProcessingStorageLocationOption[];
  nextCursor: string | null;
}

export interface ProcessingInputStorageLocationOptionsResponse {
  procurementBatchId: string;
  processingModuleId: string;
  autoSelectionLocationId: string | null;
  items: ProcessingStorageLocationOption[];
  nextCursor: string | null;
}

export interface ProcessingModuleOutputOption {
  id: string;
  outputSequence: number;
  outputKind: ProcessingOutputKind;
  targetId: string | null;
  targetDisplayName: string | null;
  targetAvailable: boolean;
  usesContainer: boolean;
  containerId: string | null;
  defaultContainerCount: number | null;
  packagingWeight: number | null;
  defaultStorageLocationId: string | null;
  defaultStorageLocationAvailable: boolean;
  defaultWageRate: number;
}

export interface ProcessingModuleOutputOptionsResponse {
  procurementBatchId: string;
  processingModuleId: string;
  outputs: ProcessingModuleOutputOption[];
}

interface OptionQuery {
  locale: OperationalLocale;
  search?: string;
  cursor?: string | null;
  limit?: number;
  signal?: AbortSignal;
}

export async function listProcessingEmployeeOptions(
  query: OptionQuery,
): Promise<ProcessingEmployeeOptionsResponse> {
  return getJson('/api/v1/processing/execution-options/employees', query);
}

export async function listProcessingBatchOptions(
  query: OptionQuery,
): Promise<ProcessingBatchOptionsResponse> {
  return getJson('/api/v1/processing/execution-options/batches', query);
}

export async function listProcessingModuleOptions(
  procurementBatchId: string,
  query: OptionQuery,
): Promise<ProcessingBatchModuleOptionsResponse> {
  return getJson(
    '/api/v1/processing/execution-options/modules',
    query,
    { procurementBatchId },
  );
}

export async function listProcessingSupplierOptions(
  query: OptionQuery,
): Promise<ProcessingSupplierOptionsResponse> {
  return getJson('/api/v1/processing/execution-options/suppliers', query);
}

export async function listProcessingInputStorageLocationOptions(
  procurementBatchId: string,
  processingModuleId: string,
  query: OptionQuery,
): Promise<ProcessingInputStorageLocationOptionsResponse> {
  return getJson(
    '/api/v1/processing/execution-options/input-storage-locations',
    query,
    { procurementBatchId, processingModuleId },
  );
}

export async function getProcessingModuleOutputOptions(
  procurementBatchId: string,
  processingModuleId: string,
  locale: OperationalLocale,
  signal?: AbortSignal,
): Promise<ProcessingModuleOutputOptionsResponse> {
  return getJson(
    '/api/v1/processing/execution-options/module-outputs',
    { locale, signal },
    { procurementBatchId, processingModuleId },
  );
}

export async function listProcessingStorageLocationOptions(
  query: OptionQuery,
): Promise<ProcessingStorageLocationOptionsResponse> {
  return getJson('/api/v1/processing/execution-options/storage-locations', query);
}

async function getJson<T>(
  path: string,
  query: OptionQuery,
  requiredParameters: Record<string, string> = {},
): Promise<T> {
  const parameters = new URLSearchParams(requiredParameters);
  const search = query.search?.trim();

  if (search) {
    parameters.set('search', search);
  }

  if (query.cursor) {
    parameters.set('cursor', query.cursor);
  }

  if (query.limit !== undefined) {
    parameters.set('limit', String(query.limit));
  }

  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<T>(`${path}${suffix}`, {
    locale: query.locale,
    signal: query.signal,
  });
}
