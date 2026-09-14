import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router';

import { ApiProblemError } from '../../app/api/apiTransport';
import { type OperationalLocale, useOperationalLocale } from '../../app/i18n/locale';
import { getAuthenticationSession, prepareDeletionReauthentication } from '../../app/security/authSession';
import {
  getProcessingWorkspace,
  listProcessingWorkspace,
  type ProcessingWorkspace,
  type ProcessingWorkspaceInput,
  type ProcessingWorkspaceListItem,
  type ProcessingWorkspaceOutput,
  type ProcessingWorkspaceStatusFilter,
} from './processingWorkspace';
import {
  changeProcessingLifecycle,
  type ProcessingLifecycleAction,
  type ProcessingLifecycleTarget,
} from './processingTransactionLifecycle';
import './ProcessingWorkspacePage.css';

type LifecycleInput = {
  target: ProcessingLifecycleTarget;
  id: string;
  action: ProcessingLifecycleAction;
  rowVersion: number;
};

const copy = {
  'zh-TW': {
    eyebrow: 'P8 · 加工作業',
    title: '加工執行工作區',
    newExecution: '新增加工執行',
    search: '搜尋執行',
    searchLabel: '員工、採購產品或加工模組',
    status: '狀態',
    all: '全部',
    active: '有效',
    deleted: '已刪除',
    loading: '載入加工執行中…',
    empty: '目前沒有符合條件的加工執行。',
    listFailed: '無法載入加工執行列表。',
    detailFailed: '無法載入加工執行內容。',
    selectExecution: '選擇一筆加工執行查看內容。',
    workDate: '工作日期',
    employee: '員工',
    procurement: '採購來源',
    module: '加工模組',
    executionMode: '執行模式',
    source: '原料來源',
    supplier: '供應商',
    recordedAt: '記錄時間',
    input: '投入',
    outputs: '產出',
    noInput: '此執行沒有投入明細。',
    noOutputs: '此執行沒有產出明細。',
    consumptionBasis: '消耗依據',
    consumedQuantity: '消耗數量',
    inventoryObject: '庫存對象',
    storageLocation: '儲位',
    scaleReading: '秤重讀值',
    containerCount: '實際容器數',
    outputKind: '產出種類',
    target: '產出目標',
    quantity: '產出數量',
    sourceConsumption: '來源消耗',
    wageRate: '工資率',
    sourceTracked: '來源追蹤',
    pooledOutput: '依產出量回推消耗',
    finalPackaging: '最終包裝',
    supplierSource: '供應商',
    farmersCombined: '農戶合併',
    processMaterial: '加工物料',
    salesProduct: '銷售產品',
    procurementProduct: '採購產品',
    scaleNet: '淨重',
    outputQuantity: '產出量',
    packagingWeight: '包裝重量',
    actions: '操作',
    softDelete: '刪除',
    restore: '還原',
    hardDelete: '永久刪除',
    loadMore: '載入更多',
    loadingMore: '載入更多中…',
    stale: '資料已更新，請重新載入。',
    notFound: '資料已不存在，請重新載入。',
    dependency: '相關資料仍有依賴，無法執行永久刪除。',
    reauth: '刪除前需要重新驗證登入身分。',
    unexpected: '操作失敗。',
    none: '—',
  },
  'th-TH': {
    eyebrow: 'P8 · การแปรรูป',
    title: 'พื้นที่ทำงานการแปรรูป',
    newExecution: 'บันทึกการแปรรูปใหม่',
    search: 'ค้นหารายการ',
    searchLabel: 'พนักงาน สินค้าจัดซื้อ หรือโมดูลแปรรูป',
    status: 'สถานะ',
    all: 'ทั้งหมด',
    active: 'ใช้งาน',
    deleted: 'ลบแล้ว',
    loading: 'กำลังโหลดรายการแปรรูป…',
    empty: 'ไม่พบรายการแปรรูปที่ตรงกับเงื่อนไข',
    listFailed: 'โหลดรายการแปรรูปไม่สำเร็จ',
    detailFailed: 'โหลดรายละเอียดการแปรรูปไม่สำเร็จ',
    selectExecution: 'เลือกรายการแปรรูปเพื่อดูรายละเอียด',
    workDate: 'วันที่ทำงาน',
    employee: 'พนักงาน',
    procurement: 'แหล่งจัดซื้อ',
    module: 'โมดูลแปรรูป',
    executionMode: 'โหมดการทำงาน',
    source: 'แหล่งวัตถุดิบ',
    supplier: 'ผู้จำหน่าย',
    recordedAt: 'เวลาบันทึก',
    input: 'วัตถุดิบเข้า',
    outputs: 'ผลผลิต',
    noInput: 'รายการนี้ไม่มีรายละเอียดวัตถุดิบเข้า',
    noOutputs: 'รายการนี้ไม่มีรายละเอียดผลผลิต',
    consumptionBasis: 'เกณฑ์การใช้',
    consumedQuantity: 'ปริมาณที่ใช้',
    inventoryObject: 'รายการสต็อก',
    storageLocation: 'ตำแหน่งจัดเก็บ',
    scaleReading: 'ค่าน้ำหนัก',
    containerCount: 'จำนวนภาชนะจริง',
    outputKind: 'ชนิดผลผลิต',
    target: 'เป้าหมายผลผลิต',
    quantity: 'ปริมาณผลผลิต',
    sourceConsumption: 'ปริมาณต้นทางที่ใช้',
    wageRate: 'อัตราค่าจ้าง',
    sourceTracked: 'ติดตามแหล่งวัตถุดิบ',
    pooledOutput: 'คำนวณการใช้จากผลผลิต',
    finalPackaging: 'บรรจุขั้นสุดท้าย',
    supplierSource: 'ผู้จำหน่าย',
    farmersCombined: 'เกษตรกรรวม',
    processMaterial: 'วัตถุดิบระหว่างกระบวนการ',
    salesProduct: 'สินค้าขาย',
    procurementProduct: 'สินค้าจัดซื้อ',
    scaleNet: 'น้ำหนักสุทธิ',
    outputQuantity: 'ปริมาณผลผลิต',
    packagingWeight: 'น้ำหนักบรรจุภัณฑ์',
    actions: 'จัดการ',
    softDelete: 'ลบ',
    restore: 'กู้คืน',
    hardDelete: 'ลบถาวร',
    loadMore: 'โหลดเพิ่มเติม',
    loadingMore: 'กำลังโหลดเพิ่มเติม…',
    stale: 'ข้อมูลถูกเปลี่ยนแล้ว โปรดโหลดใหม่',
    notFound: 'ไม่พบข้อมูลแล้ว โปรดโหลดใหม่',
    dependency: 'ยังมีข้อมูลที่เกี่ยวข้อง จึงไม่สามารถลบถาวรได้',
    reauth: 'ต้องยืนยันตัวตนอีกครั้งก่อนลบ',
    unexpected: 'ดำเนินการไม่สำเร็จ',
    none: '—',
  },
} as const;

