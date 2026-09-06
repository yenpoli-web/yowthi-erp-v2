import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export interface ProcessingWageRateOverrideRequest {
  processingModuleOutputId: string;
  configuredWageRateSnapshot: number;
  appliedWageRate: number;
}

export interface ConfirmEmployeeDailyWageRequest {
  workDate: string;
  employeeId: string;
  processingWageRateOverrides: ProcessingWageRateOverrideRequest[];
}

export interface ConfirmEmployeeDailyWageResult {
  employeeDailyWageId: string;
  rowVersion: number;
  payableId: string;
  processingWageTotalThb: number;
  salesPackagingWageTotalThb: number;
  totalWageThb: number;
}

interface ConfirmEmployeeDailyWageOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export async function confirmEmployeeDailyWage(
  request: ConfirmEmployeeDailyWageRequest,
  options: ConfirmEmployeeDailyWageOptions,
): Promise<ConfirmEmployeeDailyWageResult> {
  return postApiCommand<ConfirmEmployeeDailyWageRequest, ConfirmEmployeeDailyWageResult>(
    '/api/v1/labor/daily-wages',
    request,
    options,
  );
}
