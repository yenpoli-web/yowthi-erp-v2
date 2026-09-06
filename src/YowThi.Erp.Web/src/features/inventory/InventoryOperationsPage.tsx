import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useRef, useState } from 'react';

import { LocaleControl } from '../../app/i18n/LocaleControl';
import { useOperationalLocale } from '../../app/i18n/locale';
import {
  listInventoryAdjustmentIdentities,
  listInventoryAdjustmentLocations,
  listInventoryTransferDestinations,
  listInventoryTransferSources,
  type InventoryOperationIdentityOption,
} from './inventoryOperationOptions';
import {
  adjustInventory,
  ApiProblemError,
  transferInventory,
  type AdjustInventoryRequest,
  type InventoryPositionIdentityRequest,
  type TransferInventoryRequest,
} from './inventoryOperations';
import { inventoryOperationsCopy } from './inventoryOperationsCopy';

type OperationMode = 'transfer' | 'adjustment';
type MutationDraft =
  | { mode: 'transfer'; request: TransferInventoryRequest }
  | { mode: 'adjustment'; request: AdjustInventoryRequest };
type MutationInput = MutationDraft & { idempotencyKey: string };

interface MutationResult {
  mode: OperationMode;
  operationId: string;
  movement1: string;
  movement2: string | null;
  quantity: number;
}
interface SubmissionIdentity { fingerprint: string; idempotencyKey: string }