export function ProcessingWorkspacePage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<ProcessingWorkspaceStatusFilter>('all');
  const [searchParams, setSearchParams] = useSearchParams();
  const deferredSearch = useDeferredValue(search);

  const sessionQuery = useQuery({
    queryKey: ['authentication-session'],
    queryFn: ({ signal }) => getAuthenticationSession(signal),
    staleTime: 30_000,
  });
  const canHardDelete = sessionQuery.data?.capabilities.includes('data-protection.hard-delete') ?? false;

  const listQuery = useInfiniteQuery({
    queryKey: ['processing-workspace', 'list', locale, deferredSearch, status],
    initialPageParam: 0,
    queryFn: ({ signal, pageParam }) => listProcessingWorkspace({
      locale,
      search: deferredSearch,
      status,
      offset: pageParam,
      limit: 100,
      signal,
    }),
    getNextPageParam: (lastPage) => lastPage.nextOffset ?? undefined,
    staleTime: 15_000,
  });

  const items = useMemo(
    () => listQuery.data?.pages.flatMap((page) => page.items) ?? [],
    [listQuery.data?.pages],
  );

  const requestedExecutionId = searchParams.get('execution');
  const effectiveExecutionId = requestedExecutionId !== null
    && items.some((item) => item.id === requestedExecutionId)
    ? requestedExecutionId
    : items[0]?.id ?? null;

  const detailQuery = useQuery({
    queryKey: ['processing-workspace', 'detail', effectiveExecutionId, locale],
    queryFn: ({ signal }) => getProcessingWorkspace(effectiveExecutionId!, locale, signal),
    enabled: effectiveExecutionId !== null,
    staleTime: 15_000,
  });

  const lifecycleMutation = useMutation({
    mutationFn: async (input: LifecycleInput) => {
      if (input.action !== 'restore') await prepareDeletionReauthentication();
      return changeProcessingLifecycle(
        input.target,
        input.id,
        input.action,
        { expectedRowVersion: input.rowVersion },
        { idempotencyKey: crypto.randomUUID(), locale },
      );
    },
    onSuccess: async (_, input) => {
      if (input.target === 'execution' && input.action === 'hard-delete') {
        const next = new URLSearchParams(searchParams);
        next.delete('execution');
        setSearchParams(next, { replace: true });
      }
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['processing-workspace', 'list'] }),
        queryClient.invalidateQueries({ queryKey: ['processing-workspace', 'detail'] }),
      ]);
    },
  });

  const lifecycleProblem = useMemo(() => {
    const error = lifecycleMutation.error;
    if (!error) return null;
    if (!(error instanceof ApiProblemError)) return labels.unexpected;
    if (error.code === 'concurrency.stale-row-version') return labels.stale;
    if (error.code.endsWith('-not-found') || error.code === 'resource.not-found') return labels.notFound;
    if (error.code.endsWith('-dependency-blocked') || error.code.endsWith('-closure-invalid') || error.code.endsWith('-closure-ambiguous')) return labels.dependency;
    if (error.code === 'security.deletion-reauth-required') return labels.reauth;
    return error.code;
  }, [labels, lifecycleMutation.error]);

  function selectExecution(executionId: string) {
    const next = new URLSearchParams(searchParams);
    next.set('execution', executionId);
    setSearchParams(next, { replace: true });
    lifecycleMutation.reset();
  }

  return (
    <section className="processing-workspace-page" aria-labelledby="processing-workspace-title">
      <header className="processing-workspace-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="processing-workspace-title">{labels.title}</h1>
        </div>
        <Link className="processing-workspace-create" to="/processing/executions/new">
          {labels.newExecution}
        </Link>
      </header>

      <div className="processing-workspace-layout">
        <aside className="processing-workspace-list-panel" aria-label={labels.title}>
          <div className="processing-workspace-filters">
            <label>
              <span>{labels.search}</span>
              <input
                type="search"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                aria-label={labels.searchLabel}
              />
            </label>
            <label>
              <span>{labels.status}</span>
              <select value={status} onChange={(event) => setStatus(event.target.value as ProcessingWorkspaceStatusFilter)}>
                <option value="all">{labels.all}</option>
                <option value="active">{labels.active}</option>
                <option value="deleted">{labels.deleted}</option>
              </select>
            </label>
          </div>

          {listQuery.isPending && <p className="processing-workspace-state">{labels.loading}</p>}
          {listQuery.isError && <p className="problem-banner" role="alert">{labels.listFailed}</p>}
          {!listQuery.isPending && !listQuery.isError && items.length === 0 && (
            <p className="processing-workspace-state">{labels.empty}</p>
          )}

          <div className="processing-workspace-list">
            {items.map((item) => (
              <ExecutionListItem
                key={item.id}
                item={item}
                locale={locale}
                selected={item.id === effectiveExecutionId}
                labels={labels}
                onSelect={() => selectExecution(item.id)}
              />
            ))}
          </div>
          {listQuery.hasNextPage && (
            <button
              type="button"
              className="processing-workspace-load-more"
              disabled={listQuery.isFetchingNextPage}
              onClick={() => void listQuery.fetchNextPage()}
            >
              {listQuery.isFetchingNextPage ? labels.loadingMore : labels.loadMore}
            </button>
          )}
        </aside>

        <main className="processing-workspace-detail-panel">
          {lifecycleProblem && <p className="problem-banner" role="alert">{lifecycleProblem}</p>}
          {effectiveExecutionId === null && !listQuery.isPending && (
            <p className="processing-workspace-state">{labels.selectExecution}</p>
          )}
          {detailQuery.isPending && effectiveExecutionId !== null && (
            <p className="processing-workspace-state">{labels.loading}</p>
          )}
          {detailQuery.isError && <p className="problem-banner" role="alert">{labels.detailFailed}</p>}
          {detailQuery.data && (
            <ProcessingDocument
              workspace={detailQuery.data}
              locale={locale}
              labels={labels}
              canHardDelete={canHardDelete}
              lifecyclePending={lifecycleMutation.isPending}
              onLifecycle={(input) => lifecycleMutation.mutate(input)}
            />
          )}
        </main>
      </div>
    </section>
  );
}

