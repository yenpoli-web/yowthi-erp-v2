import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export type PartyLifecycleKind =
  | 'suppliers'
  | 'customers'
  | 'outsourced-vendors'
  | 'farmers'
  | 'employees';

export interface PartyLifecycleOption {
  id: string;
  displayName: string;
  active: boolean;
  rowVersion: number;
  deleted: boolean;
  deletedAt: string | null;
}

interface PartyLifecycleOptionPage {
  items: PartyLifecycleOption[];
  nextCursor: string | null;
}

interface PartyLifecycleOptionsQuery {
  locale: OperationalLocale;
  search?: string;
  cursor?: string | null;
  limit?: number;
  signal?: AbortSignal;
}

export function listPartyLifecycleOptions(
  kind: PartyLifecycleKind,
  query: PartyLifecycleOptionsQuery,
): Promise<PartyLifecycleOptionPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.cursor) parameters.set('cursor', query.cursor);
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;

  return getApiJson<PartyLifecycleOptionPage>(
    `/api/v1/party/lifecycle-options/${kind}${suffix}`,
    { locale: query.locale, signal: query.signal },
  );
}
