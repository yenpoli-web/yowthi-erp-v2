import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useRef, useState } from 'react';

import { useOperationalLocale, type OperationalLocale } from '../../app/i18n/locale';
import { prepareDeletionReauthentication } from '../../app/security/authSession';
import { ApiProblemError, hardDeleteTarget } from './hardDelete';
import { hardDeleteCopy } from './hardDeleteCopy';
import {
  hardDeleteTargetKinds,
  listHardDeleteOptions,
  type HardDeleteOption,
  type HardDeleteTargetKind,
} from './hardDeleteOptions';
import './dataProtectionPrototype.css';

type SubmissionIdentity = { fingerprint: string; idempotencyKey: string };

const masterQueryKeys: Record<HardDeleteTargetKind, string> = {
  suppliers: 'supplier-master',
  customers: 'customer-master',
  'outsourced-vendors': 'outsourced-vendor-master',
  farmers: 'farmer-master',
  'procurement-products': 'procurement-product-master',
  'sales-products': 'sales-product-master',
  'sales-product-groups': 'sales-product-group-master',
  'outsourced-supply-batches': 'outsourced-workspace',
  'outsourced-supply-details': 'outsourced-workspace',
  'procurement-batches': 'procurement-workspace',
  'procurement-entries': 'procurement-workspace',
  sales: 'sales-workspace',
  'sales-details': 'sales-workspace',
  'processing-executions': 'processing-execution',
  'processing-execution-inputs': 'processing-execution-input',
  'processing-execution-outputs': 'processing-execution-output',
};

const presentationCopy = {
  'zh-TW': {
    recordsTitle: '資料對象',
    records: '筆資料',
    cancel: '取消',
    confirm: '確認永久刪除',
    empty: '目前沒有資料。',
  },
  'th-TH': {
    recordsTitle: 'รายการข้อมูล',
    records: 'รายการ',
    cancel: 'ยกเลิก',
    confirm: 'ยืนยันการลบถาวร',
    empty: 'ขณะนี้ไม่มีข้อมูล',
  },
} as const;