function ExecutionListItem({
  item,
  locale,
  selected,
  labels,
  onSelect,
}: {
  item: ProcessingWorkspaceListItem;
  locale: OperationalLocale;
  selected: boolean;
  labels: typeof copy[OperationalLocale];
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      className={`processing-workspace-list-item${selected ? ' is-selected' : ''}`}
      onClick={onSelect}
    >
      <span className="processing-workspace-list-date">{item.workDate}</span>
      <strong>{item.procurementProductDisplayName}</strong>
      <span>{item.employeeDisplayName} · {item.processingModuleDisplayName}</span>
      <small>
        {executionModeLabel(item.executionMode, labels)} · {item.deletedAt === null ? labels.active : labels.deleted}
        {' · '}{formatDateTime(locale, item.recordedAt)}
      </small>
    </button>
  );
}

function ProcessingDocument({
  workspace,
  locale,
  labels,
  canHardDelete,
  lifecyclePending,
  onLifecycle,
}: {
  workspace: ProcessingWorkspace;
  locale: OperationalLocale;
  labels: typeof copy[OperationalLocale];
  canHardDelete: boolean;
  lifecyclePending: boolean;
  onLifecycle: (input: LifecycleInput) => void;
}) {
  return (
    <article className={`processing-workspace-document${workspace.deletedAt ? ' is-deleted' : ''}`}>
      <div className="processing-workspace-document-actions">
        <LifecycleButtons
          deleted={workspace.deletedAt !== null}
          pending={lifecyclePending}
          labels={labels}
          canHardDelete={canHardDelete}
          onSoft={() => onLifecycle({ target: 'execution', id: workspace.id, action: 'soft-delete', rowVersion: workspace.rowVersion })}
          onRestore={() => onLifecycle({ target: 'execution', id: workspace.id, action: 'restore', rowVersion: workspace.rowVersion })}
          onHard={() => onLifecycle({ target: 'execution', id: workspace.id, action: 'hard-delete', rowVersion: workspace.rowVersion })}
        />
      </div>
      <header className="processing-workspace-document-header">
        <Fact label={labels.workDate} value={workspace.workDate} />
        <Fact label={labels.employee} value={workspace.employeeDisplayName} />
        <Fact
          label={labels.procurement}
          value={`${workspace.procurementDate} · ${workspace.procurementProductDisplayName}`}
        />
        <Fact label={labels.module} value={workspace.processingModuleDisplayName} />
        <Fact label={labels.executionMode} value={executionModeLabel(workspace.executionMode, labels)} />
        <Fact label={labels.source} value={sourceKindLabel(workspace.sourceKind, labels)} />
        <Fact label={labels.supplier} value={workspace.supplierDisplayName ?? labels.none} />
        <Fact label={labels.recordedAt} value={formatDateTime(locale, workspace.recordedAt)} />
      </header>

      <section className="processing-workspace-section">
        <h2>{labels.input}</h2>
        {workspace.input === null
          ? <p className="processing-workspace-state">{labels.noInput}</p>
          : (
            <ProcessingInputDetail
              input={workspace.input}
              locale={locale}
              labels={labels}
              canHardDelete={canHardDelete}
              lifecyclePending={lifecyclePending}
              onLifecycle={onLifecycle}
            />
          )}
      </section>

      <section className="processing-workspace-section">
        <h2>{labels.outputs}</h2>
        {workspace.outputs.length === 0
          ? <p className="processing-workspace-state">{labels.noOutputs}</p>
          : (
            <div className="processing-workspace-output-grid">
              {workspace.outputs.map((output) => (
                <ProcessingOutputDetail
                  key={output.id}
                  output={output}
                  locale={locale}
                  labels={labels}
                  canHardDelete={canHardDelete}
                  lifecyclePending={lifecyclePending}
                  onLifecycle={onLifecycle}
                />
              ))}
            </div>
          )}
      </section>
    </article>
  );
}

