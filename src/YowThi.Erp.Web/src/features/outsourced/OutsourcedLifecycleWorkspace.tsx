import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useState } from 'react';

import { ApiProblemError } from '../../app/api/apiTransport';
import { SearchableSelect } from '../../app/forms/SearchableSelect';
import { useOperationalLocale } from '../../app/i18n/locale';
import { getAuthenticationSession, prepareDeletionReauthentication } from '../../app/security/authSession';
import { closeOutsourcedSupplyBatch } from './closeOutsourcedSupplyBatch';
import { OutsourcedDetailForm } from './OutsourcedDetailForm';
import {
  listOutsourcedVendorOptions,
  type OutsourcedVendorOption,
} from './outsourcedSupplyDetailOptions';
import {
  changeOutsourcedLifecycle,
  type OutsourcedLifecycleAction,
  type OutsourcedLifecycleTarget,
} from './outsourcedTransactionLifecycle';
import {
  getOutsourcedWorkspace,
  listOutsourcedWorkspace,
  type OutsourcedWorkspace,
  type OutsourcedWorkspaceDetail,
} from './outsourcedWorkspace';
import './OutsourcedLifecycleWorkspace.css';

const copy = {
  'zh-TW': {
    title: '委外管理', newOutsourced: '新增委外', cancelNew: '取消新增', addDetail: '新增明細', batchList: '委外主單', search: '搜尋日期或委外商', loading: '載入中', unavailable: '資料載入失敗', empty: '尚無委外主單', header: '主單',
    date: '日期', vendor: '委外商', vendorSearch: '搜尋委外商', vendorSelect: '選擇委外商', status: '主單狀態', active: '啟用', closed: '已關閉', deleted: '已刪除', details: '委外明細', noDetails: '尚無委外明細',
    product: '產品', quantity: '數量', pricing: '計價', unitPrice: '單價', amount: '金額', location: '收貨儲位', recordedAt: '登錄時間', actions: '操作', close: '關閉主單', softDelete: '刪除', restore: '還原', hardDelete: '永久刪除',
    weightBased: '依重量計價', unitBased: '依件數計價', stale: '資料已更新，請重新載入。', notFound: '資料已不存在，請重新載入。', dependency: '相關資料無法安全處理，未執行操作。', reauth: '刪除前需要重新驗證登入身分。', inventoryRemaining: '主單仍有可銷售庫存，無法關閉。', alreadyClosed: '主單已關閉。', unexpected: '操作失敗。',
  },
  'th-TH': {
    title: 'งานภายนอก', newOutsourced: 'เพิ่มงานภายนอก', cancelNew: 'ยกเลิกการเพิ่ม', addDetail: 'เพิ่มรายละเอียด', batchList: 'เอกสารงานภายนอก', search: 'ค้นหาวันที่หรือผู้รับจ้าง', loading: 'กำลังโหลด', unavailable: 'โหลดข้อมูลไม่สำเร็จ', empty: 'ยังไม่มีเอกสารงานภายนอก', header: 'เอกสารหลัก',
    date: 'วันที่', vendor: 'ผู้รับจ้างภายนอก', vendorSearch: 'ค้นหาผู้รับจ้าง', vendorSelect: 'เลือกผู้รับจ้าง', status: 'สถานะเอกสาร', active: 'ใช้งาน', closed: 'ปิดแล้ว', deleted: 'ลบแล้ว', details: 'รายละเอียดงานภายนอก', noDetails: 'ยังไม่มีรายละเอียด',
    product: 'สินค้า', quantity: 'ปริมาณ', pricing: 'เกณฑ์ราคา', unitPrice: 'ราคาต่อหน่วย', amount: 'จำนวนเงิน', location: 'ตำแหน่งรับสินค้า', recordedAt: 'เวลาบันทึก', actions: 'จัดการ', close: 'ปิดเอกสาร', softDelete: 'ลบ', restore: 'กู้คืน', hardDelete: 'ลบถาวร',
    weightBased: 'คิดราคาตามน้ำหนัก', unitBased: 'คิดราคาต่อหน่วย', stale: 'ข้อมูลถูกเปลี่ยนแล้ว โปรดโหลดใหม่', notFound: 'ไม่พบข้อมูลแล้ว โปรดโหลดใหม่', dependency: 'ไม่สามารถดำเนินการกับข้อมูลที่เกี่ยวข้องได้อย่างปลอดภัย', reauth: 'ต้องยืนยันตัวตนอีกครั้งก่อนลบ', inventoryRemaining: 'ยังมีสินค้าคงคลังที่ขายได้ จึงยังปิดเอกสารไม่ได้', alreadyClosed: 'เอกสารถูกปิดแล้ว', unexpected: 'ดำเนินการไม่สำเร็จ',
  },
} as const;

