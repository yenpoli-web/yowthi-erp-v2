import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useRef, useState } from 'react';

import { useOperationalLocale } from '../../app/i18n/locale';
import { ApiProblemError, hardDeleteTarget } from './hardDelete';
import { hardDeleteCopy } from './hardDeleteCopy';
import {
  listHardDeleteOptions,
  type HardDeleteOption,
  type HardDeleteTargetKind,
} from './hardDeleteOptions';

type MutationInput = {
  kind: HardDeleteTargetKind;
  id: string;
  displayName: string;
  expectedRowVersion: number;
  idempotencyKey: string;
};

type MutationResult = {
  displayName: string;
};

type SubmissionIdentity = {
  fingerprint: string;
  idempotencyKey: string;
};

const targetKinds: HardDeleteTargetKind[] = [
  'suppliers',
  'customers',
  'outsourced-vendors',
  'farmers',
];

export function HardDeletePage() {
  const { locale } = useOperationalLocale();
  const labels = hardDeleteCopy[locale];
  const queryClient = useQueryClient();
  const [kind, setKind] = useState<HardDeleteTargetKind>('suppliers');
  const [search, setSearch] = useState('');
  const [selectedId, setSelectedId] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const deferredSearch = useDeferredValue(search);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);

  const optionsQuery = useQuery({
    queryKey: ['hard-delete-options', kind, locale, deferredSearch],
    queryFn: ({ signal }) => listHardDeleteOptions(kind, {
      locale,
      search: deferredSearch,
      limit: 100,
      signal,
    }),
    staleTime: 5_000,
  });

  const items = optionsQuery.data?.items ?? [];
  const selected = items.find((item) => item.id === selectedId) ?? null;

  const mutation = useMutation<MutationResult, Error, MutationInput>({
    mutationFn: async (input) => {
      await hardDeleteTarget(
        input.kind,
        input.id,
        { expectedRowVersion: input.expectedRowVersion },
        { idempotencyKey: input.idempotencyKey, locale },
      );
      return { displayName: input.displayName };
    },
    onSuccess: async (_, input) => {
      await queryClient.invalidateQueries({ queryKey: ['hard-delete-options', input.kind] });
      setSelectedId('');
      setConfirmed(false);
      submissionIdentity.current = null;
    },
  });

  const apiProblem = mutation.error instanceof ApiProblemError ? mutation.error : null;
  const problemMessage = useMemo(() => {
    if (apiProblem === null) return mutation.error ? labels.unexpected : null;
    const code = apiProblem.code;
    if (code === 'concurrency.stale-row-version') return labels.stale;
    if (code === 'idempotency.key-reused') return labels.idempotency;
    if (code === 'data-protection.hard-delete-invalid') return labels.invalid;
    if (code.endsWith('-dependency-blocked')) return labels.dependencyBlocked;
    if (code.endsWith('-not-found')) return labels.notFound;
    return `${code}${apiProblem.problem.traceId ? ` · ${apiProblem.problem.traceId}` : ''}`;
  }, [apiProblem, labels, mutation.error]);

  const queryErrorMessage = optionsQuery.error
    ? optionsQuery.error instanceof ApiProblemError
      ? `${labels.queryFailed}: ${optionsQuery.error.code}`
      : labels.queryFailed
    : null;

  function resetSelection(nextKind?: HardDeleteTargetKind) {
    if (nextKind !== undefined) setKind(nextKind);
    setSelectedId('');
    setConfirmed(false);
    setLocalError(null);
    submissionIdentity.current = null;
    mutation.reset();
  }

  function submitHardDelete() {
    if (selected === null) {
      setLocalError(labels.noSelection);
      return;
    }
    if (!confirmed) {
      setLocalError(labels.confirmationRequired);
      return;
    }

    setLocalError(null);
    mutation.reset();
    const draft = {
      kind,
      id: selected.id,
      displayName: selected.displayName,
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
    <section className="procurement-page" aria-labelledby="hard-delete-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="hard-delete-title">{labels.title}</h1>
        </div>
      </header>

      <div className="procurement-grid">
        <div className="entry-form">
          <div className="field-grid">
            {targetKinds.map((targetKind) => (
              <button
                key={targetKind}
                className="primary-action"
                type="button"
                disabled={kind === targetKind}
                onClick={() => resetSelection(targetKind)}
              >
                {labels[targetKind]}
              </button>
            ))}

            <div className="option-picker full-width">
              <span className="field-label">{labels[kind]}</span>
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
                  setConfirmed(false);
                  setLocalError(null);
                  submissionIdentity.current = null;
                  mutation.reset();
                }}
              >
                <option value="">{optionsQuery.isPending ? labels.loading : labels.choose}</option>
                {items.map((item) => (
                  <option key={item.id} value={item.id}>{optionLabel(item, labels)}</option>
                ))}
              </select>
            </div>
          </div>


          {selected && (
            <dl>
              <ResultRow label={labels[kind]} value={selected.displayName} />
              <ResultRow label={labels.activeLabel} value={selected.active ? labels.active : labels.inactive} />
              <ResultRow label={labels.lifecycleState} value={selected.deleted ? labels.deleted : labels.current} />
              <ResultRow label={labels.rowVersion} value={String(selected.rowVersion)} />
              <ResultRow label={labels.deletedAt} value={selected.deletedAt ?? labels.notDeleted} />
              <ResultRow label={labels.dependencyGate} value={dependencyNote(kind, labels)} />
            </dl>
          )}

          <label className="page-intro destructive-confirmation">
            <input
              type="checkbox"
              checked={confirmed}
              disabled={selected === null || mutation.isPending}
              onChange={(event) => {
                setConfirmed(event.target.checked);
                setLocalError(null);
                submissionIdentity.current = null;
                mutation.reset();
              }}
            />{' '}
            <strong>{labels.irreversible}：</strong> {labels.confirmation}
          </label>

          {(localError ?? problemMessage ?? queryErrorMessage) && (
            <div className="problem-banner" role="alert">
              {localError ?? problemMessage ?? queryErrorMessage}
            </div>
          )}

          <button
            className="primary-action danger-action"
            type="button"
            onClick={submitHardDelete}
            disabled={mutation.isPending || selected === null || !confirmed}
          >
            {mutation.isPending ? labels.submitting : labels.hardDelete}
          </button>
        </div>

        <aside className="result-panel" aria-live="polite">
          {mutation.data ? (
            <>
              <p className="eyebrow">{labels.success}</p>
              <dl>
                <ResultRow label={labels.deletedTarget} value={mutation.data.displayName} />
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
  item: HardDeleteOption,
  labels: typeof hardDeleteCopy['zh-TW'] | typeof hardDeleteCopy['th-TH'],
): string {
  const active = item.active ? labels.active : labels.inactive;
  const lifecycle = item.deleted ? labels.deleted : labels.current;
  return `${item.displayName} · ${active} · ${lifecycle} · v${item.rowVersion}`;
}

function dependencyNote(
  kind: HardDeleteTargetKind,
  labels: typeof hardDeleteCopy['zh-TW'] | typeof hardDeleteCopy['th-TH'],
): string {
  switch (kind) {
    case 'suppliers': return labels.dependencySupplier;
    case 'customers': return labels.dependencyCustomer;
    case 'outsourced-vendors': return labels.dependencyVendor;
    case 'farmers': return labels.dependencyFarmer;
  }
}

function ResultRow({ label, value }: { label: string; value: string }) {
  return <div className="result-row"><dt>{label}</dt><dd>{value}</dd></div>;
}
