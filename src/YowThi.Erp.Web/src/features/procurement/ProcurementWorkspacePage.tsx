import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useState } from 'react';

import { ApiProblemError } from '../../app/api/apiTransport';
import { SearchableSelect } from '../../app/forms/SearchableSelect';
import { useOperationalLocale } from '../../app/i18n/locale';
import { getAuthenticationSession, prepareDeletionReauthentication } from '../../app/security/authSession';
import { ProcurementDetailForm } from './ProcurementDetailForm';
import {
  listProcurementProductOptions,
  type ProcurementProductOption,
} from './procurementEntryOptions';
import {
  changeProcurementLifecycle,
  type ProcurementLifecycleAction,
  type ProcurementLifecycleTarget,
} from './procurementTransactionLifecycle';
import {
  getProcurementBatchWorkspace,
  listProcurementBatches,
  type ProcurementBatchEntry,
  type ProcurementBatchWorkspace,
} from './procurementWorkspace';
import './ProcurementWorkspacePage.css';

const copy = {
  'zh-TW': {
    title: '採購管理', newProcurement: '新增採購', cancelNew: '取消新增', addDetail: '新增明細', batchList: '採購主單',
    search: '搜尋日期或產品', loading: '載入中', unavailable: '資料載入失敗', empty: '尚無採購主單', header: '主單',
    date: '日期', product: '產品', productSearch: '搜尋採購產品', productSelect: '選擇採購產品', status: '採購狀態', lifecycle: '主單狀態',
    details: '採購明細', noDetails: '尚無採購明細', code: '編號', source: '供應商', quantity: '數量', unitPrice: '單價', amount: '金額',
    receiptLocation: '收貨儲位', pickup: '取貨', recordedAt: '登錄時間', actions: '操作', companyPickup: '公司取貨', otherPickup: '非公司取貨',
    open: '開放', completed: '已完成', active: '啟用', closed: '已關閉', deleted: '已刪除', softDelete: '刪除', restore: '還原', hardDelete: '永久刪除',
    stale: '資料已更新，請重新載入。', notFound: '資料已不存在，請重新載入。', dependency: '相關資料無法安全關閉，未執行刪除。', reauth: '刪除前需要重新驗證登入身分。', unexpected: '操作失敗。',
  },
  'th-TH': {
    title: 'จัดซื้อ', newProcurement: 'เพิ่มการจัดซื้อ', cancelNew: 'ยกเลิกการเพิ่ม', addDetail: 'เพิ่มรายการ', batchList: 'เอกสารจัดซื้อ',
    search: 'ค้นหาวันที่หรือสินค้า', loading: 'กำลังโหลด', unavailable: 'โหลดข้อมูลไม่สำเร็จ', empty: 'ยังไม่มีเอกสารจัดซื้อ', header: 'เอกสารหลัก',
    date: 'วันที่', product: 'สินค้า', productSearch: 'ค้นหาสินค้าจัดซื้อ', productSelect: 'เลือกสินค้าจัดซื้อ', status: 'สถานะจัดซื้อ', lifecycle: 'สถานะเอกสาร',
    details: 'รายการจัดซื้อ', noDetails: 'ยังไม่มีรายการจัดซื้อ', code: 'รหัส', source: 'ผู้จำหน่าย', quantity: 'ปริมาณ', unitPrice: 'ราคาต่อหน่วย', amount: 'จำนวนเงิน',
    receiptLocation: 'ตำแหน่งรับสินค้า', pickup: 'การรับสินค้า', recordedAt: 'เวลาบันทึก', actions: 'จัดการ', companyPickup: 'บริษัทรับสินค้า', otherPickup: 'ไม่ใช่บริษัทรับสินค้า',
    open: 'เปิด', completed: 'เสร็จสมบูรณ์', active: 'ใช้งาน', closed: 'ปิด', deleted: 'ลบแล้ว', softDelete: 'ลบ', restore: 'กู้คืน', hardDelete: 'ลบถาวร',
    stale: 'ข้อมูลถูกเปลี่ยนแล้ว โปรดโหลดใหม่', notFound: 'ไม่พบข้อมูลแล้ว โปรดโหลดใหม่', dependency: 'ไม่สามารถปิดข้อมูลที่เกี่ยวข้องได้อย่างปลอดภัย จึงยังไม่ได้ลบ', reauth: 'ต้องยืนยันตัวตนอีกครั้งก่อนลบ', unexpected: 'ดำเนินการไม่สำเร็จ',
  },
} as const;

type WorkspaceLabels = (typeof copy)[keyof typeof copy];
type LifecycleInput = { target: ProcurementLifecycleTarget; id: string; action: ProcurementLifecycleAction; rowVersion: number };

