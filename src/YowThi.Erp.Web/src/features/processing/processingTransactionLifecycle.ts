import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export type ProcessingLifecycleTarget = 'execution' | 'input' | 'output';
export type ProcessingLifecycleAction = 'soft-delete' | 'restore' | 'hard-delete';

export interface ProcessingLifecycleRequest {
  expectedRowVersion: number;
}

export interface ProcessingLifecycleResult {
  id?: string;
  processingExecutionId?: string;
  processingExecutionOutputId?: string;
  rowVersion?: number;
  deleted?: boolean;
}

interface CommandOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export function changeProcessingLifecycle(
  target: ProcessingLifecycleTarget,
  id: string,
  action: ProcessingLifecycleAction,
  request: ProcessingLifecycleRequest,
  options: CommandOptions,
): Promise<ProcessingLifecycleResult> {
  const encodedId = encodeURIComponent(id);
  const path = target === 'execution'
    ? `/api/v1/processing/executions/${encodedId}/${action}`
    : target === 'input'
      ? `/api/v1/processing/executions/${encodedId}/input/${action}`
      : `/api/v1/processing/execution-outputs/${encodedId}/${action}`;

  return postApiCommand<ProcessingLifecycleRequest, ProcessingLifecycleResult>(path, request, options);
}