type Labels = (typeof copy)[keyof typeof copy];
type LifecycleInput = { target: OutsourcedLifecycleTarget; id: string; action: OutsourcedLifecycleAction; rowVersion: number };

export function OutsourcedLifecycleWorkspace() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const deferredSearch = useDeferredValue(search);
  const [selectedBatchId, setSelectedBatchId] = useState<string | null>(null);
  const [creatingHeader, setCreatingHeader] = useState(false);
  const [draftDate, setDraftDate] = useState(todayValue());
  const [draftVendorSearch, setDraftVendorSearch] = useState('');
  const [draftVendor, setDraftVendor] = useState<OutsourcedVendorOption | null>(null);
  const [detailEditorOpen, setDetailEditorOpen] = useState(false);

  const sessionQuery = useQuery({
    queryKey: ['authentication-session'],
    queryFn: ({ signal }) => getAuthenticationSession(signal),
    staleTime: 30_000,
  });
  const canConfirm = sessionQuery.data?.capabilities.includes('outsourced.confirm') ?? false;
  const canLifecycle = sessionQuery.data?.capabilities.includes('outsourced.transaction.lifecycle') ?? false;
  const canHardDelete = canLifecycle && (sessionQuery.data?.capabilities.includes('data-protection.hard-delete') ?? false);

  const listQuery = useQuery({
    queryKey: ['outsourced-workspace', 'list', locale, deferredSearch],
    queryFn: ({ signal }) => listOutsourcedWorkspace({ locale, search: deferredSearch, offset: 0, limit: 100, signal }),
    staleTime: 2_000,
  });
  const vendorQuery = useQuery({
    queryKey: ['outsourced-workspace', 'new-header-vendors', locale, draftVendorSearch],
    queryFn: ({ signal }) => listOutsourcedVendorOptions({ locale, search: draftVendorSearch, limit: 50, signal }),
    enabled: creatingHeader,
    staleTime: 10_000,
  });

  const items = listQuery.data?.items ?? [];
  const vendorItems = includeSelected(vendorQuery.data?.items ?? [], draftVendor);
  const effectiveBatchId = creatingHeader ? null : selectedBatchId ?? items[0]?.id ?? null;
  const workspaceQuery = useQuery({
    queryKey: ['outsourced-workspace', 'item', effectiveBatchId, locale],
    queryFn: ({ signal }) => getOutsourcedWorkspace(effectiveBatchId!, locale, signal),
    enabled: effectiveBatchId !== null,
    staleTime: 1_000,
  });

  const lifecycleMutation = useMutation({
    mutationFn: async (input: LifecycleInput) => {
      if (input.action !== 'restore') await prepareDeletionReauthentication();
      return changeOutsourcedLifecycle(input.target, input.id, input.action, { expectedRowVersion: input.rowVersion }, { idempotencyKey: crypto.randomUUID(), locale });
    },
    onSuccess: async (_, input) => {
      setDetailEditorOpen(false);
      if (input.target === 'batch' && input.action === 'hard-delete') setSelectedBatchId(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['outsourced-workspace'] }),
        queryClient.invalidateQueries({ queryKey: ['hard-delete-options', input.target === 'batch' ? 'outsourced-supply-batches' : 'outsourced-supply-details'] }),
      ]);
    },
  });

  const closeMutation = useMutation({
    mutationFn: (workspace: OutsourcedWorkspace) => closeOutsourcedSupplyBatch(workspace.id, workspace.rowVersion, { idempotencyKey: crypto.randomUUID(), locale }),
    onSuccess: async (result) => {
      setDetailEditorOpen(false);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['outsourced-workspace', 'list'] }),
        queryClient.invalidateQueries({ queryKey: ['outsourced-workspace', 'item', result.outsourcedSupplyBatchId] }),
      ]);
    },
  });

  const problem = useMemo(() => {
    const error = lifecycleMutation.error ?? closeMutation.error;
    if (!error) return null;
    if (!(error instanceof ApiProblemError)) return labels.unexpected;
    if (error.code === 'concurrency.stale-row-version' || error.code === 'outsourced.concurrent-change') return labels.stale;
    if (error.code === 'security.deletion-reauth-required') return labels.reauth;
    if (error.code === 'outsourced.batch-sellable-inventory-remaining') return labels.inventoryRemaining;
    if (error.code === 'outsourced.batch-already-closed') return labels.alreadyClosed;
    if (error.code.endsWith('-not-found')) return labels.notFound;
    if (error.code.endsWith('-closure-invalid') || error.code.endsWith('-dependency-blocked') || error.code === 'outsourced.batch-unavailable') return labels.dependency;
    return error.code;
  }, [closeMutation.error, labels, lifecycleMutation.error]);

  const workspace = workspaceQuery.data ?? null;
  const detailHeader = creatingHeader
    ? draftVendor && draftDate ? { supplyDate: draftDate, outsourcedVendorId: draftVendor.id, outsourcedVendorDisplayName: draftVendor.displayName } : null
    : workspace ? { supplyDate: workspace.supplyDate, outsourcedVendorId: workspace.outsourcedVendorId, outsourcedVendorDisplayName: workspace.outsourcedVendorDisplayName } : null;
  const existingCanAddDetail = workspace !== null && workspace.lifecycleStatus === 'ACTIVE' && workspace.deletedAt === null;
  const canAddDetail = canConfirm && (creatingHeader ? detailHeader !== null : existingCanAddDetail);

  function startNewOutsourced() {
    setCreatingHeader(true); setSelectedBatchId(null); setDraftDate(todayValue()); setDraftVendor(null); setDraftVendorSearch(''); setDetailEditorOpen(false); lifecycleMutation.reset(); closeMutation.reset();
  }
  function cancelNewOutsourced() {
    setCreatingHeader(false); setDetailEditorOpen(false); setSelectedBatchId(items[0]?.id ?? null);
  }
  function selectBatch(batchId: string) {
    setCreatingHeader(false); setDetailEditorOpen(false); setSelectedBatchId(batchId); lifecycleMutation.reset(); closeMutation.reset();
  }
  async function handleDetailSaved(result: { outsourcedSupplyBatchId: string }) {
    setSearch(''); setCreatingHeader(false); setSelectedBatchId(result.outsourcedSupplyBatchId); setDetailEditorOpen(true);
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['outsourced-workspace', 'list'] }),
      queryClient.invalidateQueries({ queryKey: ['outsourced-workspace', 'item', result.outsourcedSupplyBatchId] }),
    ]);
  }

  return (
    <section className="outsourced-workspace" aria-labelledby="outsourced-workspace-title">
      <header className="page-header outsourced-workspace-heading">
        <h1 id="outsourced-workspace-title">{labels.title}</h1>
        <button type="button" className="outsourced-primary-action" onClick={startNewOutsourced}>＋ {labels.newOutsourced}</button>
      </header>
      <div className="outsourced-workspace-layout">
        <aside className="outsourced-batch-list" aria-label={labels.batchList}>
          <label><span>{labels.search}</span><input type="search" value={search} onChange={(event) => setSearch(event.target.value)} /></label>
          <div className="outsourced-batch-items">
            {listQuery.isPending && <div className="outsourced-state">{labels.loading}</div>}
            {listQuery.isError && <div className="problem-banner" role="alert">{labels.unavailable}</div>}
            {!listQuery.isPending && !listQuery.isError && items.length === 0 && <div className="outsourced-state">{labels.empty}</div>}
            {items.map((item) => (
              <button key={item.id} type="button" className={`outsourced-batch-item${effectiveBatchId === item.id ? ' is-selected' : ''}${item.deletedAt ? ' is-deleted' : ''}`} onClick={() => selectBatch(item.id)}>
                <strong>{item.outsourcedVendorDisplayName}</strong><span>{formatDate(item.supplyDate, locale)}</span><small>{item.deletedAt ? labels.deleted : statusLabel(item.lifecycleStatus, labels)}</small>
              </button>
            ))}
          </div>
        </aside>

        <section className="outsourced-document-workspace">
          {problem && <div className="problem-banner" role="alert">{problem}</div>}
          {creatingHeader ? (
            <NewOutsourcedHeader labels={labels} date={draftDate} onDateChange={(value) => { setDraftDate(value); setDetailEditorOpen(false); }} selectedVendor={draftVendor} vendorItems={vendorItems} vendorSearch={draftVendorSearch} onVendorSearchChange={setDraftVendorSearch} onVendorChange={(id) => { setDraftVendor(vendorItems.find((item) => item.id === id) ?? null); setDetailEditorOpen(false); }} vendorLoading={vendorQuery.isPending} onCancel={cancelNewOutsourced} onAddDetail={() => setDetailEditorOpen(true)} canAddDetail={canAddDetail} />
          ) : (
            <ExistingOutsourcedDocument labels={labels} locale={locale} workspace={workspace} loading={workspaceQuery.isPending && effectiveBatchId !== null} error={workspaceQuery.isError} onAddDetail={() => setDetailEditorOpen(true)} canAddDetail={canAddDetail} canConfirm={canConfirm} canLifecycle={canLifecycle} canHardDelete={canHardDelete} onClose={() => workspace && closeMutation.mutate(workspace)} onLifecycle={(input) => lifecycleMutation.mutate(input)} pending={lifecycleMutation.isPending || closeMutation.isPending} />
          )}
          {detailEditorOpen && detailHeader && <OutsourcedDetailForm header={detailHeader} onSaved={handleDetailSaved} onCancel={() => setDetailEditorOpen(false)} />}
        </section>
      </div>
    </section>
  );
}