function ProcessingInputDetail({
  input,
  locale,
  labels,
  canHardDelete,
  lifecyclePending,
  onLifecycle,
}: {
  input: ProcessingWorkspaceInput;
  locale: OperationalLocale;
  labels: typeof copy[OperationalLocale];
  canHardDelete: boolean;
  lifecyclePending: boolean;
  onLifecycle: (input: LifecycleInput) => void;
}) {
  return (
    <div className={`processing-workspace-lifecycle-target${input.deletedAt ? ' is-deleted' : ''}`}>
      <LifecycleButtons
        deleted={input.deletedAt !== null}
        pending={lifecyclePending}
        labels={labels}
        canHardDelete={canHardDelete}
        onSoft={() => onLifecycle({ target: 'input', id: input.processingExecutionId, action: 'soft-delete', rowVersion: input.rowVersion })}
        onRestore={() => onLifecycle({ target: 'input', id: input.processingExecutionId, action: 'restore', rowVersion: input.rowVersion })}
        onHard={() => onLifecycle({ target: 'input', id: input.processingExecutionId, action: 'hard-delete', rowVersion: input.rowVersion })}
      />
      <dl className="processing-workspace-facts">
        <FactRow label={labels.consumptionBasis} value={consumptionBasisLabel(input.consumptionBasis, labels)} />
        <FactRow label={labels.consumedQuantity} value={formatNumber(locale, input.consumedQuantity)} />
        <FactRow
          label={labels.inventoryObject}
          value={input.inventoryObjectDisplayName ?? inventoryObjectKindLabel(input.inventoryObjectKind, labels)}
        />
        <FactRow label={labels.storageLocation} value={input.storageLocationDisplayName ?? labels.none} />
        <FactRow label={labels.scaleReading} value={formatOptionalNumber(locale, input.observedScaleReading, labels.none)} />
        <FactRow label={labels.containerCount} value={formatOptionalNumber(locale, input.actualContainerCount, labels.none)} />
      </dl>
    </div>
  );
}

