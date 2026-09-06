import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export type InfrastructureLifecycleKind = 'containers' | 'warehouses';

export interface ContainerLifecycleOption {
  id: string;
  displayName: string;
  tareWeight: number;
  active: boolean;
  rowVersion: number;
  deleted: boolean;
  deletedAt: string | null;
}

export interface WarehouseLifecycleOption {
  id: string;
  displayName: string;
  code: string | null;
  active: boolean;
  rowVersion: number;
  deleted: boolean;
  deletedAt: string | null;
}

export type InfrastructureLifecycleOption = ContainerLifecycleOption | WarehouseLifecycleOption;

interface OptionPage<T> {
  items: T[];
  nextCursor: string | null;
}

interface LifecycleOptionsQuery {
  locale: OperationalLocale;
  search?: string;
  cursor?: string | null;
  limit?: number;
  signal?: AbortSignal;
}

export function listContainerLifecycleOptions(query: LifecycleOptionsQuery): Promise<OptionPage<ContainerLifecycleOption>> {
  return getOptions<ContainerLifecycleOption>('containers', query);
}

export function listWarehouseLifecycleOptions(query: LifecycleOptionsQuery): Promise<OptionPage<WarehouseLifecycleOption>> {
  return getOptions<WarehouseLifecycleOption>('warehouses', query);
}

function getOptions<T>(kind: InfrastructureLifecycleKind, query: LifecycleOptionsQuery): Promise<OptionPage<T>> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.cursor) parameters.set('cursor', query.cursor);
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;

  return getApiJson<OptionPage<T>>(
    `/api/v1/infrastructure/lifecycle-options/${kind}${suffix}`,
    { locale: query.locale, signal: query.signal },
  );
}
