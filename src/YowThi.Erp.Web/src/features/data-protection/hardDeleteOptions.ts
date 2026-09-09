import { getApiJson } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export const hardDeleteTargetKinds = [
  'suppliers',
  'customers',
  'outsourced-vendors',
  'farmers',
  'processing-executions',
  'processing-execution-inputs',
  'processing-execution-outputs',
] as const;

export type HardDeleteTargetKind = (typeof hardDeleteTargetKinds)[number];

export interface HardDeleteOption {
  id: string;
  displayName: string;
  active: boolean;
  rowVersion: number;
  deleted: boolean;
  deletedAt: string | null;
}

interface HardDeleteOptionPage {
  items: HardDeleteOption[];
  nextCursor: string | null;
}

interface HardDeleteOptionsQuery {
  locale: OperationalLocale;
  search?: string;
  cursor?: string | null;
  limit?: number;
  signal?: AbortSignal;
}

export function listHardDeleteOptions(
  kind: HardDeleteTargetKind,
  query: HardDeleteOptionsQuery,
): Promise<HardDeleteOptionPage> {
  const parameters = new URLSearchParams();
  const search = query.search?.trim();
  if (search) parameters.set('search', search);
  if (query.cursor) parameters.set('cursor', query.cursor);
  if (query.limit !== undefined) parameters.set('limit', String(query.limit));
  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`;

  return getApiJson<HardDeleteOptionPage>(
    `/api/v1/data-protection/hard-delete-options/${kind}${suffix}`,
    { locale: query.locale, signal: query.signal },
  );
}
