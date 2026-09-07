import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router';

import { useOperationalLocale } from '../../app/i18n/locale';
import { ApiProblemError, changePartyLifecycle, type PartyLifecycleAction } from './partyLifecycle';
import { partyLifecycleCopy } from './partyLifecycleCopy';
import { listPartyLifecycleOptions, type PartyLifecycleKind } from './partyLifecycleOptions';
import {
  partyKindLabel,
  partyLifecycleProblemMessage,
  type LifecycleMutationInput,
  type LifecycleMutationResult,
  type SubmissionIdentity,
} from './partyLifecycleUi';
import { PartyMasterNavigation } from './PartyMasterNavigation';
import './supplierMasterPrototype.css';

function resolveKind(value: string | null): PartyLifecycleKind {
  switch (value) {
    case 'suppliers':
    case 'customers':
    case 'outsourced-vendors':
    case 'farmers':
    case 'employees':
      return value;
    default:
      return 'outsourced-vendors';
  }
}

export function PartyLifecyclePage() {
  const [searchParams] = useSearchParams();
  const kind = resolveKind(searchParams.get('kind'));
  return <PartyLifecycleWorkspace key={kind} kind={kind} />;
}

function PartyLifecycleWorkspace({ kind }: { kind: PartyLifecycleKind }) {
  const { locale } = useOperationalLocale();
  const labels = partyLifecycleCopy[locale];
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [selectedId, setSelectedId] = useState('');
  const [localError, setLocalError] = useState<string | null>(null);
  const deferredSearch = useDeferredValue(search);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);

  const optionsQuery = useQuery({
    queryKey: ['party-lifecycle-options', kind, locale, deferredSearch],
    queryFn: ({ signal }) => listPartyLifecycleOptions(kind, {
      locale,
      search: deferredSearch,
      limit: 100,
      signal,
    }),
    staleTime: 5_000,
  });

  const items = optionsQuery.data?.items ?? [];
  const selected = items.find((item) => item.id === selectedId) ?? items[0] ?? null;
  const action: PartyLifecycleAction | null = selected === null
    ? null
    : selected.deleted ? 'restore' : 'soft-delete';

  const mutation = useMutation<LifecycleMutationResult, Error, LifecycleMutationInput>({
    mutationFn: async (input) => {
      const result = await changePartyLifecycle(
        input.kind,
        input.id,
        input.action,
        { expectedRowVersion: input.expectedRowVersion },
        { idempotencyKey: input.idempotencyKey, locale },
      );
      return { action: input.action, rowVersion: result.rowVersion, deleted: result.deleted };
    },
    onSuccess: async (_, input) => {
      submissionIdentity.current = null;
      await queryClient.invalidateQueries({ queryKey: ['party-lifecycle-options', input.kind] });
    },
  });

  const apiProblem = mutation.error instanceof ApiProblemError ? mutation.error : null;
  const problemMessage = useMemo(() => {
    if (apiProblem === null) return mutation.error ? labels.unexpected : null;
    return partyLifecycleProblemMessage(apiProblem.code, labels)
      ?? `${apiProblem.code}${apiProblem.problem.traceId ? ` · ${apiProblem.problem.traceId}` : ''}`;
  }, [apiProblem, labels, mutation.error]);

  const queryErrorMessage = optionsQuery.error
    ? optionsQuery.error instanceof ApiProblemError
      ? `${labels.queryFailed}: ${optionsQuery.error.code}`
      : labels.queryFailed
    : null;

  const currentKindLabel = partyKindLabel(kind, labels);
  const masterEyebrow = locale === 'zh-TW' ? '夥伴管理' : 'จัดการคู่ค้า';

  function selectItem(id: string) {
    setSelectedId(id);
    setLocalError(null);
    submissionIdentity.current = null;
    mutation.reset();
  }

  function submitLifecycle() {
    if (selected === null || action === null) {
      setLocalError(labels.noSelection);
      return;
    }

    setLocalError(null);
    mutation.reset();
    const draft = { kind, id: selected.id, action, expectedRowVersion: selected.rowVersion };
    const fingerprint = JSON.stringify(draft);
    if (submissionIdentity.current?.fingerprint !== fingerprint) {
      submissionIdentity.current = { fingerprint, idempotencyKey: crypto.randomUUID() };
    }

    mutation.mutate({
      ...draft,
      idempotencyKey: submissionIdentity.current.idempotencyKey,
    });
  }

  return (
    <section className="supplier-master-prototype" aria-labelledby="party-lifecycle-title">
      <header className="supplier-master-header">
        <div>
          <p className="eyebrow">{masterEyebrow}</p>
          <h1 id="party-lifecycle-title">{currentKindLabel}</h1>
        </div>
        <div className="supplier-master-header-actions">
          <PartyMasterNavigation activeKind={kind} />
        </div>
      </header>

      <div className="supplier-master-toolbar">
        <label className="supplier-search">
          <SearchIcon />
          <input
            type="search"
            value={search}
            aria-label={labels.search}
            onChange={(event) => {
              setSearch(event.target.value);
              setSelectedId('');
              setLocalError(null);
              submissionIdentity.current = null;
              mutation.reset();
            }}
          />
        </label>
      </div>

      <div className="supplier-master-grid">
        <aside className="supplier-list-panel" aria-label={currentKindLabel}>
          <div className="supplier-list-meta">
            <strong>{currentKindLabel}</strong>
            <span>{items.length}</span>
          </div>
          <div className="supplier-list-heading" aria-hidden="true">
            <span>{currentKindLabel}</span>
            <span>{labels.resultState}</span>
            <span>{labels.rowVersion}</span>
          </div>
          <div className="supplier-list-body">
            {items.map((item) => (
              <button
                key={item.id}
                type="button"
                className={`supplier-list-item${selected?.id === item.id ? ' is-selected' : ''}`}
                onClick={() => selectItem(item.id)}
              >
                <span className="supplier-list-name"><strong>{item.displayName}</strong></span>
                <span className="supplier-list-phone">{item.deleted ? labels.deleted : item.active ? labels.active : labels.inactive}</span>
                <StatusPill active={item.active} deleted={item.deleted} labels={labels} />
              </button>
            ))}
            {!optionsQuery.isPending && items.length === 0 && (
              <p className="supplier-list-empty">{queryErrorMessage ?? labels.choose}</p>
            )}
          </div>
        </aside>

        <article className="supplier-detail-panel">
          {selected ? (
            <>
              <header className="supplier-detail-header">
                <div>
                  <div className="supplier-detail-title-row">
                    <h2>{selected.displayName}</h2>
                    <StatusPill active={selected.active} deleted={selected.deleted} labels={labels} />
                  </div>
                </div>
                <div className="supplier-detail-actions">
                  <button
                    className={action === 'soft-delete' ? 'supplier-danger-ghost-action' : 'supplier-primary-action'}
                    type="button"
                    onClick={submitLifecycle}
                    disabled={mutation.isPending}
                  >
                    {mutation.isPending ? labels.submitting : action === 'restore' ? labels.restore : labels.softDelete}
                  </button>
                </div>
              </header>

              <section className="supplier-detail-section">
                <h3>{labels.resultState}</h3>
                <dl className="supplier-detail-fields">
                  <DetailField label={labels.active} value={selected.active ? labels.active : labels.inactive} />
                  <DetailField label={labels.resultState} value={selected.deleted ? labels.deleted : labels.current} />
                  <DetailField label={labels.rowVersion} value={String(selected.rowVersion)} />
                  <DetailField label={labels.deletedAt} value={selected.deletedAt ?? labels.notDeleted} wide />
                </dl>
              </section>

              {(localError ?? problemMessage ?? queryErrorMessage) && (
                <div className="problem-banner" role="alert">
                  {localError ?? problemMessage ?? queryErrorMessage}
                </div>
              )}
            </>
          ) : (
            <div className="supplier-list-empty">
              {optionsQuery.isPending ? '…' : queryErrorMessage ?? labels.choose}
            </div>
          )}
        </article>
      </div>
    </section>
  );
}

function DetailField({ label, value, wide = false }: { label: string; value: string; wide?: boolean }) {
  return (
    <div className={`supplier-detail-field${wide ? ' is-wide' : ''}`}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function StatusPill({ active, deleted, labels }: { active: boolean; deleted: boolean; labels: typeof partyLifecycleCopy['zh-TW'] | typeof partyLifecycleCopy['th-TH'] }) {
  const state = deleted ? 'deleted' : active ? 'active' : 'inactive';
  const text = deleted ? labels.deleted : active ? labels.active : labels.inactive;
  return <span className={`supplier-status-pill is-${state}`}>{text}</span>;
}

function SearchIcon() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="6.5" /><path d="m16 16 4 4" /></svg>;
}