export function ProcurementWorkspacePage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const queryClient = useQueryClient();
  const sessionQuery = useQuery({
    queryKey: ['authentication-session'],
    queryFn: ({ signal }) => getAuthenticationSession(signal),
    staleTime: 30_000,
  });
  const canHardDelete = sessionQuery.data?.capabilities.includes('data-protection.hard-delete') ?? false;
  const [search, setSearch] = useState('');
  const deferredSearch = useDeferredValue(search);
  const [selectedBatchId, setSelectedBatchId] = useState<string | null>(null);
  const [creatingHeader, setCreatingHeader] = useState(false);
  const [draftDate, setDraftDate] = useState(todayValue());
  const [draftProductSearch, setDraftProductSearch] = useState('');
  const [draftProduct, setDraftProduct] = useState<ProcurementProductOption | null>(null);
  const [detailEditorOpen, setDetailEditorOpen] = useState(false);

  const batchQuery = useQuery({
    queryKey: ['procurement-workspace', 'batches', locale, deferredSearch],
    queryFn: ({ signal }) => listProcurementBatches({ locale, search: deferredSearch, offset: 0, limit: 100, signal }),
    staleTime: 2_000,
  });
  const productQuery = useQuery({
    queryKey: ['procurement-workspace', 'new-header-products', locale, draftProductSearch],
    queryFn: ({ signal }) => listProcurementProductOptions({ locale, search: draftProductSearch, limit: 50, signal }),
    enabled: creatingHeader,
    staleTime: 10_000,
  });

  const batches = batchQuery.data?.items ?? [];
  const productItems = includeSelected(productQuery.data?.items ?? [], draftProduct);
  const effectiveSelectedBatchId = creatingHeader ? null : selectedBatchId ?? batches[0]?.id ?? null;
  const workspaceQuery = useQuery({
    queryKey: ['procurement-workspace', 'batch', effectiveSelectedBatchId, locale],
    queryFn: ({ signal }) => getProcurementBatchWorkspace(effectiveSelectedBatchId!, locale, signal),
    enabled: effectiveSelectedBatchId !== null,
    staleTime: 1_000,
  });

  const lifecycleMutation = useMutation({
    mutationFn: async (input: LifecycleInput) => {
      if (input.action !== 'restore') await prepareDeletionReauthentication();
      return changeProcurementLifecycle(
        input.target,
        input.id,
        input.action,
        { expectedRowVersion: input.rowVersion },
        { idempotencyKey: crypto.randomUUID(), locale },
      );
    },
    onSuccess: async (_, input) => {
      setDetailEditorOpen(false);
      if (input.target === 'batch' && input.action === 'hard-delete') setSelectedBatchId(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['procurement-workspace', 'batches'] }),
        queryClient.invalidateQueries({ queryKey: ['procurement-workspace', 'batch'] }),
        queryClient.invalidateQueries({ queryKey: ['hard-delete-options', input.target === 'batch' ? 'procurement-batches' : 'procurement-entries'] }),
      ]);
    },
  });

  const lifecycleProblem = useMemo(() => {
    const error = lifecycleMutation.error;
    if (!error) return null;
    if (!(error instanceof ApiProblemError)) return labels.unexpected;
    if (error.code === 'concurrency.stale-row-version') return labels.stale;
    if (error.code.endsWith('-not-found')) return labels.notFound;
    if (error.code.endsWith('-dependency-blocked') || error.code.endsWith('-closure-invalid') || error.code.endsWith('-closure-ambiguous')) return labels.dependency;
    if (error.code === 'security.deletion-reauth-required') return labels.reauth;
    return error.code;
  }, [labels, lifecycleMutation.error]);

  const existingWorkspace = workspaceQuery.data ?? null;
  const detailHeader = creatingHeader
    ? draftProduct && draftDate ? { procurementDate: draftDate, procurementProductId: draftProduct.id, procurementProductDisplayName: draftProduct.displayName, unitCode: draftProduct.unitCode } : null
    : existingWorkspace ? { procurementDate: existingWorkspace.procurementDate, procurementProductId: existingWorkspace.procurementProductId, procurementProductDisplayName: existingWorkspace.procurementProductDisplayName, unitCode: existingWorkspace.unitCode } : null;
  const existingCanAddDetail = existingWorkspace !== null && existingWorkspace.procurementStatus === 'OPEN' && existingWorkspace.lifecycleStatus === 'ACTIVE' && existingWorkspace.deletedAt === null;
  const canAddDetail = creatingHeader ? detailHeader !== null : existingCanAddDetail;

  function startNewProcurement() {
    setCreatingHeader(true); setSelectedBatchId(null); setDraftDate(todayValue()); setDraftProduct(null); setDraftProductSearch(''); setDetailEditorOpen(false); lifecycleMutation.reset();
  }
  function cancelNewProcurement() {
    setCreatingHeader(false); setDetailEditorOpen(false); setSelectedBatchId(batches[0]?.id ?? null);
  }
  function selectBatch(batchId: string) {
    setCreatingHeader(false); setDetailEditorOpen(false); setSelectedBatchId(batchId); lifecycleMutation.reset();
  }
  async function handleDetailSaved(result: { procurementBatchId: string }) {
    setSearch(''); setCreatingHeader(false); setSelectedBatchId(result.procurementBatchId); setDetailEditorOpen(true);
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['procurement-workspace', 'batches'] }),
      queryClient.invalidateQueries({ queryKey: ['procurement-workspace', 'batch', result.procurementBatchId] }),
    ]);
  }

  return (
    <section className="procurement-workspace-page" aria-labelledby="procurement-workspace-title">
      <header className="page-header procurement-workspace-header">
        <h1 id="procurement-workspace-title">{labels.title}</h1>
        <button type="button" className="procurement-primary-action" onClick={startNewProcurement}>＋ {labels.newProcurement}</button>
      </header>
      <div className="procurement-workspace-layout">
        <section className="procurement-batch-list" aria-label={labels.batchList}>
          <label className="procurement-batch-search"><span>{labels.search}</span><input type="search" value={search} onChange={(event) => setSearch(event.target.value)} /></label>
          <div className="procurement-batch-items">
            {batchQuery.isPending && <div className="procurement-state">{labels.loading}</div>}
            {batchQuery.isError && <div className="problem-banner" role="alert">{labels.unavailable}</div>}
            {!batchQuery.isPending && !batchQuery.isError && batches.length === 0 && <div className="procurement-state">{labels.empty}</div>}
            {batches.map((batch) => (
              <button key={batch.id} type="button" className={`procurement-batch-item${effectiveSelectedBatchId === batch.id ? ' is-selected' : ''}${batch.deletedAt ? ' is-deleted' : ''}`} onClick={() => selectBatch(batch.id)}>
                <strong>{batch.procurementProductDisplayName}</strong><span>{formatDate(batch.procurementDate, locale)}</span>
                <span className="procurement-batch-status">{batch.deletedAt ? labels.deleted : procurementStatusLabel(batch.procurementStatus, labels)}</span>
              </button>
            ))}
          </div>
        </section>

        <section className="procurement-document-workspace">
          {lifecycleProblem && <div className="problem-banner" role="alert">{lifecycleProblem}</div>}
          {creatingHeader ? (
            <NewProcurementHeader labels={labels} locale={locale} date={draftDate} onDateChange={(value) => { setDraftDate(value); setDetailEditorOpen(false); }} selectedProduct={draftProduct} productItems={productItems} productSearch={draftProductSearch} onProductSearchChange={setDraftProductSearch} onProductChange={(id) => { setDraftProduct(productItems.find((item) => item.id === id) ?? null); setDetailEditorOpen(false); }} productLoading={productQuery.isPending} onCancel={cancelNewProcurement} onAddDetail={() => setDetailEditorOpen(true)} canAddDetail={canAddDetail} />
          ) : (
            <ExistingProcurementDocument labels={labels} locale={locale} workspace={existingWorkspace} loading={workspaceQuery.isPending && effectiveSelectedBatchId !== null} error={workspaceQuery.isError} onAddDetail={() => setDetailEditorOpen(true)} canAddDetail={canAddDetail} canHardDelete={canHardDelete} onLifecycle={(input) => lifecycleMutation.mutate(input)} lifecyclePending={lifecycleMutation.isPending} />
          )}
          {detailEditorOpen && detailHeader && <ProcurementDetailForm header={detailHeader} onSaved={handleDetailSaved} onCancel={() => setDetailEditorOpen(false)} />}
        </section>
      </div>
    </section>
  );
}