function ExistingOutsourcedDocument({ labels, locale, workspace, loading, error, onAddDetail, canAddDetail, canConfirm, canLifecycle, canHardDelete, onClose, onLifecycle, pending }: {
  labels: Labels; locale: 'zh-TW' | 'th-TH'; workspace: OutsourcedWorkspace | null; loading: boolean; error: boolean; onAddDetail: () => void; canAddDetail: boolean; canConfirm: boolean; canLifecycle: boolean; canHardDelete: boolean; onClose: () => void; onLifecycle: (input: LifecycleInput) => void; pending: boolean;
}) {
  if (loading) return <div className="outsourced-state">{labels.loading}</div>;
  if (error) return <div className="problem-banner" role="alert">{labels.unavailable}</div>;
  if (workspace === null) return <div className="outsourced-state">{labels.empty}</div>;
  const canClose = canConfirm && workspace.lifecycleStatus === 'ACTIVE' && workspace.deletedAt === null;
  return <div className="outsourced-document">
    <header className="outsourced-document-header">
      <div><span>{labels.header}</span><strong>{workspace.outsourcedVendorDisplayName}</strong></div>
      <div className="outsourced-document-actions">
        <span className="outsourced-document-status">{workspace.deletedAt ? labels.deleted : statusLabel(workspace.lifecycleStatus, labels)}</span>
        {canClose && <button type="button" className="outsourced-secondary-action" disabled={pending} onClick={onClose}>{labels.close}</button>}
        <LifecycleButtons deleted={workspace.deletedAt !== null} pending={pending} canLifecycle={canLifecycle} canHardDelete={canHardDelete} labels={labels} onSoft={() => onLifecycle({ target: 'batch', id: workspace.id, action: 'soft-delete', rowVersion: workspace.rowVersion })} onRestore={() => onLifecycle({ target: 'batch', id: workspace.id, action: 'restore', rowVersion: workspace.rowVersion })} onHard={() => onLifecycle({ target: 'batch', id: workspace.id, action: 'hard-delete', rowVersion: workspace.rowVersion })} />
      </div>
    </header>
    <dl className="outsourced-header-fields"><HeaderField label={labels.date} value={formatDate(workspace.supplyDate, locale)} /><HeaderField label={labels.vendor} value={workspace.outsourcedVendorDisplayName} /><HeaderField label={labels.status} value={workspace.deletedAt ? labels.deleted : statusLabel(workspace.lifecycleStatus, labels)} /></dl>
    <section className="outsourced-details"><header className="outsourced-details-header"><h2>{labels.details}</h2><button type="button" className="outsourced-primary-action" disabled={!canAddDetail} onClick={onAddDetail}>＋ {labels.addDetail}</button></header>{workspace.details.length === 0 ? <div className="outsourced-state">{labels.noDetails}</div> : <DetailTable details={workspace.details} labels={labels} locale={locale} pending={pending} canLifecycle={canLifecycle} canHardDelete={canHardDelete} onLifecycle={onLifecycle} />}</section>
  </div>;
}

