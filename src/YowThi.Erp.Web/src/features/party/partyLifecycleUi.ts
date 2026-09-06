import type { PartyLifecycleAction } from './partyLifecycle';
import type { PartyLifecycleKind, PartyLifecycleOption } from './partyLifecycleOptions';
import { partyLifecycleCopy } from './partyLifecycleCopy';

export interface LifecycleMutationInput {
  kind: PartyLifecycleKind;
  id: string;
  action: PartyLifecycleAction;
  expectedRowVersion: number;
  idempotencyKey: string;
}

export interface LifecycleMutationResult {
  action: PartyLifecycleAction;
  rowVersion: number;
  deleted: boolean;
}

export interface SubmissionIdentity {
  fingerprint: string;
  idempotencyKey: string;
}

export const partyLifecycleKinds: readonly PartyLifecycleKind[] = [
  'suppliers',
  'customers',
  'outsourced-vendors',
  'farmers',
  'employees',
];

type PartyLifecycleLabels =
  | typeof partyLifecycleCopy['zh-TW']
  | typeof partyLifecycleCopy['th-TH'];

export function partyKindLabel(kind: PartyLifecycleKind, labels: PartyLifecycleLabels): string {
  switch (kind) {
    case 'suppliers': return labels.suppliers;
    case 'customers': return labels.customers;
    case 'outsourced-vendors': return labels.outsourcedVendors;
    case 'farmers': return labels.farmers;
    case 'employees': return labels.employees;
  }
}

export function partyOptionLabel(item: PartyLifecycleOption, labels: PartyLifecycleLabels): string {
  const active = item.active ? labels.active : labels.inactive;
  const lifecycle = item.deleted ? labels.deleted : labels.current;
  return `${item.displayName} · ${active} · ${lifecycle} · v${item.rowVersion}`;
}

export function partyLifecycleProblemMessage(code: string, labels: PartyLifecycleLabels): string | null {
  if (code === 'concurrency.stale-row-version') return labels.stale;
  if (code === 'idempotency.key-reused') return labels.idempotency;
  if (code.endsWith('-lifecycle-invalid')) return labels.invalid;
  if (code.endsWith('-not-found')) return labels.notFound;
  if (code.endsWith('-already-deleted')) return labels.alreadyDeleted;
  if (code.endsWith('-not-deleted')) return labels.notDeletedState;
  return null;
}
