import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useState } from 'react';
import { Link } from 'react-router';

import { ApiProblemError } from '../../app/api/apiTransport';
import { useOperationalLocale } from '../../app/i18n/locale';
import { getAuthenticationSession, prepareDeletionReauthentication } from '../../app/security/authSession';
import {
  changeOutsourcedLifecycle,
  type OutsourcedLifecycleAction,
  type OutsourcedLifecycleTarget,
} from './outsourcedTransactionLifecycle';
import {
  getOutsourcedWorkspace,
  listOutsourcedWorkspace,
  type OutsourcedWorkspaceDetail,
} from './outsourcedWorkspace';
import './OutsourcedLifecycleWorkspace.css';

const copy = {
  'zh-TW': {
    title: '委外供應紀錄', newDetail: '新增委外明細', search: '搜尋委外商', loading: '載入中', unavailable: '資料載入失敗', empty: '目前沒有委外供應紀錄',
    date: '日期', vendor: '委外商', status: '狀態', active: '啟用', closed: '已關閉', deleted: '已刪除', details: '委外供應明細', noDetails: '目前沒有明細',
    product: '產品', quantity: '數量', pricing: '計價', unitPrice: '單價', amount: '金額', actions: '操作', softDelete: '刪除', restore: '還原', hardDelete: '永久刪除',
    weightBased: '依重量計價', unitBased: '依件數計價', stale: '資料已更新，請重新載入。', notFound: '資料已不存在，請重新載入。', dependency: '相關資料無法安全關閉，未執行刪除。', reauth: '刪除前需要重新驗證登入身分。', unexpected: '操作失敗。',
  },
  'th-TH': {
    title: 'ประวัติการจัดหาภายนอก', newDetail: 'เพิ่มรายละเอียดงานภายนอก', search: 'ค้นหาผู้รับจ้าง', loading: 'กำลังโหลด', unavailable: 'โหลดข้อมูลไม่สำเร็จ', empty: 'ยังไม่มีประวัติการจัดหาภายนอก',
    date: 'วันที่', vendor: 'ผู้รับจ้างภายนอก', status: 'สถานะ', active: 'ใช้งาน', closed: 'ปิดแล้ว', deleted: 'ลบแล้ว', details: 'รายละเอียดการจัดหาภายนอก', noDetails: 'ยังไม่มีรายละเอียด',
    product: 'สินค้า', quantity: 'ปริมาณ', pricing: 'เกณฑ์ราคา', unitPrice: 'ราคาต่อหน่วย', amount: 'จำนวนเงิน', actions: 'จัดการ', softDelete: 'ลบ', restore: 'กู้คืน', hardDelete: 'ลบถาวร',
    weightBased: 'คิดราคาตามน้ำหนัก', unitBased: 'คิดราคาต่อหน่วย', stale: 'ข้อมูลถูกเปลี่ยนแล้ว โปรดโหลดใหม่', notFound: 'ไม่พบข้อมูลแล้ว โปรดโหลดใหม่', dependency: 'ไม่สามารถปิดข้อมูลที่เกี่ยวข้องได้อย่างปลอดภัย จึงยังไม่ได้ลบ', reauth: 'ต้องยืนยันตัวตนอีกครั้งก่อนลบ', unexpected: 'ดำเนินการไม่สำเร็จ',
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

  const sessionQuery = useQuery({
    queryKey: ['authentication-session'],
    queryFn: ({ signal }) => getAuthenticationSession(signal),
    staleTime: 30_000,
  });
  const canLifecycle = sessionQuery.data?.capabilities.includes('outsourced.transaction.lifecycle') ?? false;
  const canHardDelete = canLifecycle && (sessionQuery.data?.capabilities.includes('data-protection.hard-delete') ?? false);

  const listQuery = useQuery({
    queryKey: ['outsourced-workspace', 'list', locale, deferredSearch],
    queryFn: ({ signal }) => listOutsourcedWorkspace({ locale, search: deferredSearch, offset: 0, limit: 100, signal }),
    staleTime: 2_000,
  });
  const items = listQuery.data?.items ?? [];
  const effectiveBatchId = selectedBatchId ?? items[0]?.id ?? null;
  const workspaceQuery = useQuery({
    queryKey: ['outsourced-workspace', 'item', effectiveBatchId, locale],
    queryFn: ({ signal }) => getOutsourcedWorkspace(effectiveBatchId!, locale, signal),
    enabled: effectiveBatchId !== null,
    staleTime: 1_000,
  });

  const lifecycleMutation = useMutation({
    mutationFn: async (input: LifecycleInput) => {
      if (input.action !== 'restore') await prepareDeletionReauthentication();
      return changeOutsourcedLifecycle(
        input.target,
        input.id,
        input.action,
        { expectedRowVersion: input.rowVersion },
        { idempotencyKey: crypto.randomUUID(), locale },
      );
    },
    onSuccess: async (_, input) => {
      if (input.target === 'batch' && input.action === 'hard-delete') setSelectedBatchId(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['outsourced-workspace'] }),
        queryClient.invalidateQueries({ queryKey: ['hard-delete-options', input.target === 'batch' ? 'outsourced-supply-batches' : 'outsourced-supply-details'] }),
      ]);
    },
  });

  const problem = useMemo(() => {
    const error = lifecycleMutation.error;
    if (!error) return null;
    if (!(error instanceof ApiProblemError)) return labels.unexpected;
    if (error.code === 'concurrency.stale-row-version') return labels.stale;
    if (error.code === 'security.deletion-reauth-required') return labels.reauth;
    if (error.code.endsWith('-not-found')) return labels.notFound;
    if (error.code.endsWith('-closure-invalid') || error.code.endsWith('-dependency-blocked')) return labels.dependency;
    return error.code;
  }, [labels, lifecycleMutation.error]);

  const workspace = workspaceQuery.data ?? null;

  return (
    <section className="outsourced-workspace" aria-labelledby="outsourced-workspace-title">
      <header className="outsourced-workspace-heading">
        <h2 id="outsourced-workspace-title">{labels.title}</h2>
        <Link className="outsourced-secondary-action" to="/outsourced/supply-details/new">{labels.newDetail}</Link>
      </header>
      <div className="outsourced-workspace-layout">
        <aside className="outsourced-batch-list">
          <label><span>{labels.search}</span><input type="search" value={search} onChange={(event) => setSearch(event.target.value)} /></label>
          <div className="outsourced-batch-items">
            {listQuery.isPending && <div className="outsourced-state">{labels.loading}</div>}
            {listQuery.isError && <div className="problem-banner" role="alert">{labels.unavailable}</div>}
            {!listQuery.isPending && !listQuery.isError && items.length === 0 && <div className="outsourced-state">{labels.empty}</div>}
            {items.map((item) => (
              <button key={item.id} type="button" className={`outsourced-batch-item${effectiveBatchId === item.id ? ' is-selected' : ''}${item.deletedAt ? ' is-deleted' : ''}`} onClick={() => { setSelectedBatchId(item.id); lifecycleMutation.reset(); }}>
                <strong>{item.outsourcedVendorDisplayName}</strong>
                <span>{formatDate(item.supplyDate, locale)}</span>
                <small>{item.deletedAt ? labels.deleted : statusLabel(item.lifecycleStatus, labels)}</small>
              </button>
            ))}
          </div>
        </aside>

        <div className="outsourced-document">
          {problem && <div className="problem-banner" role="alert">{problem}</div>}
          {workspaceQuery.isPending && effectiveBatchId && <div className="outsourced-state">{labels.loading}</div>}
          {workspaceQuery.isError && <div className="problem-banner" role="alert">{labels.unavailable}</div>}
          {workspace && <>
            <header className="outsourced-document-header">
              <div><span>{labels.vendor}</span><strong>{workspace.outsourcedVendorDisplayName}</strong><small>{formatDate(workspace.supplyDate, locale)} · {workspace.deletedAt ? labels.deleted : statusLabel(workspace.lifecycleStatus, labels)}</small></div>
              <LifecycleButtons deleted={workspace.deletedAt !== null} pending={lifecycleMutation.isPending} canLifecycle={canLifecycle} canHardDelete={canHardDelete} labels={labels} onSoft={() => lifecycleMutation.mutate({ target: 'batch', id: workspace.id, action: 'soft-delete', rowVersion: workspace.rowVersion })} onRestore={() => lifecycleMutation.mutate({ target: 'batch', id: workspace.id, action: 'restore', rowVersion: workspace.rowVersion })} onHard={() => lifecycleMutation.mutate({ target: 'batch', id: workspace.id, action: 'hard-delete', rowVersion: workspace.rowVersion })} />
            </header>
            <section className="outsourced-details"><h3>{labels.details}</h3>{workspace.details.length === 0 ? <div className="outsourced-state">{labels.noDetails}</div> : <DetailTable details={workspace.details} labels={labels} locale={locale} pending={lifecycleMutation.isPending} canLifecycle={canLifecycle} canHardDelete={canHardDelete} onLifecycle={(input) => lifecycleMutation.mutate(input)} />}</section>
          </>}
        </div>
      </div>
    </section>
  );
}