export function InventoryOperationsPage() {
  const { locale } = useOperationalLocale();
  const labels = inventoryOperationsCopy[locale];
  const queryClient = useQueryClient();
  const [mode, setMode] = useState<OperationMode>('transfer');
  const [search, setSearch] = useState('');
  const [selectedKey, setSelectedKey] = useState('');
  const [locationId, setLocationId] = useState('');
  const [quantityText, setQuantityText] = useState('');
  const [reasonText, setReasonText] = useState('');
  const [localError, setLocalError] = useState<string | null>(null);
  const deferredSearch = useDeferredValue(search);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);
  const numberFormat = useMemo(
    () => new Intl.NumberFormat(locale, { maximumFractionDigits: 6 }),
    [locale],
  );

  const transferSources = useQuery({
    queryKey: ['inventory-operation-options', 'transfer-sources', locale, deferredSearch],
    queryFn: ({ signal }) => listInventoryTransferSources({ locale, search: deferredSearch, limit: 50, signal }),
    enabled: mode === 'transfer',
    staleTime: 5_000,
  });
  const adjustmentIdentities = useQuery({
    queryKey: ['inventory-operation-options', 'adjustment-identities', locale, deferredSearch],
    queryFn: ({ signal }) => listInventoryAdjustmentIdentities({ locale, search: deferredSearch, limit: 50, signal }),
    enabled: mode === 'adjustment',
    staleTime: 5_000,
  });
  const transferLocations = useQuery({
    queryKey: ['inventory-operation-options', 'transfer-destinations', locale],
    queryFn: ({ signal }) => listInventoryTransferDestinations({ locale, limit: 100, signal }),
    enabled: mode === 'transfer',
    staleTime: 30_000,
  });
  const adjustmentLocations = useQuery({
    queryKey: ['inventory-operation-options', 'adjustment-locations', locale],
    queryFn: ({ signal }) => listInventoryAdjustmentLocations({ locale, limit: 100, signal }),
    enabled: mode === 'adjustment',
    staleTime: 30_000,
  });

  const selectedTransfer = mode === 'transfer'
    ? transferSources.data?.items.find((item) => item.inventoryPositionId === selectedKey) ?? null
    : null;
  const selectedAdjustment = mode === 'adjustment'
    ? adjustmentIdentities.data?.items.find((item) => identityKey(item) === selectedKey) ?? null
    : null;
  const locationItems = mode === 'transfer'
    ? transferLocations.data?.items ?? []
    : adjustmentLocations.data?.items ?? [];
  const selectedLocation = locationItems.find((item) => item.id === locationId) ?? null;

  const mutation = useMutation<MutationResult, Error, MutationInput>({
    mutationFn: async (input) => {
      if (input.mode === 'transfer') {
        const result = await transferInventory(input.request, { idempotencyKey: input.idempotencyKey, locale });
        return {
          mode: input.mode,
          operationId: result.inventoryOperationId,
          movement1: result.transferOutMovementId,
          movement2: result.transferInMovementId,
          quantity: result.quantity,
        };
      }
      const result = await adjustInventory(input.request, { idempotencyKey: input.idempotencyKey, locale });
      return {
        mode: input.mode,
        operationId: result.inventoryOperationId,
        movement1: result.adjustmentMovementId,
        movement2: null,
        quantity: result.quantityDelta,
      };
    },
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ['inventory-operation-options'] }),
  });

  const apiProblem = mutation.error instanceof ApiProblemError ? mutation.error : null;
  const problemMessage = useMemo(() => {
    if (apiProblem === null) return mutation.error ? labels.unexpected : null;
    switch (apiProblem.code) {
      case 'inventory.invalid-input': return labels.invalidInput;
      case 'inventory.position-not-found': return labels.positionMissing;
      case 'inventory.storage-location-not-found': return labels.locationMissing;
      case 'inventory.insufficient-stock': return labels.insufficient;
      case 'inventory.batch-closed': return labels.batchClosed;
      case 'inventory.concurrent-change': return labels.concurrent;
      case 'idempotency.key-reused': return labels.idempotency;
      default: return `${apiProblem.code}${apiProblem.problem.traceId ? ` · ${apiProblem.problem.traceId}` : ''}`;
    }
  }, [apiProblem, labels, mutation.error]);

  const mainQuery = mode === 'transfer' ? transferSources : adjustmentIdentities;
  const locationQuery = mode === 'transfer' ? transferLocations : adjustmentLocations;
  const queryError = mainQuery.error ?? locationQuery.error;
  const queryErrorMessage = queryError
    ? queryError instanceof ApiProblemError ? `${labels.queryFailed}: ${queryError.code}` : labels.queryFailed
    : null;

  function reset(nextMode?: OperationMode) {
    if (nextMode) setMode(nextMode);
    setSelectedKey('');
    setLocationId('');
    setQuantityText('');
    setReasonText('');
    setLocalError(null);
    submissionIdentity.current = null;
    mutation.reset();
  }

  function submit(draft: MutationDraft) {
    const fingerprint = JSON.stringify(draft);
    if (submissionIdentity.current?.fingerprint !== fingerprint) {
      submissionIdentity.current = { fingerprint, idempotencyKey: crypto.randomUUID() };
    }
    mutation.mutate({ ...draft, idempotencyKey: submissionIdentity.current.idempotencyKey });
  }

  function handleSubmit() {
    setLocalError(null);
    mutation.reset();
    if (mode === 'transfer') {
      if (!selectedTransfer || !selectedLocation) return setLocalError(labels.required);
      const quantity = Number(quantityText);
      if (!Number.isFinite(quantity) || quantity <= 0) return setLocalError(labels.invalidQuantity);
      if (quantity > selectedTransfer.balanceQuantity) return setLocalError(labels.exceedsBalance);
      if (selectedTransfer.sourceStorageLocationId === selectedLocation.id) return setLocalError(labels.sameLocation);
      submit({
        mode,
        request: {
          inventoryIdentity: toRequestIdentity(selectedTransfer.inventoryIdentity),
          sourceStorageLocationId: selectedTransfer.sourceStorageLocationId,
          destinationStorageLocationId: selectedLocation.id,
          quantity,
        },
      });
      return;
    }

    if (!selectedAdjustment || !selectedLocation) return setLocalError(labels.required);
    const quantityDelta = Number(quantityText);
    if (!Number.isFinite(quantityDelta) || quantityDelta === 0) return setLocalError(labels.invalidDelta);
    const reason = reasonText.trim();
    if (!reason) return setLocalError(labels.reasonRequired);
    submit({
      mode,
      request: {
        inventoryIdentity: toRequestIdentity(selectedAdjustment),
        storageLocationId: selectedLocation.id,
        quantityDelta,
        reasonText: reason,
      },
    });
  }

  const identity = selectedTransfer?.inventoryIdentity ?? selectedAdjustment;
  const sourceOptions = transferSources.data?.items ?? [];
  const adjustmentOptions = adjustmentIdentities.data?.items ?? [];

  return (
    <section className="procurement-page" aria-labelledby="inventory-operations-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="inventory-operations-title">{labels.title}</h1>
        </div>
        <LocaleControl />
      </header>

      <div className="procurement-grid">
        <div className="entry-form">
          <div className="field-grid">
            <button className="primary-action" type="button" disabled={mode === 'transfer'} onClick={() => reset('transfer')}>{labels.transfer}</button>
            <button className="primary-action" type="button" disabled={mode === 'adjustment'} onClick={() => reset('adjustment')}>{labels.adjustment}</button>

            <div className="option-picker full-width">
              <span className="field-label">{mode === 'transfer' ? labels.chooseSource : labels.chooseIdentity}</span>
              <input value={search} onChange={(event) => { setSearch(event.target.value); reset(); }} />
              <select value={selectedKey} onChange={(event) => { setSelectedKey(event.target.value); setLocationId(''); setQuantityText(''); setReasonText(''); submissionIdentity.current = null; mutation.reset(); }}>
                <option value="">{mainQuery.isPending ? labels.loading : mode === 'transfer' ? labels.chooseSource : labels.chooseIdentity}</option>
                {mode === 'transfer'
                  ? sourceOptions.map((item) => <option key={item.inventoryPositionId} value={item.inventoryPositionId}>{transferLabel(item, labels, numberFormat)}</option>)
                  : adjustmentOptions.map((item) => <option key={identityKey(item)} value={identityKey(item)}>{identityLabel(item, labels)}</option>)}
              </select>
            </div>

            <label className="full-width">
              <span>{mode === 'transfer' ? labels.destination : labels.adjustmentLocation}</span>
              <select value={locationId} onChange={(event) => { setLocationId(event.target.value); submissionIdentity.current = null; mutation.reset(); }}>
                <option value="">{locationQuery.isPending ? labels.loading : mode === 'transfer' ? labels.chooseDestination : labels.chooseLocation}</option>
                {locationItems.map((item) => <option key={item.id} value={item.id}>{item.displayName}{item.active ? '' : ` · ${labels.inactiveStatus}`}</option>)}
              </select>
            </label>

            <label className="full-width">
              <span>{mode === 'transfer' ? labels.quantity : labels.quantityDelta}</span>
              <input type="number" step="any" inputMode="decimal" value={quantityText} onChange={(event) => { setQuantityText(event.target.value); submissionIdentity.current = null; mutation.reset(); }} />
            </label>

            {mode === 'adjustment' && <label className="full-width"><span>{labels.reason}</span><textarea value={reasonText} onChange={(event) => { setReasonText(event.target.value); submissionIdentity.current = null; mutation.reset(); }} /></label>}
          </div>

          {identity && <dl>
            <ResultRow label={labels.batchDate} value={identity.batchDate} />
            <ResultRow label={labels.origin} value={originLabel(identity, labels)} />
            <ResultRow label={labels.objectKind} value={`${objectKindLabel(identity, labels)} · ${identity.objectDisplayName}`} />
            <ResultRow label={labels.rawSource} value={rawSourceLabel(identity, labels)} />
            {selectedTransfer && <ResultRow label={labels.sourceLocation} value={selectedTransfer.sourceStorageLocationDisplayName} />}
            {selectedTransfer && <ResultRow label={labels.balance} value={numberFormat.format(selectedTransfer.balanceQuantity)} />}
          </dl>}
          {selectedTransfer && !selectedTransfer.sourceStorageLocationActive && <div className="problem-banner" role="status">{labels.inactiveSource}</div>}
          {mode === 'adjustment' && selectedLocation && !selectedLocation.active && <div className="problem-banner" role="status">{labels.inactiveAdjustmentLocation}</div>}
          {(localError ?? problemMessage ?? queryErrorMessage) && <div className="problem-banner" role="alert">{localError ?? problemMessage ?? queryErrorMessage}</div>}
          <button className="primary-action" type="button" onClick={handleSubmit} disabled={mutation.isPending}>{mutation.isPending ? labels.submitting : mode === 'transfer' ? labels.submitTransfer : labels.submitAdjustment}</button>
        </div>

        <aside className="result-panel" aria-live="polite">
          {mutation.data ? <><p className="eyebrow">{mutation.data.mode === 'transfer' ? labels.transferSuccess : labels.adjustmentSuccess}</p><dl>
            <ResultRow label={labels.operationId} value={mutation.data.operationId} />
            <ResultRow label={labels.movement1} value={mutation.data.movement1} />
            {mutation.data.movement2 && <ResultRow label={labels.movement2} value={mutation.data.movement2} />}
            <ResultRow label={labels.resultQuantity} value={numberFormat.format(mutation.data.quantity)} />
          </dl></> : <div className="result-placeholder" aria-hidden="true"><span>YowThi ERP V2</span></div>}
        </aside>
      </div>
    </section>
  );
}