function ProcessingOutputDetail({
  output,
  locale,
  labels,
  canHardDelete,
  lifecyclePending,
  onLifecycle,
}: {
  output: ProcessingWorkspaceOutput;
  locale: OperationalLocale;
  labels: typeof copy[OperationalLocale];
  canHardDelete: boolean;
  lifecyclePending: boolean;
  onLifecycle: (input: LifecycleInput) => void;
}) {
  const quantity = output.derivedNetQuantity ?? output.completedQuantity;

  return (
    <article className={`processing-workspace-output-card${output.deletedAt ? ' is-deleted' : ''}`}>
      <header>
        <span>{output.outputSequence}</span>
        <strong>{output.targetDisplayName ?? labels.none}</strong>
      </header>
      <LifecycleButtons
        deleted={output.deletedAt !== null}
        pending={lifecyclePending}
        labels={labels}
        canHardDelete={canHardDelete}
        onSoft={() => onLifecycle({ target: 'output', id: output.id, action: 'soft-delete', rowVersion: output.rowVersion })}
        onRestore={() => onLifecycle({ target: 'output', id: output.id, action: 'restore', rowVersion: output.rowVersion })}
        onHard={() => onLifecycle({ target: 'output', id: output.id, action: 'hard-delete', rowVersion: output.rowVersion })}
      />
      <dl className="processing-workspace-facts">
        <FactRow label={labels.outputKind} value={outputKindLabel(output.outputKind, labels)} />
        <FactRow label={labels.target} value={output.targetDisplayName ?? labels.none} />
        <FactRow label={labels.quantity} value={formatOptionalNumber(locale, quantity, labels.none)} />
        <FactRow
          label={labels.sourceConsumption}
          value={formatOptionalNumber(locale, output.sourceConsumptionQuantity, labels.none)}
        />
        <FactRow label={labels.storageLocation} value={output.storageLocationDisplayName ?? labels.none} />
        <FactRow label={labels.wageRate} value={formatNumber(locale, output.configuredWageRate)} />
      </dl>
    </article>
  );
}

