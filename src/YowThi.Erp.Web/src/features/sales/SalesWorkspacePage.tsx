import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useState } from 'react';

import { ApiProblemError } from '../../app/api/apiTransport';
import { useOperationalLocale } from '../../app/i18n/locale';
import { getAuthenticationSession, prepareDeletionReauthentication } from '../../app/security/authSession';
import { confirmSales, type ConfirmSalesRequest } from './confirmSales';
import {
  changeSalesLifecycle,
  type SalesLifecycleAction,
  type SalesLifecycleTarget,
} from './salesTransactionLifecycle';
import {
  getSalesWorkspace,
  listSalesWorkspace,
  type SalesWorkspace,
  type SalesWorkspaceDetail,
} from './salesWorkspace';
import './SalesWorkspacePage.css';

const copy = {
  'zh-TW': {
    title: '銷售管理', list: '銷售單', search: '搜尋客戶', loading: '載入中', empty: '目前沒有銷售資料', unavailable: '資料載入失敗',
    date: '日期', customer: '客戶', status: '狀態', draft: '草稿', confirmed: '已確認', deleted: '已刪除', rowVersion: '資料版本',
    details: '銷售明細', noDetails: '目前沒有銷售明細', line: '明細', product: '產品', quantity: '數量', pricing: '計價', weight: '重量', unitPrice: '單價', amount: '金額', actions: '操作', weightBased: '依重量計價', unitBased: '依件數計價',
    confirm: '確認銷售', confirming: '確認中', softDelete: '刪除', restore: '還原', hardDelete: '永久刪除',
    stale: '資料已更新，請重新載入。', notFound: '資料已不存在，請重新載入。', dependency: '相關資料無法安全關閉，未執行刪除。', reauth: '刪除前需要重新驗證登入身分。', insufficient: '可售庫存不足。', locationRequired: '可售庫存跨多個儲位，無法自動確認。', unexpected: '操作失敗。',
  },
  'th-TH': {
    title: 'จัดการการขาย', list: 'เอกสารขาย', search: 'ค้นหาลูกค้า', loading: 'กำลังโหลด', empty: 'ยังไม่มีข้อมูลการขาย', unavailable: 'โหลดข้อมูลไม่สำเร็จ',
    date: 'วันที่', customer: 'ลูกค้า', status: 'สถานะ', draft: 'ฉบับร่าง', confirmed: 'ยืนยันแล้ว', deleted: 'ลบแล้ว', rowVersion: 'รุ่นข้อมูล',
    details: 'รายละเอียดการขาย', noDetails: 'ยังไม่มีรายละเอียดการขาย', line: 'รายการ', product: 'สินค้า', quantity: 'ปริมาณ', pricing: 'เกณฑ์ราคา', weight: 'น้ำหนัก', unitPrice: 'ราคาต่อหน่วย', amount: 'จำนวนเงิน', actions: 'จัดการ', weightBased: 'คิดราคาตามน้ำหนัก', unitBased: 'คิดราคาต่อหน่วย',
    confirm: 'ยืนยันการขาย', confirming: 'กำลังยืนยัน', softDelete: 'ลบ', restore: 'กู้คืน', hardDelete: 'ลบถาวร',
    stale: 'ข้อมูลถูกเปลี่ยนแล้ว โปรดโหลดใหม่', notFound: 'ไม่พบข้อมูลแล้ว โปรดโหลดใหม่', dependency: 'ไม่สามารถปิดข้อมูลที่เกี่ยวข้องได้อย่างปลอดภัย จึงยังไม่ได้ลบ', reauth: 'ต้องยืนยันตัวตนอีกครั้งก่อนลบ', insufficient: 'สินค้าคงคลังที่ขายได้ไม่เพียงพอ', locationRequired: 'สินค้าคงคลังอยู่หลายตำแหน่ง จึงยืนยันอัตโนมัติไม่ได้', unexpected: 'ดำเนินการไม่สำเร็จ',
  },
} as const;