function toRequestIdentity(item: InventoryOperationIdentityOption): InventoryPositionIdentityRequest {
  return {
    origin: item.origin,
    procurementBatchId: item.procurementBatchId,
    outsourcedSupplyBatchId: item.outsourcedSupplyBatchId,
    inventoryObjectKind: item.inventoryObjectKind,
    procurementProductId: item.procurementProductId,
    processMaterialId: item.processMaterialId,
    salesProductId: item.salesProductId,
    rawSourceKind: item.rawSourceKind,
    supplierId: item.supplierId,
  };
}
function identityKey(item: InventoryOperationIdentityOption): string {
  return [item.origin, item.procurementBatchId ?? '', item.outsourcedSupplyBatchId ?? '', item.inventoryObjectKind, item.procurementProductId ?? '', item.processMaterialId ?? '', item.salesProductId ?? '', item.rawSourceKind ?? '', item.supplierId ?? ''].join('|');
}
type InventoryLabels = typeof inventoryOperationsCopy['zh-TW'] | typeof inventoryOperationsCopy['th-TH'];
function rawSourceLabel(item: InventoryOperationIdentityOption, labels: InventoryLabels): string {
  if (item.rawSourceKind === 'FARMERS_COMBINED') return labels.farmersCombined;
  if (item.rawSourceKind === 'SUPPLIER') return item.rawSourceDisplayName ?? labels.supplierSource;
  return '—';
}
function originLabel(item: InventoryOperationIdentityOption, labels: InventoryLabels): string {
  return item.origin === 'IN_HOUSE' ? labels.inHouseOrigin : labels.outsourcedOrigin;
}
function objectKindLabel(item: InventoryOperationIdentityOption, labels: InventoryLabels): string {
  switch (item.inventoryObjectKind) {
    case 'PROCUREMENT_PRODUCT': return labels.procurementProductKind;
    case 'PROCESS_MATERIAL': return labels.processMaterialKind;
    case 'SALES_PRODUCT': return labels.salesProductKind;
  }
}
function identityLabel(item: InventoryOperationIdentityOption, labels: InventoryLabels): string {
  return `${item.batchDate} · ${item.objectDisplayName} · ${rawSourceLabel(item, labels)} · ${originLabel(item, labels)}`;
}
function transferLabel(item: { inventoryIdentity: InventoryOperationIdentityOption; sourceStorageLocationDisplayName: string; balanceQuantity: number }, labels: InventoryLabels, format: Intl.NumberFormat): string {
  return `${identityLabel(item.inventoryIdentity, labels)} · ${item.sourceStorageLocationDisplayName} · ${format.format(item.balanceQuantity)}`;
}

function ResultRow({ label, value }: { label: string; value: string }) {
  return <div className="result-row"><dt>{label}</dt><dd>{value}</dd></div>;
}
