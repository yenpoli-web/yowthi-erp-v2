import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export interface LaborDailyWageEmployeeOption { id: string; displayName: string; }
export interface LaborDailyWageEmployeeOptionsResponse { items: LaborDailyWageEmployeeOption[]; nextCursor: string | null; }
export interface LaborProcessingWageTarget { processingModuleOutputId: string; displayName: string; configuredWageRateSnapshot: number; aggregatedQuantity: number; }
export interface LaborDailyWageWorkspace { employeeId: string; workDate: string; employeeDisplayName: string; alreadyConfirmed: boolean; processingTargets: LaborProcessingWageTarget[]; salesPackagingWorkRecordCount: number; salesPackagingWageTotalThb: number; }

interface EmployeeOptionsQuery { locale: OperationalLocale; search?: string; cursor?: string | null; limit?: number; signal?: AbortSignal; }

export async function listLaborDailyWageEmployeeOptions(query: EmployeeOptionsQuery): Promise<LaborDailyWageEmployeeOptionsResponse> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.cursor) parameters.set('cursor', query.cursor);
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<LaborDailyWageEmployeeOptionsResponse>(`/api/v1/labor/daily-wage-options/employees${suffix}`, { locale: query.locale, signal: query.signal });
}

export async function getLaborDailyWageWorkspace(employeeId: string, workDate: string, locale: OperationalLocale, signal?: AbortSignal): Promise<LaborDailyWageWorkspace> {
  const parameters = new URLSearchParams({ workDate });
  return getApiJson<LaborDailyWageWorkspace>(`/api/v1/labor/employees/${employeeId}/daily-wage-workspace?${parameters.toString()}`, { locale, signal });
}
