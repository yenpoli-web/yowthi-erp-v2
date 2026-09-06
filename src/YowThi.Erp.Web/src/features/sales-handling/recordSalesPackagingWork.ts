import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export interface RecordSalesPackagingWorkRequest {
  salesId: string;
  workDate: string;
  employeeId: string;
  salesPackagingItemId: string;
  confirmedWageThb: number;
}

export interface RecordSalesPackagingWorkResult {
  salesPackagingWorkRecordId: string;
  rowVersion: number;
}

interface RecordSalesPackagingWorkOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export async function recordSalesPackagingWork(
  request: RecordSalesPackagingWorkRequest,
  options: RecordSalesPackagingWorkOptions,
): Promise<RecordSalesPackagingWorkResult> {
  return postApiCommand<RecordSalesPackagingWorkRequest, RecordSalesPackagingWorkResult>(
    '/api/v1/sales-handling/work-records',
    request,
    options,
  );
}
