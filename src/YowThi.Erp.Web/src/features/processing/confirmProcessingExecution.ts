import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';
export type { ApiProblemDetails } from '../../app/api/apiTransport';
export type { OperationalLocale } from '../../app/i18n/locale';
export type ProcessingSourceKind = 'SUPPLIER' | 'FARMERS_COMBINED';

export interface ProcessingSourceSelectionRequest {
  sourceKind: ProcessingSourceKind;
  supplierId: string | null;
}

export interface ProcessingScaleMeasurementRequest {
  observedScaleReading: number;
  actualContainerCount: number | null;
}

export interface ProcessingOutputMeasurementRequest {
  processingModuleOutputId: string;
  observedScaleReading: number | null;
  actualContainerCount: number | null;
  completedQuantity: number | null;
  outputStorageLocationId: string | null;
}

export interface ConfirmProcessingExecutionRequest {
  workDate: string;
  employeeId: string;
  procurementBatchId: string;
  processingModuleId: string;
  source: ProcessingSourceSelectionRequest | null;
  inputScale: ProcessingScaleMeasurementRequest | null;
  inputStorageLocationId: string | null;
  outputs: ProcessingOutputMeasurementRequest[];
}

export interface ConfirmProcessingExecutionResult {
  processingExecutionId: string;
  inventoryOperationId: string;
  processingExecutionRowVersion: number;
}

interface ConfirmOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export async function confirmProcessingExecution(
  request: ConfirmProcessingExecutionRequest,
  options: ConfirmOptions,
): Promise<ConfirmProcessingExecutionResult> {
  return postApiCommand<ConfirmProcessingExecutionRequest, ConfirmProcessingExecutionResult>(
    '/api/v1/processing/executions',
    request,
    options,
  );
}