function ExistingProcurementDocument({ labels, locale, workspace, loading, error, onAddDetail, canAddDetail, canHardDelete, onLifecycle, lifecyclePending }: {
  labels: WorkspaceLabels; locale: 'zh-TW' | 'th-TH'; workspace: ProcurementBatchWorkspace | null; loading: boolean; error: boolean; onAddDetail: () => void; canAddDetail: boolean; canHardDelete: boolean; onLifecycle: (input: LifecycleInput) => void; lifecyclePending: boolean;
}) {
  if (loading) return <div className="procurement-state">{labels.loading}</div>;
  if (error) return <div className="problem-banner" role="alert">{labels.unavailable}</div>;
  if (workspace === null) return <div className="procurement-state">{labels.empty}</div>;
  return (
    <div className="procurement-document">
      <header className="procurement-document-header">
        <div className="procurement-document-title"><span>{labels.header}</span><strong>{workspace.procurementProductDisplayName}</strong></div>
        <div className="procurement-document-actions">
          <span className="procurement-document-statuses"><span>{procurementStatusLabel(workspace.procurementStatus, labels)}</span><span>{workspace.deletedAt ? labels.deleted : lifecycleStatusLabel(workspace.lifecycleStatus, labels)}</span></span>
          <LifecycleButtons deleted={workspace.deletedAt !== null} pending={lifecyclePending} labels={labels} canHardDelete={canHardDelete} onSoft={() => onLifecycle({ target: 'batch', id: workspace.id, action: 'soft-delete', rowVersion: workspace.rowVersion })} onRestore={() => onLifecycle({ target: 'batch', id: workspace.id, action: 'restore', rowVersion: workspace.rowVersion })} onHard={() => onLifecycle({ target: 'batch', id: workspace.id, action: 'hard-delete', rowVersion: workspace.rowVersion })} />
        </div>
      </header>
      <dl className="procurement-header-fields">
        <HeaderField label={labels.date} value={formatDate(workspace.procurementDate, locale)} /><HeaderField label={labels.product} value={workspace.procurementProductDisplayName} /><HeaderField label={labels.status} value={procurementStatusLabel(workspace.procurementStatus, labels)} /><HeaderField label={labels.lifecycle} value={workspace.deletedAt ? labels.deleted : lifecycleStatusLabel(workspace.lifecycleStatus, labels)} />
      </dl>
      <section className="procurement-detail-section">
        <header className="procurement-detail-section-header"><h2>{labels.details}</h2><button type="button" className="procurement-primary-action" onClick={onAddDetail} disabled={!canAddDetail}>＋ {labels.addDetail}</button></header>
        {workspace.entries.length === 0 ? <div className="procurement-state">{labels.noDetails}</div> : <ProcurementDetailTable entries={workspace.entries} labels={labels} locale={locale} pending={lifecyclePending} canHardDelete={canHardDelete} onLifecycle={onLifecycle} />}
      </section>
    </div>
  );
}

