import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useRef, useState } from 'react';

import { LocaleControl } from '../../app/i18n/LocaleControl';
import { useOperationalLocale } from '../../app/i18n/locale';
import { ApiProblemError, changePartyLifecycle, type PartyLifecycleAction } from './partyLifecycle';
import { listPartyLifecycleOptions, type PartyLifecycleKind } from './partyLifecycleOptions';
import { partyLifecycleCopy } from './partyLifecycleCopy';
import {
  partyKindLabel,
  partyLifecycleKinds,
  partyLifecycleProblemMessage,
  partyOptionLabel,
  type LifecycleMutationInput,
  type LifecycleMutationResult,
  type SubmissionIdentity,
} from './partyLifecycleUi';

export function PartyLifecyclePage() {
  const { locale } = useOperationalLocale();
  const labels = partyLifecycleCopy[locale];
  const queryClient = useQueryClient();
  const [kind, setKind] = useState<PartyLifecycleKind>('suppliers');
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

  const selected = optionsQuery.data?.items.find((item) => item.id === selectedId) ?? null;
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

  function resetSelection(nextKind?: PartyLifecycleKind) {
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

    mutation.mutate({
      ...draft,
      idempotencyKey: submissionIdentity.current.idempotencyKey,
    });
  }

  return (
    <section className="procurement-page" aria-labelledby="party-lifecycle-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="party-lifecycle-title">{labels.title}</h1>
          <p className="page-intro">{labels.intro}</p>
        </div>
        <LocaleControl />
      </header>

      <div className="procurement-grid">
        <div className="entry-form">
          <div className="field-grid">
            {partyLifecycleKinds.map((candidate) => (
              <button
                key={candidate}
                className="primary-action"
                type="button"
                disabled={kind === candidate}
                onClick={() => resetSelection(candidate)}
              >
                {partyKindLabel(candidate, labels)}
              </button>
            ))}

            <div className="option-picker full-width">
              <span className="field-label">{partyKindLabel(kind, labels)}</span>
              <input
                value={search}
                onChange={(event) => {
                  setSearch(event.target.value);
                  resetSelection();
                }}
                placeholder={labels.search}
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
                  <option key={item.id} value={item.id}>{partyOptionLabel(item, labels)}</option>
                ))}
              </select>
            </div>
          </div>

          <p className="page-intro">{labels.lifecycleHint}</p>

          {selected && (
            <dl>
              <ResultRow label={partyKindLabel(kind, labels)} value={selected.displayName} />
              <ResultRow label={labels.active} value={selected.active ? labels.active : labels.inactive} />
              <ResultRow label={labels.resultState} value={selected.deleted ? labels.deleted : labels.current} />
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
                <ResultRow label={labels.resultState} value={mutation.data.deleted ? labels.deleted : labels.current} />
                <ResultRow label={labels.resultRowVersion} value={String(mutation.data.rowVersion)} />
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

function ResultRow({ label, value }: { label: string; value: string }) {
  return <div className="result-row"><dt>{label}</dt><dd>{value}</dd></div>;
}