function DetailTable({ details, labels, locale, pending, canLifecycle, canHardDelete, onLifecycle }: { details: readonly OutsourcedWorkspaceDetail[]; labels: Labels; locale: 'zh-TW' | 'th-TH'; pending: boolean; canLifecycle: boolean; canHardDelete: boolean; onLifecycle: (input: LifecycleInput) => void }) {
  return <div className="outsourced-detail-wrap"><table className="outsourced-detail-table"><thead><tr><th>{labels.product}</th><th>{labels.quantity}</th><th>{labels.pricing}</th><th>{labels.unitPrice}</th><th>{labels.amount}</th><th>{labels.actions}</th></tr></thead><tbody>{details.map((detail) => <tr key={detail.id} className={detail.deletedAt ? 'is-deleted' : ''}>
    <td data-label={labels.product}><strong>{detail.salesProductDisplayName}</strong>{detail.deletedAt && <small>{labels.deleted}</small>}</td>
    <td data-label={labels.quantity}>{formatNumber(detail.quantity, locale)}</td>
    <td data-label={labels.pricing}>{pricingLabel(detail.pricingBasis, labels)}</td>
    <td data-label={labels.unitPrice}>{formatNumber(detail.unitPrice, locale)}</td>
    <td data-label={labels.amount}>{formatMoney(detail.amountThb, locale)}</td>
    <td data-label={labels.actions}><LifecycleButtons compact deleted={detail.deletedAt !== null} pending={pending} canLifecycle={canLifecycle} canHardDelete={canHardDelete} labels={labels} onSoft={() => onLifecycle({ target: 'detail', id: detail.id, action: 'soft-delete', rowVersion: detail.rowVersion })} onRestore={() => onLifecycle({ target: 'detail', id: detail.id, action: 'restore', rowVersion: detail.rowVersion })} onHard={() => onLifecycle({ target: 'detail', id: detail.id, action: 'hard-delete', rowVersion: detail.rowVersion })} /></td>
  </tr>)}</tbody></table></div>;
}

