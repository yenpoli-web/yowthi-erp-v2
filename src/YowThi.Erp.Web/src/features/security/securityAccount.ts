import { getApiJson, postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export interface SecurityAccountItem {
  id: string;
  displayName: string;
  active: boolean;
  identityIssuer: string | null;
  identitySubject: string | null;
  rowVersion: number;
  createdAt: string;
  capabilities: string[];
  isDevelopmentTestAdmin: boolean;
}

export interface SecurityAccountManagementPage {
  items: SecurityAccountItem[];
  availableCapabilities: string[];
}

export interface SecurityAccountDraftRequest {
  displayName: string;
  active: boolean;
  identityIssuer: string | null;
  identitySubject: string | null;
  capabilities: string[];
}

export interface SecurityAccountUpdateRequest extends SecurityAccountDraftRequest {
  expectedRowVersion: number;
}

export interface SecurityAccountWriteResult {
  accountId: string;
  rowVersion: number;
}

interface QueryOptions {
  locale: OperationalLocale;
  search?: string;
  signal?: AbortSignal;
}

interface CommandOptions {
  locale: OperationalLocale;
  idempotencyKey: string;
  signal?: AbortSignal;
}

export function listSecurityAccounts(options: QueryOptions): Promise<SecurityAccountManagementPage> {
  const search = options.search?.trim();
  const suffix = search ? `?search=${encodeURIComponent(search)}` : '';
  return getApiJson<SecurityAccountManagementPage>(`/api/v1/security/accounts${suffix}`, {
    locale: options.locale,
    signal: options.signal,
  });
}

export function createSecurityAccount(
  request: SecurityAccountDraftRequest,
  options: CommandOptions,
): Promise<SecurityAccountWriteResult> {
  return postApiCommand<SecurityAccountDraftRequest, SecurityAccountWriteResult>(
    '/api/v1/security/accounts',
    request,
    options,
  );
}

export function updateSecurityAccount(
  accountId: string,
  request: SecurityAccountUpdateRequest,
  options: CommandOptions,
): Promise<SecurityAccountWriteResult> {
  return postApiCommand<SecurityAccountUpdateRequest, SecurityAccountWriteResult>(
    `/api/v1/security/accounts/${accountId}/update`,
    request,
    options,
  );
}