type Labels = (typeof copy)[keyof typeof copy];
type LifecycleInput = { target: SalesLifecycleTarget; id: string; action: SalesLifecycleAction; rowVersion: number };

export function SalesWorkspacePage() {
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
  const [selectedSalesId, setSelectedSalesId] = useState<string | null>(null);

  const listQuery = useQuery({
    queryKey: ['sales-workspace', 'list', locale, deferredSearch],
    queryFn: ({ signal }) => listSalesWorkspace({ locale, search: deferredSearch, offset: 0, limit: 100, signal }),
    staleTime: 2_000,
  });
  const items = listQuery.data?.items ?? [];
  const effectiveSalesId = selectedSalesId ?? items[0]?.id ?? null;
  const workspaceQuery = useQuery({
    queryKey: ['sales-workspace', 'item', effectiveSalesId, locale],
    queryFn: ({ signal }) => getSalesWorkspace(effectiveSalesId!, locale, signal),
    enabled: effectiveSalesId !== null,
    staleTime: 1_000,
  });

  const lifecycleMutation = useMutation({
    mutationFn: async (input: LifecycleInput) => {
      if (input.action !== 'restore') await prepareDeletionReauthentication();
      return changeSalesLifecycle(
        input.target,
        input.id,
        input.action,
        { expectedRowVersion: input.rowVersion },
        { idempotencyKey: crypto.randomUUID(), locale },
      );
    },
    onSuccess: async (_, input) => {
      if (input.target === 'sale' && input.action === 'hard-delete') setSelectedSalesId(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['sales-workspace'] }),
        queryClient.invalidateQueries({ queryKey: ['sales-confirmation-options'] }),
        queryClient.invalidateQueries({ queryKey: ['hard-delete-options', input.target === 'sale' ? 'sales' : 'sales-details'] }),
      ]);
    },
  });

  const confirmMutation = useMutation({
    mutationFn: async (workspace: SalesWorkspace) => {
      const request: ConfirmSalesRequest = { expectedRowVersion: workspace.rowVersion, manualAllocationOverrides: [] };
      return confirmSales(workspace.id, request, { idempotencyKey: crypto.randomUUID(), locale });
    },
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['sales-workspace'] }),
        queryClient.invalidateQueries({ queryKey: ['sales-confirmation-options'] }),
      ]);
    },
  });

  const problem = useMemo(() => {
    const error = lifecycleMutation.error ?? confirmMutation.error;
    if (!error) return null;
    if (!(error instanceof ApiProblemError)) return labels.unexpected;
    if (error.code === 'concurrency.stale-row-version') return labels.stale;
    if (error.code === 'inventory.insufficient-stock') return labels.insufficient;
    if (error.code === 'sales.issue-location-required') return labels.locationRequired;
    if (error.code === 'security.deletion-reauth-required') return labels.reauth;
    if (error.code.endsWith('-not-found')) return labels.notFound;
    if (error.code.endsWith('-dependency-blocked') || error.code.endsWith('-closure-invalid') || error.code.endsWith('-closure-ambiguous')) return labels.dependency;
    return error.code;
  }, [confirmMutation.error, labels, lifecycleMutation.error]);

  const workspace = workspaceQuery.data ?? null;
  const canConfirm = workspace !== null
    && workspace.status === 'DRAFT'
    && workspace.deletedAt === null
    && workspace.details.some((detail) => detail.deletedAt === null);

  return (
    <section className="sales-workspace-page" aria-labelledby="sales-workspace-title">
      <header className="page-header sales-workspace-header"><h1 id="sales-workspace-title">{labels.title}</h1></header>
      <div className="sales-workspace-layout">
        <section className="sales-list" aria-label={labels.list}>
          <label className="sales-search"><span>{labels.search}</span><input type="search" value={search} onChange={(event) => setSearch(event.target.value)} /></label>
          <div className="sales-list-items">
            {listQuery.isPending && <div className="sales-state">{labels.loading}</div>}
            {listQuery.isError && <div className="problem-banner" role="alert">{labels.unavailable}</div>}
            {!listQuery.isPending && !listQuery.isError && items.length === 0 && <div className="sales-state">{labels.empty}</div>}
            {items.map((item) => (
              <button key={item.id} type="button" className={`sales-list-item${effectiveSalesId === item.id ? ' is-selected' : ''}${item.deletedAt ? ' is-deleted' : ''}`} onClick={() => { setSelectedSalesId(item.id); lifecycleMutation.reset(); confirmMutation.reset(); }}>
                <strong>{item.customerDisplayName}</strong><span>{formatDate(item.salesDate, locale)}</span><span className="sales-status-pill">{item.deletedAt ? labels.deleted : statusLabel(item.status, labels)}</span>
              </button>
            ))}
          </div>
        </section>

        <section className="sales-document-workspace">
          {problem && <div className="problem-banner" role="alert">{problem}</div>}
          {workspaceQuery.isPending && effectiveSalesId !== null && <div className="sales-state">{labels.loading}</div>}
          {workspaceQuery.isError && <div className="problem-banner" role="alert">{labels.unavailable}</div>}
          {workspace && <SalesDocument workspace={workspace} labels={labels} locale={locale} lifecyclePending={lifecycleMutation.isPending} confirming={confirmMutation.isPending} canConfirm={canConfirm} canHardDelete={canHardDelete} onConfirm={() => confirmMutation.mutate(workspace)} onLifecycle={(input) => lifecycleMutation.mutate(input)} />}
          {!workspace && !workspaceQuery.isPending && !workspaceQuery.isError && <div className="sales-state">{labels.empty}</div>}
        </section>
      </div>
    </section>
  );
}