function LifecycleButtons({ deleted, pending, canLifecycle, canHardDelete, labels, onSoft, onRestore, onHard, compact = false }: { deleted: boolean; pending: boolean; canLifecycle: boolean; canHardDelete: boolean; labels: Labels; onSoft: () => void; onRestore: () => void; onHard: () => void; compact?: boolean }) {
  if (!canLifecycle && !canHardDelete) return null;
  return <span className={`outsourced-lifecycle-actions${compact ? ' is-compact' : ''}`}>{canLifecycle && (deleted ? <button type="button" className="outsourced-secondary-action" disabled={pending} onClick={onRestore}>{labels.restore}</button> : <button type="button" className="outsourced-secondary-action" disabled={pending} onClick={onSoft}>{labels.softDelete}</button>)}{canHardDelete && <button type="button" className="outsourced-danger-action" disabled={pending} onClick={onHard}>{labels.hardDelete}</button>}</span>;
}
function statusLabel(status: string, labels: Labels): string { if (status === 'ACTIVE') return labels.active; if (status === 'CLOSED') return labels.closed; return status; }
function pricingLabel(value: string, labels: Labels): string { if (value === 'WEIGHT_BASED_UNIT') return labels.weightBased; if (value === 'UNIT_BASED') return labels.unitBased; return value; }
function formatDate(value: string, locale: 'zh-TW' | 'th-TH'): string { return new Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(`${value}T00:00:00`)); }
function formatNumber(value: number, locale: 'zh-TW' | 'th-TH'): string { return new Intl.NumberFormat(locale, { maximumFractionDigits: 6 }).format(value); }
function formatMoney(value: number, locale: 'zh-TW' | 'th-TH'): string { return new Intl.NumberFormat(locale, { style: 'currency', currency: 'THB', maximumFractionDigits: 0 }).format(value); }