function ProcurementDetailTable({ entries, labels, locale, pending, canHardDelete, onLifecycle }: { entries: readonly ProcurementBatchEntry[]; labels: WorkspaceLabels; locale: 'zh-TW' | 'th-TH'; pending: boolean; canHardDelete: boolean; onLifecycle: (input: LifecycleInput) => void }) {
  return (
    <div className="procurement-detail-table-wrap"><table className="procurement-detail-table">
      <thead><tr><th>{labels.code}</th><th>{labels.source}</th><th>{labels.quantity}</th><th>{labels.unitPrice}</th><th>{labels.amount}</th><th>{labels.receiptLocation}</th><th>{labels.pickup}</th><th>{labels.recordedAt}</th><th>{labels.actions}</th></tr></thead>
      <tbody>{entries.map((entry) => (
        <tr key={entry.id} className={entry.deletedAt ? 'is-deleted' : ''}>
          <td className="cell-code" data-label={labels.code}><strong>{entry.sourceCode ?? '—'}</strong></td>
          <td className="cell-source" data-label={labels.source}><strong>{entry.sourceDisplayName}</strong>{entry.deletedAt && <small>{labels.deleted}</small>}</td>
          <td className="cell-quantity" data-label={labels.quantity}>{formatNumber(entry.netQuantity, locale)} {entry.unitCodeSnapshot}</td>
          <td className="cell-price" data-label={labels.unitPrice}>{formatNumber(entry.unitPrice, locale)}</td>
          <td className="cell-amount" data-label={labels.amount}>{formatMoney(entry.amountThb, locale)}</td>
          <td className="cell-location" data-label={labels.receiptLocation}>{entry.receiptStorageLocationDisplayName ?? '—'}</td>
          <td className="cell-pickup" data-label={labels.pickup}>{entry.companyPickup ? labels.companyPickup : labels.otherPickup}</td>
          <td className="cell-recorded" data-label={labels.recordedAt}>{formatDateTime(entry.recordedAt, locale)}</td>
          <td className="cell-actions" data-label={labels.actions}><LifecycleButtons compact deleted={entry.deletedAt !== null} pending={pending} labels={labels} canHardDelete={canHardDelete} onSoft={() => onLifecycle({ target: 'entry', id: entry.id, action: 'soft-delete', rowVersion: entry.rowVersion })} onRestore={() => onLifecycle({ target: 'entry', id: entry.id, action: 'restore', rowVersion: entry.rowVersion })} onHard={() => onLifecycle({ target: 'entry', id: entry.id, action: 'hard-delete', rowVersion: entry.rowVersion })} /></td>
        </tr>
      ))}</tbody>
    </table></div>
  );
}