export function DataProtectionPage() {
  const { locale } = useOperationalLocale();
  const labels = hardDeleteCopy[locale];
  const presentation = presentationCopy[locale];
  const queryClient = useQueryClient();
  const [targetKind, setTargetKind] = useState<HardDeleteTargetKind>('suppliers');
  const [selectedId, setSelectedId] = useState('');
  const [confirmOpen, setConfirmOpen] = useState(false);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);

  const optionsQuery = useQuery({
    queryKey: ['hard-delete-options', targetKind, locale, 'all'],
    queryFn: ({ signal }) => loadAllHardDeleteOptions(targetKind, locale, signal),
    staleTime: 2_000,
  });

  const items = optionsQuery.data ?? [];
  const selected = items.find((item) => item.id === selectedId) ?? items[0] ?? null;
  const targetLabel = labels[targetKind];

  const mutation = useMutation({
    mutationFn: async (input: { item: HardDeleteOption; idempotencyKey: string }) => {
      await prepareDeletionReauthentication();
      return hardDeleteTarget(
        targetKind,
        input.item.id,
        { expectedRowVersion: input.item.rowVersion },
        { idempotencyKey: input.idempotencyKey, locale },
      );
    },
    onSuccess: async () => {
      const masterKey = masterQueryKeys[targetKind];
      setSelectedId('');
      setConfirmOpen(false);
      submissionIdentity.current = null;
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: [masterKey] }),
        queryClient.invalidateQueries({ queryKey: ['hard-delete-options', targetKind] }),
        queryClient.invalidateQueries({ queryKey: ['party-lifecycle-options', targetKind] }),
      ]);
    },
  });

  const problemMessage = useMemo(() => {
    const error = mutation.error ?? optionsQuery.error;
    if (!error) return null;
    if (!(error instanceof ApiProblemError)) return optionsQuery.error ? labels.queryFailed : labels.unexpected;
    if (error.code === 'security.deletion-reauth-required') return labels.reauthRequired;
    if (error.code === 'concurrency.stale-row-version') return labels.stale;
    if (error.code === 'idempotency.key-reused') return labels.idempotency;
    if (error.code === 'data-protection.hard-delete-invalid') return labels.invalid;
    if (error.code.endsWith('-dependency-blocked') || error.code.endsWith('-closure-invalid') || error.code.endsWith('-closure-ambiguous')) return labels.dependencyBlocked;
    if (error.code.endsWith('-not-found')) return labels.notFound;
    return `${error.code}${error.problem.traceId ? ` · ${error.problem.traceId}` : ''}`;
  }, [labels, mutation.error, optionsQuery.error]);

  function changeTarget(next: HardDeleteTargetKind) {
    setTargetKind(next);
    setSelectedId('');
    setConfirmOpen(false);
    submissionIdentity.current = null;
    mutation.reset();
  }

  function confirmHardDelete() {
    if (!selected) return;
    const fingerprint = JSON.stringify({ targetKind, id: selected.id, rowVersion: selected.rowVersion });
    if (submissionIdentity.current?.fingerprint !== fingerprint) {
      submissionIdentity.current = { fingerprint, idempotencyKey: crypto.randomUUID() };
    }
    mutation.mutate({ item: selected, idempotencyKey: submissionIdentity.current.idempotencyKey });
  }

  return (
    <section className="protection-prototype" aria-labelledby="protection-title">
      <header className="protection-prototype-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="protection-title">{labels.title}</h1>
        </div>
        <nav className="protection-target-switcher" aria-label={labels.title}>
          {hardDeleteTargetKinds.map((kind) => (
            <button
              key={kind}
              type="button"
              className={targetKind === kind ? 'is-active' : ''}
              onClick={() => changeTarget(kind)}
            >
              {labels[kind]}
            </button>
          ))}
        </nav>
      </header>

      <div className="protection-prototype-grid">
        <aside className="protection-list-panel" aria-label={presentation.recordsTitle}>
          <div className="protection-list-meta">
            <div>
              <strong>{presentation.recordsTitle}</strong>
              <span>{targetLabel}</span>
            </div>
            <span>{items.length} {presentation.records}</span>
          </div>
          <div className="protection-list-body">
            {items.map((item) => (
              <button
                key={item.id}
                type="button"
                className={`protection-list-item${selected?.id === item.id ? ' is-selected' : ''}`}
                onClick={() => {
                  setSelectedId(item.id);
                  submissionIdentity.current = null;
                  mutation.reset();
                }}
              >
                <span className="protection-list-name">
                  <strong>{item.displayName}</strong>
                  <small>{item.active ? labels.active : labels.inactive} · {item.deleted ? labels.deleted : labels.current}</small>
                </span>
                <span className={`protection-deleted-pill${item.deleted ? '' : ' is-current'}`}>
                  {item.deleted ? labels.deleted : labels.current}
                </span>
              </button>
            ))}
            {!optionsQuery.isPending && items.length === 0 && (
              <p className="protection-empty">{problemMessage ?? presentation.empty}</p>
            )}
          </div>
        </aside>

        <article className="protection-detail-panel">
          {selected ? (
            <>
              <header className="protection-detail-header">
                <div>
                  <p className="eyebrow">{targetLabel}</p>
                  <h2>{selected.displayName}</h2>
                </div>
              </header>
              <dl className="protection-detail-fields">
                <DetailRow label={labels.activeLabel} value={selected.active ? labels.active : labels.inactive} />
                <DetailRow label={labels.lifecycleState} value={selected.deleted ? labels.deleted : labels.current} />
                <DetailRow label={labels.deletedAt} value={formatDeletedAt(selected.deletedAt, locale, labels.notDeleted)} wide />
                <DetailRow label={labels.rowVersion} value={String(selected.rowVersion)} />
              </dl>
              {problemMessage && <div className="problem-banner" role="alert">{problemMessage}</div>}
              <footer className="protection-detail-footer">
                <button
                  className="protection-danger-action"
                  type="button"
                  disabled={mutation.isPending}
                  onClick={() => setConfirmOpen(true)}
                >
                  {labels.hardDelete}
                </button>
              </footer>
            </>
          ) : (
            <div className="protection-detail-empty">
              {optionsQuery.isPending ? '…' : problemMessage ?? presentation.empty}
            </div>
          )}
        </article>
      </div>

      {confirmOpen && selected && (
        <div className="protection-dialog-backdrop" role="presentation" onMouseDown={() => setConfirmOpen(false)}>
          <div
            className="protection-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="protection-dialog-title"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="protection-dialog-icon" aria-hidden="true">!</div>
            <div>
              <h2 id="protection-dialog-title">{labels.hardDelete} · {targetLabel}</h2>
              <p>{selected.displayName}</p>
              <p className="protection-dialog-warning">{labels.irreversible}</p>
            </div>
            <div className="protection-dialog-actions">
              <button className="protection-secondary-action" type="button" onClick={() => setConfirmOpen(false)}>
                {presentation.cancel}
              </button>
              <button
                className="protection-danger-action"
                type="button"
                disabled={mutation.isPending}
                onClick={confirmHardDelete}
              >
                {presentation.confirm}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}

async function loadAllHardDeleteOptions(
  targetKind: HardDeleteTargetKind,
  locale: OperationalLocale,
  signal: AbortSignal,
): Promise<HardDeleteOption[]> {
  const items: HardDeleteOption[] = [];
  let cursor: string | null = null;

  do {
    const page = await listHardDeleteOptions(targetKind, {
      locale,
      cursor,
      limit: 100,
      signal,
    });
    items.push(...page.items);
    cursor = page.nextCursor;
  } while (cursor !== null);

  return items;
}

function formatDeletedAt(value: string | null, locale: OperationalLocale, fallback: string): string {
  if (!value) return fallback;
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
}

function DetailRow({ label, value, wide = false }: { label: string; value: string; wide?: boolean }) {
  return <div className={`protection-detail-row${wide ? ' is-wide' : ''}`}><dt>{label}</dt><dd>{value}</dd></div>;
}