function NewOutsourcedHeader({ labels, date, onDateChange, selectedVendor, vendorItems, vendorSearch, onVendorSearchChange, onVendorChange, vendorLoading, onCancel, onAddDetail, canAddDetail }: {
  labels: Labels; date: string; onDateChange: (value: string) => void; selectedVendor: OutsourcedVendorOption | null; vendorItems: readonly OutsourcedVendorOption[]; vendorSearch: string; onVendorSearchChange: (value: string) => void; onVendorChange: (id: string) => void; vendorLoading: boolean; onCancel: () => void; onAddDetail: () => void; canAddDetail: boolean;
}) {
  return <div className="outsourced-document outsourced-new-document">
    <header className="outsourced-document-header"><div><span>{labels.header}</span><strong>{labels.newOutsourced}</strong></div><button type="button" className="outsourced-secondary-action" onClick={onCancel}>{labels.cancelNew}</button></header>
    <div className="outsourced-new-header-fields"><label><span>{labels.date}</span><input type="date" value={date} onChange={(event) => onDateChange(event.target.value)} /></label><SearchableSelect label={labels.vendor} value={selectedVendor?.id ?? ''} options={vendorItems.map((item) => ({ id: item.id, label: item.displayName }))} onChange={onVendorChange} searchValue={vendorSearch} onSearchChange={onVendorSearchChange} searchLabel={labels.vendorSearch} chooseLabel={labels.vendorSelect} loadingLabel={labels.loading} loading={vendorLoading} /></div>
    <section className="outsourced-details"><header className="outsourced-details-header"><h2>{labels.details}</h2><button type="button" className="outsourced-primary-action" disabled={!canAddDetail} onClick={onAddDetail}>＋ {labels.addDetail}</button></header><div className="outsourced-state">{labels.noDetails}</div></section>
  </div>;
}