function LifecycleButtons({
  deleted,
  pending,
  labels,
  canHardDelete,
  onSoft,
  onRestore,
  onHard,
}: {
  deleted: boolean;
  pending: boolean;
  labels: typeof copy[OperationalLocale];
  canHardDelete: boolean;
  onSoft: () => void;
  onRestore: () => void;
  onHard: () => void;
}) {
  return (
    <span className="processing-workspace-lifecycle-actions" aria-label={labels.actions}>
      {deleted
        ? <button type="button" disabled={pending} onClick={onRestore}>{labels.restore}</button>
        : <button type="button" disabled={pending} onClick={onSoft}>{labels.softDelete}</button>}
      {deleted && canHardDelete && (
        <button type="button" className="is-danger" disabled={pending} onClick={onHard}>{labels.hardDelete}</button>
      )}
    </span>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="processing-workspace-header-fact">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function FactRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="processing-workspace-fact-row">
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function executionModeLabel(mode: string, labels: typeof copy[OperationalLocale]): string {
  switch (mode) {
    case 'SOURCE_TRACKED': return labels.sourceTracked;
    case 'POOLED_OUTPUT': return labels.pooledOutput;
    case 'FINAL_PACKAGING': return labels.finalPackaging;
    default: return mode;
  }
}

function sourceKindLabel(sourceKind: string | null, labels: typeof copy[OperationalLocale]): string {
  switch (sourceKind) {
    case 'SUPPLIER': return labels.supplierSource;
    case 'FARMERS_COMBINED': return labels.farmersCombined;
    case null: return labels.none;
    default: return sourceKind;
  }
}

function outputKindLabel(outputKind: string, labels: typeof copy[OperationalLocale]): string {
  switch (outputKind) {
    case 'PROCESS_MATERIAL': return labels.processMaterial;
    case 'SALES_PRODUCT': return labels.salesProduct;
    default: return outputKind;
  }
}

function inventoryObjectKindLabel(kind: string | null, labels: typeof copy[OperationalLocale]): string {
  switch (kind) {
    case 'PROCUREMENT_PRODUCT': return labels.procurementProduct;
    case 'PROCESS_MATERIAL': return labels.processMaterial;
    case 'SALES_PRODUCT': return labels.salesProduct;
    case null: return labels.none;
    default: return kind;
  }
}

function consumptionBasisLabel(basis: string, labels: typeof copy[OperationalLocale]): string {
  switch (basis) {
    case 'SCALE_NET': return labels.scaleNet;
    case 'OUTPUT_QUANTITY': return labels.outputQuantity;
    case 'PACKAGING_WEIGHT': return labels.packagingWeight;
    default: return basis;
  }
}

function formatNumber(locale: OperationalLocale, value: number): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits: 6 }).format(value);
}

function formatOptionalNumber(locale: OperationalLocale, value: number | null, fallback: string): string {
  return value === null ? fallback : formatNumber(locale, value);
}

function formatDateTime(locale: OperationalLocale, value: string): string {
  return new Intl.DateTimeFormat(locale, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value));
}