function SalesDocument({ workspace, labels, locale, lifecyclePending, confirming, canConfirm, canHardDelete, onConfirm, onLifecycle }: { workspace: SalesWorkspace; labels: Labels; locale: 'zh-TW' | 'th-TH'; lifecyclePending: boolean; confirming: boolean; canConfirm: boolean; canHardDelete: boolean; onConfirm: () => void; onLifecycle: (input: LifecycleInput) => void }) {
  return <div className="sales-document">
    <header className="sales-document-header">
      <div className="sales-document-title"><span>{labels.customer}</span><strong>{workspace.customerDisplayName}</strong></div>
      <div className="sales-document-actions">
        <span className="sales-status-pill">{workspace.deletedAt ? labels.deleted : statusLabel(workspace.status, labels)}</span>
        {canConfirm && <button type="button" className="sales-primary-action" disabled={confirming || lifecyclePending} onClick={onConfirm}>{confirming ? labels.confirming : labels.confirm}</button>}
        <LifecycleButtons deleted={workspace.deletedAt !== null} pending={lifecyclePending || confirming} labels={labels} canHardDelete={canHardDelete} onSoft={() => onLifecycle({ target: 'sale', id: workspace.id, action: 'soft-delete', rowVersion: workspace.rowVersion })} onRestore={() => onLifecycle({ target: 'sale', id: workspace.id, action: 'restore', rowVersion: workspace.rowVersion })} onHard={() => onLifecycle({ target: 'sale', id: workspace.id, action: 'hard-delete', rowVersion: workspace.rowVersion })} />
      </div>
    </header>
    <dl className="sales-header-fields"><HeaderField label={labels.date} value={formatDate(workspace.salesDate, locale)} /><HeaderField label={labels.customer} value={workspace.customerDisplayName} /><HeaderField label={labels.status} value={workspace.deletedAt ? labels.deleted : statusLabel(workspace.status, labels)} /><HeaderField label={labels.rowVersion} value={String(workspace.rowVersion)} /></dl>
    <section className="sales-detail-section"><h2>{labels.details}</h2>{workspace.details.length === 0 ? <div className="sales-state">{labels.noDetails}</div> : <SalesDetailTable details={workspace.details} labels={labels} locale={locale} pending={lifecyclePending || confirming} canHardDelete={canHardDelete} onLifecycle={onLifecycle} />}</section>
  </div>;
}

