import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useRef, useState } from 'react';

import { useOperationalLocale } from '../../app/i18n/locale';
import {
  ApiProblemError,
  changeInfrastructureLifecycle,
  type InfrastructureLifecycleAction,
} from './infrastructureLifecycle';
import { infrastructureLifecycleCopy } from './infrastructureLifecycleCopy';
import {
  listContainerLifecycleOptions,
  listWarehouseLifecycleOptions,
  type InfrastructureLifecycleKind,
  type InfrastructureLifecycleOption,
} from './infrastructureLifecycleOptions';

type MutationInput = {
  kind: InfrastructureLifecycleKind;
  id: string;
  action: InfrastructureLifecycleAction;
  expectedRowVersion: number;
  idempotencyKey: string;
};

type MutationResult = {
  action: InfrastructureLifecycleAction;
  rowVersion: number;
  deleted: boolean;
};

type SubmissionIdentity = {
  fingerprint: string;
  idempotencyKey: string;
};

export function InfrastructureLifecyclePage() {
  const { locale } = useOperationalLocale();
  const labels = infrastructureLifecycleCopy[locale];
  const queryClient = useQueryClient();
  const [kind, setKind] = useState<InfrastructureLifecycleKind>('containers');
  const [search, setSearch] = useState('');
  const [selectedId, setSelectedId] = useState('');
  const [localError, setLocalError] = useState<string | null>(null);
  const deferredSearch = useDeferredValue(search);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);

  const containersQuery = useQuery({
    queryKey: ['infrastructure-lifecycle-options', 'containers', locale, deferredSearch],
    queryFn: ({ signal }) => listContainerLifecycleOptions({ locale, search: deferredSearch, limit: 100, signal }),
    enabled: kind === 'containers',
    staleTime: 5_000,
  });

  const warehousesQuery = useQuery({
    queryKey: ['infrastructure-lifecycle-options', 'warehouses', locale, deferredSearch],
    queryFn: ({ signal }) => listWarehouseLifecycleOptions({ locale, search: deferredSearch, limit: 100, signal }),
    enabled: kind === 'warehouses',
    staleTime: 5_000,
  });

  const currentItems: InfrastructureLifecycleOption[] = kind === 'containers'
    ? containersQuery.data?.items ?? []
    : warehousesQuery.data?.items ?? [];
  const currentQuery = kind === 'containers' ? containersQuery : warehousesQuery;
  const selected = currentItems.find((item) => item.id === selectedId) ?? null;
  const action: InfrastructureLifecycleAction | null = selected === null
    ? null
    : selected.deleted ? 'restore' : 'soft-delete';

  const mutation = useMutation<MutationResult, Error, MutationInput>({
    mutationFn: async (input) => {
      const result = await changeInfrastructureLifecycle(
        input.kind,
        input.id,
        input.action,
        { expectedRowVersion: input.expectedRowVersion },
        { idempotencyKey: input.idempotencyKey, locale },
      );
      return { action: input.action, rowVersion: result.rowVersion, deleted: result.deleted };
    },
    onSuccess: async (_, input) => {
      await queryClient.invalidateQueries({ queryKey: ['infrastructure-lifecycle-options', input.kind] });
    },
  });

  const apiProblem = mutation.error instanceof ApiProblemError ? mutation.error : null;
  const problemMessage = useMemo(() => {
    if (apiProblem === null) return mutation.error ? labels.unexpected : null;
    const code = apiProblem.code;
    if (code === 'concurrency.stale-row-version') return labels.stale;
    if (code === 'idempotency.key-reused') return labels.idempotency;
    if (code.endsWith('-lifecycle-invalid')) return labels.invalid;
    if (code.endsWith('-not-found')) return labels.notFound;
    if (code.endsWith('-already-deleted')) return labels.alreadyDeleted;
    if (code.endsWith('-not-deleted')) return labels.notDeletedState;
    return `${code}${apiProblem.problem.traceId ? ` · ${apiProblem.problem.traceId}` : ''}`;
  }, [apiProblem, labels, mutation.error]);

  const queryErrorMessage = currentQuery.error
    ? currentQuery.error instanceof ApiProblemError
      ? `${labels.queryFailed}: ${currentQuery.error.code}`
      : labels.queryFailed
    : null;

  function resetSelection(nextKind?: InfrastructureLifecycleKind) {
    if (nextKind !== undefined) setKind(nextKind);
    setSelectedId('');
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

    mutation.mutate({ ...draft, idempotencyKey: submissionIdentity.current.idempotencyKey });
  }

  return (
    <section className="procurement-page" aria-labelledby="infrastructure-lifecycle-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="infrastructure-lifecycle-title">{labels.title}</h1>
        </div>
      </header>

      <div className="procurement-grid">
        <div className="entry-form">
          <div className="field-grid">
            <button
              className="primary-action"
              type="button"
              disabled={kind === 'containers'}
              onClick={() => resetSelection('containers')}
            >
              {labels.containers}
            </button>
            <button
              className="primary-action"
              type="button"
              disabled={kind === 'warehouses'}
              onClick={() => resetSelection('warehouses')}
            >
              {labels.warehouses}
            </button>

            <div className="option-picker full-width">
              <span className="field-label">{kind === 'containers' ? labels.containers : labels.warehouses}</span>
              <input
                value={search}
                onChange={(event) => {
                  setSearch(event.target.value);
                  resetSelection();
                }}
              />
              <select
                value={selectedId}
                onChange={(event) => {
                  setSelectedId(event.target.value);
                  setLocalError(null);
                  submissionIdentity.current = null;
                  mutation.reset();
                }}
              >
                <option value="">{currentQuery.isPending ? labels.loading : labels.choose}</option>
                {currentItems.map((item) => (
                  <option key={item.id} value={item.id}>{optionLabel(item, labels)}</option>
                ))}
              </select>
            </div>
          </div>


          {selected && (
            <dl>
              <ResultRow label={kind === 'containers' ? labels.containers : labels.warehouses} value={selected.displayName} />
              <ResultRow label={labels.activeLabel} value={selected.active ? labels.active : labels.inactive} />
              <ResultRow label={labels.lifecycleState} value={selected.deleted ? labels.deleted : labels.current} />
              <ResultRow label={labels.rowVersion} value={String(selected.rowVersion)} />
              <ResultRow label={labels.deletedAt} value={selected.deletedAt ?? labels.notDeleted} />
              {'tareWeight' in selected && <ResultRow label={labels.tareWeight} value={String(selected.tareWeight)} />}
              {'code' in selected && <ResultRow label={labels.warehouseCode} value={selected.code ?? labels.noCode} />}
            </dl>
          )}

          {(localError ?? problemMessage ?? queryErrorMessage) && (
            <div className="problem-banner" role="alert">
              {localError ?? problemMessage ?? queryErrorMessage}
            </div>
          )}

          <button
            className={`primary-action${action === 'soft-delete' ? ' danger-action' : ''}`}
            type="button"
            onClick={submitLifecycle}
            disabled={mutation.isPending || selected === null}
          >
            {mutation.isPending ? labels.submitting : action === 'restore' ? labels.restore : labels.softDelete}
          </button>
        </div>

        <aside className="result-panel" aria-live="polite">
          {mutation.data ? (
            <>
              <p className="eyebrow">{mutation.data.action === 'restore' ? labels.successRestore : labels.successDelete}</p>
              <dl>
                <ResultRow label={labels.lifecycleState} value={mutation.data.deleted ? labels.deleted : labels.current} />
                <ResultRow label={labels.newRowVersion} value={String(mutation.data.rowVersion)} />
              </dl>
            </>
          ) : (
            <div className="result-placeholder" aria-hidden="true"><span>YowThi ERP V2</span></div>
          )}
        </aside>
      </div>
    </section>
  );
}

function optionLabel(
  item: InfrastructureLifecycleOption,
  labels: typeof infrastructureLifecycleCopy['zh-TW'] | typeof infrastructureLifecycleCopy['th-TH'],
): string {
  const active = item.active ? labels.active : labels.inactive;
  const lifecycle = item.deleted ? labels.deleted : labels.current;
  return `${item.displayName} · ${active} · ${lifecycle} · v${item.rowVersion}`;
}

function ResultRow({ label, value }: { label: string; value: string }) {
  return <div className="result-row"><dt>{label}</dt><dd>{value}</dd></div>;
}
