import { getApiJson, postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export type EmployeeMasterStatus = 'all' | 'active' | 'inactive' | 'deleted';

export interface EmployeeMasterItem {
  id: string;
  nameZhTw: string | null;
  nameThTh: string | null;
  bankName: string | null;
  bankAccount: string | null;
  phone: string | null;
  address: string | null;
  active: boolean;
  rowVersion: number;
  createdAt: string;
  deletedAt: string | null;
}

export interface EmployeeMasterPage {
  items: EmployeeMasterItem[];
  nextOffset: number | null;
}

export interface EmployeeDraftRequest {
  nameZhTw: string | null;
  nameThTh: string | null;
  bankName: string | null;
  bankAccount: string | null;
  phone: string | null;
  address: string | null;
  active: boolean;
}

export type CreateEmployeeRequest = EmployeeDraftRequest;

export interface UpdateEmployeeRequest extends EmployeeDraftRequest {
  expectedRowVersion: number;
}

export interface EmployeeWriteResult {
  employeeId: string;
  rowVersion: number;
}

interface EmployeeQuery {
  locale: OperationalLocale;
  search?: string;
  status?: EmployeeMasterStatus;
  offset?: number;
  limit?: number;
  signal?: AbortSignal;
}

interface EmployeeCommandOptions {
  locale: OperationalLocale;
  idempotencyKey: string;
  signal?: AbortSignal;
}

export function listEmployees(query: EmployeeQuery): Promise<EmployeeMasterPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.status) parameters.set('status', query.status);
  if (query.offset !== undefined) parameters.set('offset', String(query.offset));
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;
  return getApiJson<EmployeeMasterPage>(`/api/v1/party/employees${suffix}`, {
    locale: query.locale,
    signal: query.signal,
  });
}

export function createEmployee(
  request: CreateEmployeeRequest,
  options: EmployeeCommandOptions,
): Promise<EmployeeWriteResult> {
  return postApiCommand<CreateEmployeeRequest, EmployeeWriteResult>(
    '/api/v1/party/employees',
    request,
    options,
  );
}

export function updateEmployee(
  employeeId: string,
  request: UpdateEmployeeRequest,
  options: EmployeeCommandOptions,
): Promise<EmployeeWriteResult> {
  return postApiCommand<UpdateEmployeeRequest, EmployeeWriteResult>(
    `/api/v1/party/employees/${employeeId}/update`,
    request,
    options,
  );
}
