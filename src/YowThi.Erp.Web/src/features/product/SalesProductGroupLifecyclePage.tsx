import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useRef, useState } from 'react';

import { LocaleControl } from '../../app/i18n/LocaleControl';
import { useOperationalLocale } from '../../app/i18n/locale';
import {
  ApiProblemError,
  changeSalesProductGroupLifecycle,
  type SalesProductGroupLifecycleAction,
} from './salesProductGroupLifecycle';
import { salesProductGroupLifecycleCopy } from './salesProductGroupLifecycleCopy';
import {
  listSalesProductGroupLifecycleOptions,
  type SalesProductGroupLifecycleOption,
} from './salesProductGroupLifecycleOptions';

type MutationInput = {
  id: string;
  action: SalesProductGroupLifecycleAction;
  expectedRowVersion: number;
  idempotencyKey: string;
};

type MutationResult = {
  action: SalesProductGroupLifecycleAction;
  rowVersion: number;
  deleted: boolean;
};

type SubmissionIdentity = {
  fingerprint: string;
  idempotencyKey: string;
};

export function SalesProductGroupLifecyclePage() {
  const { locale } = useOperationalLocale();
  const labels = salesProductGroupLifecycleCopy[locale];
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [selectedId, setSelectedId] = useState('');
  const [localError, setLocalError] = useState<string | null>(null);
  const deferredSearch = useDeferredValue(search);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);

  const optionsQuery = useQuery({
    queryKey: ['sales-product-group-lifecycle-options', locale, deferredSearch],
    queryFn: ({ signal }) => listSalesProductGroupLifecycleOptions({
      locale,
      search: deferredSearch,
      limit: 100,
      signal,
    }),
    staleTime: 5_000,
  });

  const selected = optionsQuery.data?.items.find((item) => item.id === selectedId) ?? null;
  const action: SalesProductGroupLifecycleAction | null = selected === null
    ? null
    : selected.deleted ? 'restore' : 'soft-delete';

  const mutation = useMutation<MutationResult, Error, MutationInput>({
    mutationFn: async (input) => {
      const result = await changeSalesProductGroupLifecycle(
        input.id,
        input.action,
        { expectedRowVersion: input.expectedRowVersion },
        { idempotencyKey: input.idempotencyKey, locale },
      );
      return { action: input.action, rowVersion: result.rowVersion, deleted: result.deleted };
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['sales-product-group-lifecycle-options'] });
    },
  });

  const apiProblem = mutation.error instanceof ApiProblemError ? mutation.error : null;
  const problemMessage = useMemo(() => {
    if (apiProblem === null) return mutation.error ? labels.unexpected : null;
    const code = apiProblem.code;
    if (code === 'concurrency.stale-row-version') return labels.stale;
    if (code === 'idempotency.key-reused') return labels.idempotency;
    if (code === 'product.sales-product-group-lifecycle-invalid') return labels.invalid;
    if (code === 'product.sales-product-group-not-found') return labels.notFound;
    if (code === 'product.sales-product-group-already-deleted') return labels.alreadyDeleted;
    if (code === 'product.sales-product-group-not-deleted') return labels.notDeletedState;
    return `${code}${apiProblem.problem.traceId ? ` · ${apiProblem.problem.traceId}` : ''}`;
  }, [apiProblem, labels, mutation.error]);

  const queryErrorMessage = optionsQuery.error
    ? optionsQuery.error instanceof ApiProblemError
      ? `${labels.queryFailed}: ${optionsQuery.error.code}`
      : labels.queryFailed
    : null;

  function resetSelection() {
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
    const draft = {
      id: selected.id,
      action,
      expectedRowVersion: selected.rowVersion,
    };
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
    <section className="procurement-page" aria-labelledby="sales-product-group-lifecycle-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="sales-product-group-lifecycle-title">{labels.title}</h1>
        </div>
        <LocaleControl />
      </header>

      <div className="procurement-grid">
        <div className="entry-form">
          <div className="field-grid">
            <div className="option-picker full-width">
              <span className="field-label">Sales Product Group</span>
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
                <option value="">{optionsQuery.isPending ? labels.loading : labels.choose}</option>
                {(optionsQuery.data?.items ?? []).map((item) => (
                  <option key={item.id} value={item.id}>{optionLabel(item, labels)}</option>
                ))}
              </select>
            </div>
          </div>


          {selected && (
            <dl>
              <ResultRow label="Sales Product Group" value={selected.displayName} />
              <ResultRow label={labels.activeLabel} value={selected.active ? labels.active : labels.inactive} />
              <ResultRow label={labels.lifecycleState} value={selected.deleted ? labels.deleted : labels.current} />
              <ResultRow label={labels.rowVersion} value={String(selected.rowVersion)} />
              <ResultRow label={labels.deletedAt} value={selected.deletedAt ?? labels.notDeleted} />
            </dl>
          )}

          {(localError ?? problemMessage ?? queryErrorMessage) && (
            <div className="problem-banner" role="alert">
              {localError ?? problemMessage ?? queryErrorMessage}
            </div>
          )}

          <button
            className="primary-action"
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
              <p className="eyebrow">
                {mutation.data.action === 'restore' ? labels.successRestore : labels.successDelete}
              </p>
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
  item: SalesProductGroupLifecycleOption,
  labels: typeof salesProductGroupLifecycleCopy['zh-TW'] | typeof salesProductGroupLifecycleCopy['th-TH'],
): string {
  const active = item.active ? labels.active : labels.inactive;
  const lifecycle = item.deleted ? labels.deleted : labels.current;
  return `${item.displayName} · ${active} · ${lifecycle} · v${item.rowVersion}`;
}

function ResultRow({ label, value }: { label: string; value: string }) {
  return <div className="result-row"><dt>{label}</dt><dd>{value}</dd></div>;
}