function SalesDetailTable({ details, labels, locale, pending, canHardDelete, onLifecycle }: { details: readonly SalesWorkspaceDetail[]; labels: Labels; locale: 'zh-TW' | 'th-TH'; pending: boolean; canHardDelete: boolean; onLifecycle: (input: LifecycleInput) => void }) {
  return <div className="sales-detail-table-wrap"><table className="sales-detail-table">
    <thead><tr><th>{labels.line}</th><th>{labels.product}</th><th>{labels.quantity}</th><th>{labels.pricing}</th><th>{labels.weight}</th><th>{labels.unitPrice}</th><th>{labels.amount}</th><th>{labels.actions}</th></tr></thead>
    <tbody>{details.map((detail) => <tr key={detail.id} className={detail.deletedAt ? 'is-deleted' : ''}>
      <td data-label={labels.line}>#{detail.lineNumber}</td><td data-label={labels.product}><strong>{detail.productDisplayName}</strong>{detail.deletedAt && <small>{labels.deleted}</small>}</td><td data-label={labels.quantity}>{formatNumber(detail.quantity, locale)}</td><td data-label={labels.pricing}>{pricingBasisLabel(detail.pricingBasis, labels)}</td><td data-label={labels.weight}>{detail.salesWeight === null ? '—' : formatNumber(detail.salesWeight, locale)}</td><td data-label={labels.unitPrice}>{formatNumber(detail.unitPrice, locale)}</td><td data-label={labels.amount}>{formatMoney(detail.amountThb, locale)}</td>
      <td data-label={labels.actions}><LifecycleButtons compact deleted={detail.deletedAt !== null} pending={pending} labels={labels} canHardDelete={canHardDelete} onSoft={() => onLifecycle({ target: 'detail', id: detail.id, action: 'soft-delete', rowVersion: detail.rowVersion })} onRestore={() => onLifecycle({ target: 'detail', id: detail.id, action: 'restore', rowVersion: detail.rowVersion })} onHard={() => onLifecycle({ target: 'detail', id: detail.id, action: 'hard-delete', rowVersion: detail.rowVersion })} /></td>
    </tr>)}</tbody>
  </table></div>;
}

function LifecycleButtons({ deleted, pending, labels, canHardDelete, onSoft, onRestore, onHard, compact = false }: { deleted: boolean; pending: boolean; labels: Labels; canHardDelete: boolean; onSoft: () => void; onRestore: () => void; onHard: () => void; compact?: boolean }) {
  return <span className={`sales-lifecycle-actions${compact ? ' is-compact' : ''}`}>{deleted ? <button type="button" className="sales-secondary-action" disabled={pending} onClick={onRestore}>{labels.restore}</button> : <button type="button" className="sales-secondary-action" disabled={pending} onClick={onSoft}>{labels.softDelete}</button>}{canHardDelete && <button type="button" className="sales-danger-action" disabled={pending} onClick={onHard}>{labels.hardDelete}</button>}</span>;
}
function HeaderField({ label, value }: { label: string; value: string }) { return <div><dt>{label}</dt><dd>{value}</dd></div>; }
function statusLabel(status: string, labels: Labels): string { if (status === 'DRAFT') return labels.draft; if (status === 'CONFIRMED') return labels.confirmed; return status; }
function pricingBasisLabel(value: string, labels: Labels): string { if (value === 'WEIGHT_BASED_UNIT') return labels.weightBased; if (value === 'UNIT_BASED') return labels.unitBased; return value; }
function formatDate(value: string, locale: 'zh-TW' | 'th-TH'): string { return new Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(`${value}T00:00:00`)); }
function formatNumber(value: number, locale: 'zh-TW' | 'th-TH'): string { return new Intl.NumberFormat(locale, { maximumFractionDigits: 6 }).format(value); }
function formatMoney(value: number, locale: 'zh-TW' | 'th-TH'): string { return new Intl.NumberFormat(locale, { style: 'currency', currency: 'THB', maximumFractionDigits: 0 }).format(value); }