function DetailTable({ details, labels, locale, pending, canLifecycle, canHardDelete, onLifecycle }: { details: readonly OutsourcedWorkspaceDetail[]; labels: Labels; locale: 'zh-TW' | 'th-TH'; pending: boolean; canLifecycle: boolean; canHardDelete: boolean; onLifecycle: (input: LifecycleInput) => void }) {
  return <div className="outsourced-detail-wrap"><table className="outsourced-detail-table"><thead><tr><th>{labels.product}</th><th>{labels.quantity}</th><th>{labels.pricing}</th><th>{labels.unitPrice}</th><th>{labels.amount}</th><th>{labels.location}</th><th>{labels.recordedAt}</th><th>{labels.actions}</th></tr></thead><tbody>{details.map((detail) => <tr key={detail.id} className={detail.deletedAt ? 'is-deleted' : ''}>
    <td data-label={labels.product}><strong>{detail.salesProductDisplayName}</strong>{detail.deletedAt && <small>{labels.deleted}</small>}</td><td data-label={labels.quantity}>{formatNumber(detail.quantity, locale)}</td><td data-label={labels.pricing}>{pricingLabel(detail.pricingBasis, labels)}</td><td data-label={labels.unitPrice}>{formatNumber(detail.unitPrice, locale)}</td><td data-label={labels.amount}>{formatMoney(detail.amountThb, locale)}</td><td data-label={labels.location}>{detail.receiptStorageLocationDisplayName}</td><td data-label={labels.recordedAt}>{formatDateTime(detail.recordedAt, locale)}</td><td data-label={labels.actions}><LifecycleButtons compact deleted={detail.deletedAt !== null} pending={pending} canLifecycle={canLifecycle} canHardDelete={canHardDelete} labels={labels} onSoft={() => onLifecycle({ target: 'detail', id: detail.id, action: 'soft-delete', rowVersion: detail.rowVersion })} onRestore={() => onLifecycle({ target: 'detail', id: detail.id, action: 'restore', rowVersion: detail.rowVersion })} onHard={() => onLifecycle({ target: 'detail', id: detail.id, action: 'hard-delete', rowVersion: detail.rowVersion })} /></td>
  </tr>)}</tbody></table></div>;
}