function LifecycleButtons({ deleted, pending, labels, canHardDelete, onSoft, onRestore, onHard, compact = false }: { deleted: boolean; pending: boolean; labels: WorkspaceLabels; canHardDelete: boolean; onSoft: () => void; onRestore: () => void; onHard: () => void; compact?: boolean }) {
  return <span className={`procurement-lifecycle-actions${compact ? ' is-compact' : ''}`}>
    {deleted ? <button type="button" className="procurement-secondary-action" disabled={pending} onClick={onRestore}>{labels.restore}</button> : <button type="button" className="procurement-secondary-action" disabled={pending} onClick={onSoft}>{labels.softDelete}</button>}
    {canHardDelete && <button type="button" className="procurement-danger-action" disabled={pending} onClick={onHard}>{labels.hardDelete}</button>}
  </span>;
}

function NewProcurementHeader({ labels, locale, date, onDateChange, selectedProduct, productItems, productSearch, onProductSearchChange, onProductChange, productLoading, onCancel, onAddDetail, canAddDetail }: { labels: WorkspaceLabels; locale: 'zh-TW' | 'th-TH'; date: string; onDateChange: (value: string) => void; selectedProduct: ProcurementProductOption | null; productItems: readonly ProcurementProductOption[]; productSearch: string; onProductSearchChange: (value: string) => void; onProductChange: (id: string) => void; productLoading: boolean; onCancel: () => void; onAddDetail: () => void; canAddDetail: boolean }) {
  return <div className="procurement-document procurement-new-document">
    <header className="procurement-document-header"><div className="procurement-document-title"><span>{labels.header}</span><strong>{labels.newProcurement}</strong></div><button type="button" className="procurement-secondary-action" onClick={onCancel}>{labels.cancelNew}</button></header>
    <div className="procurement-new-header-fields"><label><span>{labels.date}</span><input type="date" value={date} onChange={(event) => onDateChange(event.target.value)} /></label><SearchableSelect label={labels.product} value={selectedProduct?.id ?? ''} options={productItems.map((item) => ({ id: item.id, label: item.displayName, secondaryLabel: item.unitCode }))} onChange={onProductChange} searchValue={productSearch} onSearchChange={onProductSearchChange} searchLabel={labels.productSearch} chooseLabel={labels.productSelect} loadingLabel={labels.loading} loading={productLoading} /></div>
    <section className="procurement-detail-section"><header className="procurement-detail-section-header"><h2>{labels.details}</h2><button type="button" className="procurement-primary-action" onClick={onAddDetail} disabled={!canAddDetail}>＋ {labels.addDetail}</button></header><div className="procurement-state">{labels.noDetails}</div></section><span className="procurement-date-context">{formatDate(date, locale)}</span>
  </div>;
}

function HeaderField({ label, value }: { label: string; value: string }) { return <div><dt>{label}</dt><dd>{value}</dd></div>; }
function includeSelected<T extends { id: string }>(items: readonly T[], selected: T | null): T[] { return selected === null || items.some((item) => item.id === selected.id) ? [...items] : [selected, ...items]; }
function procurementStatusLabel(status: string, labels: WorkspaceLabels): string { if (status === 'OPEN') return labels.open; if (status === 'COMPLETED') return labels.completed; return status; }
function lifecycleStatusLabel(status: string, labels: WorkspaceLabels): string { if (status === 'ACTIVE') return labels.active; if (status === 'CLOSED') return labels.closed; return status; }
function formatDate(value: string, locale: 'zh-TW' | 'th-TH'): string { return new Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(`${value}T00:00:00`)); }
function formatDateTime(value: string, locale: 'zh-TW' | 'th-TH'): string { return new Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(value)); }
function formatNumber(value: number, locale: 'zh-TW' | 'th-TH'): string { return new Intl.NumberFormat(locale, { maximumFractionDigits: 6 }).format(value); }
function formatMoney(value: number, locale: 'zh-TW' | 'th-TH'): string { return new Intl.NumberFormat(locale, { style: 'currency', currency: 'THB', maximumFractionDigits: 0 }).format(value); }
function todayValue(): string { const now = new Date(); return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`; }