function LifecycleButtons({ deleted, pending, canLifecycle, canHardDelete, labels, onSoft, onRestore, onHard, compact = false }: { deleted: boolean; pending: boolean; canLifecycle: boolean; canHardDelete: boolean; labels: Labels; onSoft: () => void; onRestore: () => void; onHard: () => void; compact?: boolean }) {
  if (!canLifecycle && !canHardDelete) return null;
  return <span className={`outsourced-lifecycle-actions${compact ? ' is-compact' : ''}`}>{canLifecycle && (deleted ? <button type="button" className="outsourced-secondary-action" disabled={pending} onClick={onRestore}>{labels.restore}</button> : <button type="button" className="outsourced-secondary-action" disabled={pending} onClick={onSoft}>{labels.softDelete}</button>)}{canHardDelete && <button type="button" className="outsourced-danger-action" disabled={pending} onClick={onHard}>{labels.hardDelete}</button>}</span>;
}

function HeaderField({ label, value }: { label: string; value: string }) { return <div><dt>{label}</dt><dd>{value}</dd></div>; }
function includeSelected<T extends { id: string }>(items: readonly T[], selected: T | null): T[] { return selected === null || items.some((item) => item.id === selected.id) ? [...items] : [selected, ...items]; }
function statusLabel(status: string, labels: Labels): string { if (status === 'ACTIVE') return labels.active; if (status === 'CLOSED') return labels.closed; return status; }
function pricingLabel(value: string, labels: Labels): string { if (value === 'WEIGHT_BASED_UNIT') return labels.weightBased; if (value === 'UNIT_BASED') return labels.unitBased; return value; }
function formatDate(value: string, locale: 'zh-TW' | 'th-TH'): string { return new Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(`${value}T00:00:00`)); }
function formatDateTime(value: string, locale: 'zh-TW' | 'th-TH'): string { return new Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(value)); }
function formatNumber(value: number, locale: 'zh-TW' | 'th-TH'): string { return new Intl.NumberFormat(locale, { maximumFractionDigits: 6 }).format(value); }
function formatMoney(value: number, locale: 'zh-TW' | 'th-TH'): string { return new Intl.NumberFormat(locale, { style: 'currency', currency: 'THB', maximumFractionDigits: 0 }).format(value); }
function todayValue(): string { const now = new Date(); return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`; }
