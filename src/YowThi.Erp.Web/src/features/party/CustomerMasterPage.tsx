import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useRef, useState, type FormEvent, type KeyboardEvent, type ReactNode } from 'react';
import { useOperationalLocale } from '../../app/i18n/locale';
import { ApiProblemError, changePartyLifecycle, type PartyLifecycleAction } from './partyLifecycle';
import { PartyMasterNavigation } from './PartyMasterNavigation';
import {
  createCustomer,
  listCustomers,
  updateCustomer,
  type CustomerDraftRequest,
  type CustomerMasterItem,
  type CustomerMasterStatus,
} from './customerMaster';
import './supplierMasterPrototype.css';

type EditorMode = 'detail' | 'create' | 'edit';
type CustomerDraft = {
  code: string;
  nameZhTw: string;
  nameThTh: string;
  phone: string;
  active: boolean;
};
type SubmissionIdentity = { fingerprint: string; idempotencyKey: string };

const copy = {
  'zh-TW': {
    eyebrow: '夥伴管理', title: '客戶', newCustomer: '新增客戶', search: '搜尋編號、名稱或電話',
    supplierTab: '供應商', customerTab: '客戶', all: '全部', active: '使用中', inactive: '停用', deleted: '已刪除', customer: '客戶', listName: '名稱', phone: '電話', status: '狀態', records: '筆資料',
    edit: '編輯', deactivate: '停用', activate: '啟用', softDelete: '刪除', restore: '恢復', basicInfo: '基本資料', contactInfo: '聯絡資料',
    code: '編號', nameZhTw: '中文名稱', nameThTh: '泰文名稱', activeState: '啟用狀態', nameRequired: '中文名稱與泰文名稱至少填寫一項。', save: '儲存', saving: '儲存中…', cancel: '取消', createTitle: '新增客戶', editTitle: '編輯客戶',
    deleteTitle: '刪除客戶', deleteMessage: '這會將資料設為軟刪除；之後可從「已刪除」恢復，並會出現在資料保護的永久刪除清單。', confirmDelete: '確認刪除',
    noResult: '沒有符合條件的客戶。', notProvided: '—', queryFailed: '無法載入客戶資料', unexpected: '操作失敗，請重新整理後再試。',
    stale: '資料已被其他操作更新，請重新整理後再試。', notFound: '客戶資料不存在。', deletedConflict: '這筆客戶已經刪除，請先恢復後再編輯。', idempotency: '相同操作識別已被其他內容使用。',
  },
  'th-TH': {
    eyebrow: 'จัดการคู่ค้า', title: 'ลูกค้า', newCustomer: 'เพิ่มลูกค้า', search: 'ค้นหารหัส ชื่อ หรือโทรศัพท์',
    supplierTab: 'ผู้จำหน่าย', customerTab: 'ลูกค้า', all: 'ทั้งหมด', active: 'ใช้งาน', inactive: 'ไม่ใช้งาน', deleted: 'ลบแล้ว', customer: 'ลูกค้า', listName: 'ชื่อ', phone: 'โทรศัพท์', status: 'สถานะ', records: 'รายการ',
    edit: 'แก้ไข', deactivate: 'ปิดใช้งาน', activate: 'เปิดใช้งาน', softDelete: 'ลบ', restore: 'กู้คืน', basicInfo: 'ข้อมูลพื้นฐาน', contactInfo: 'ข้อมูลติดต่อ',
    code: 'รหัส', nameZhTw: 'ชื่อภาษาจีน', nameThTh: 'ชื่อภาษาไทย', activeState: 'สถานะการใช้งาน', nameRequired: 'ต้องระบุชื่อภาษาจีนหรือชื่อภาษาไทยอย่างน้อยหนึ่งรายการ', save: 'บันทึก', saving: 'กำลังบันทึก…', cancel: 'ยกเลิก', createTitle: 'เพิ่มลูกค้า', editTitle: 'แก้ไขลูกค้า',
    deleteTitle: 'ลบลูกค้า', deleteMessage: 'รายการจะถูกลบแบบเก็บประวัติ สามารถกู้คืนได้ และจะแสดงในพื้นที่คุ้มครองสำหรับการลบถาวร', confirmDelete: 'ยืนยันการลบ',
    noResult: 'ไม่พบลูกค้าที่ตรงกับเงื่อนไข', notProvided: '—', queryFailed: 'ไม่สามารถโหลดข้อมูลลูกค้าได้', unexpected: 'ดำเนินการไม่สำเร็จ กรุณารีเฟรชแล้วลองใหม่',
    stale: 'ข้อมูลถูกแก้ไขโดยการทำงานอื่น กรุณารีเฟรชแล้วลองใหม่', notFound: 'ไม่พบข้อมูลลูกค้า', deletedConflict: 'ลูกค้านี้ถูกลบแล้ว กรุณากู้คืนก่อนแก้ไข', idempotency: 'รหัสการทำงานเดียวกันถูกใช้กับข้อมูลอื่นแล้ว',
  },
} as const;

type Labels = typeof copy['zh-TW'] | typeof copy['th-TH'];

function toDraft(item: CustomerMasterItem): CustomerDraft {
  return { code: item.code ?? '', nameZhTw: item.nameZhTw ?? '', nameThTh: item.nameThTh ?? '', phone: item.phone ?? '', active: item.active };
}
function emptyDraft(): CustomerDraft {
  return { code: '', nameZhTw: '', nameThTh: '', phone: '', active: true };
}
function toRequest(draft: CustomerDraft): CustomerDraftRequest {
  const optional = (value: string) => value.trim() || null;
  return { code: optional(draft.code), nameZhTw: optional(draft.nameZhTw), nameThTh: optional(draft.nameThTh), phone: optional(draft.phone), active: draft.active };
}

export function CustomerMasterPage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState('');
  const [mode, setMode] = useState<EditorMode>('detail');
  const [draft, setDraft] = useState<CustomerDraft>(emptyDraft);
  const [filter, setFilter] = useState<CustomerMasterStatus>('all');
  const [search, setSearch] = useState('');
  const deferredSearch = useDeferredValue(search);
  const [formError, setFormError] = useState<string | null>(null);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const writeIdentity = useRef<SubmissionIdentity | null>(null);
  const lifecycleIdentity = useRef<SubmissionIdentity | null>(null);

  const customersQuery = useQuery({
    queryKey: ['customer-master', locale, deferredSearch, filter],
    queryFn: ({ signal }) => listCustomers({ locale, search: deferredSearch, status: filter, limit: 200, signal }),
    staleTime: 2_000,
  });
  const items = customersQuery.data?.items ?? [];
  const selected = items.find((item) => item.id === selectedId) ?? (mode === 'detail' ? items[0] ?? null : null);

  async function invalidateCustomerData() {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['customer-master'] }),
      queryClient.invalidateQueries({ queryKey: ['party-lifecycle-options', 'customers'] }),
      queryClient.invalidateQueries({ queryKey: ['hard-delete-options', 'customers'] }),
    ]);
  }

  const createMutation = useMutation({
    mutationFn: (input: { request: CustomerDraftRequest; idempotencyKey: string }) => createCustomer(input.request, { locale, idempotencyKey: input.idempotencyKey }),
    onSuccess: async (result) => {
      setFilter('all'); setSelectedId(result.customerId); setMode('detail'); writeIdentity.current = null;
      await invalidateCustomerData();
    },
  });
  const updateMutation = useMutation({
    mutationFn: (input: { id: string; expectedRowVersion: number; request: CustomerDraftRequest; idempotencyKey: string }) =>
      updateCustomer(input.id, { ...input.request, expectedRowVersion: input.expectedRowVersion }, { locale, idempotencyKey: input.idempotencyKey }),
    onSuccess: async (result) => {
      setSelectedId(result.customerId); setMode('detail'); writeIdentity.current = null;
      await invalidateCustomerData();
    },
  });
  const lifecycleMutation = useMutation({
    mutationFn: (input: { id: string; action: PartyLifecycleAction; expectedRowVersion: number; idempotencyKey: string }) =>
      changePartyLifecycle('customers', input.id, input.action, { expectedRowVersion: input.expectedRowVersion }, { locale, idempotencyKey: input.idempotencyKey }),
    onSuccess: async (_, input) => {
      setFilter(input.action === 'soft-delete' ? 'deleted' : 'all'); setDeleteOpen(false); lifecycleIdentity.current = null;
      await invalidateCustomerData();
    },
  });

  const activeError = createMutation.error ?? updateMutation.error ?? lifecycleMutation.error ?? customersQuery.error;
  const problemMessage = useMemo(() => {
    if (!activeError) return null;
    if (!(activeError instanceof ApiProblemError)) return customersQuery.error ? labels.queryFailed : labels.unexpected;
    if (activeError.code === 'party.customer.stale-row-version' || activeError.code === 'concurrency.stale-row-version') return labels.stale;
    if (activeError.code === 'party.customer.not-found' || activeError.code === 'party.customer-not-found') return labels.notFound;
    if (activeError.code === 'party.customer.deleted') return labels.deletedConflict;
    if (activeError.code === 'party.customer.idempotency-key-reused' || activeError.code === 'idempotency.key-reused') return labels.idempotency;
    return `${activeError.code}${activeError.problem.traceId ? ` · ${activeError.problem.traceId}` : ''}`;
  }, [activeError, customersQuery.error, labels]);

  const displayName = (item: CustomerMasterItem) => {
    const primary = locale === 'zh-TW' ? item.nameZhTw : item.nameThTh;
    const fallback = locale === 'zh-TW' ? item.nameThTh : item.nameZhTw;
    return primary?.trim() || fallback?.trim() || labels.notProvided;
  };
  const secondaryName = (item: CustomerMasterItem) => (locale === 'zh-TW' ? item.nameThTh : item.nameZhTw)?.trim() ?? '';

  function selectCustomer(item: CustomerMasterItem) {
    setSelectedId(item.id); setDraft(toDraft(item)); setFormError(null); setMode('detail'); writeIdentity.current = null; lifecycleIdentity.current = null;
  }
  function handleListKey(event: KeyboardEvent<HTMLDivElement>, item: CustomerMasterItem) {
    if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); selectCustomer(item); }
  }
  function beginCreate() { setSelectedId(''); setDraft(emptyDraft()); setFormError(null); setMode('create'); writeIdentity.current = null; }
  function beginEdit() { if (selected) { setDraft(toDraft(selected)); setFormError(null); setMode('edit'); writeIdentity.current = null; } }
  function cancelEditor() { setMode('detail'); setFormError(null); writeIdentity.current = null; if (selected) setDraft(toDraft(selected)); }

  function saveDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const request = toRequest(draft);
    if (!request.nameZhTw && !request.nameThTh) { setFormError(labels.nameRequired); return; }
    setFormError(null);
    const fingerprint = JSON.stringify({ mode, selectedId, rowVersion: selected?.rowVersion ?? null, request });
    if (writeIdentity.current?.fingerprint !== fingerprint) writeIdentity.current = { fingerprint, idempotencyKey: crypto.randomUUID() };
    if (mode === 'create') createMutation.mutate({ request, idempotencyKey: writeIdentity.current.idempotencyKey });
    else if (mode === 'edit' && selected) updateMutation.mutate({ id: selected.id, expectedRowVersion: selected.rowVersion, request, idempotencyKey: writeIdentity.current.idempotencyKey });
  }
  function submitUpdateActive(active: boolean) {
    if (!selected || selected.deletedAt) return;
    const request = { ...toRequest(toDraft(selected)), active };
    const idempotencyKey = crypto.randomUUID();
    writeIdentity.current = { fingerprint: JSON.stringify({ id: selected.id, rowVersion: selected.rowVersion, request }), idempotencyKey };
    updateMutation.mutate({ id: selected.id, expectedRowVersion: selected.rowVersion, request, idempotencyKey });
  }
  function submitLifecycle(action: PartyLifecycleAction) {
    if (!selected) return;
    const fingerprint = JSON.stringify({ id: selected.id, action, rowVersion: selected.rowVersion });
    if (lifecycleIdentity.current?.fingerprint !== fingerprint) lifecycleIdentity.current = { fingerprint, idempotencyKey: crypto.randomUUID() };
    lifecycleMutation.mutate({ id: selected.id, action, expectedRowVersion: selected.rowVersion, idempotencyKey: lifecycleIdentity.current.idempotencyKey });
  }

  const busy = createMutation.isPending || updateMutation.isPending || lifecycleMutation.isPending;
  return (
    <section className="supplier-master-prototype" aria-labelledby="customer-master-title">
      <header className="supplier-master-header">
        <div><p className="eyebrow">{labels.eyebrow}</p><h1 id="customer-master-title">{labels.title}</h1></div>
        <div className="supplier-master-header-actions">
          <PartyMasterNavigation activeKind="customers" />
          <button className="supplier-primary-action" type="button" onClick={beginCreate} disabled={busy}><span aria-hidden="true">＋</span>{labels.newCustomer}</button>
        </div>
      </header>

      <div className="supplier-master-toolbar">
        <label className="supplier-search"><SearchIcon /><input type="search" value={search} aria-label={labels.search} onChange={(event) => setSearch(event.target.value)} /></label>
        <div className="supplier-filter-tabs" role="group" aria-label={labels.status}>
          {(['all', 'active', 'inactive', 'deleted'] as const).map((candidate) => <button key={candidate} type="button" className={filter === candidate ? 'is-active' : ''} onClick={() => { setFilter(candidate); setSelectedId(''); setMode('detail'); }}>{labels[candidate]}</button>)}
        </div>
      </div>

      <div className="supplier-master-grid">
        <aside className="supplier-list-panel supplier-master-record-list" aria-label={labels.customer}>
          <div className="supplier-list-meta"><strong>{labels.customer}</strong><span>{items.length} {labels.records}</span></div>
          <div className="supplier-list-heading" aria-hidden="true"><span>{labels.code}</span><span>{labels.listName}</span></div>
          <div className="supplier-list-body">
            {items.map((item) => <div key={item.id} role="button" tabIndex={0} className={`supplier-list-item${selected?.id === item.id ? ' is-selected' : ''}`} onClick={() => selectCustomer(item)} onKeyDown={(event) => handleListKey(event, item)}><span className="supplier-list-code">{item.code || labels.notProvided}</span><span className="supplier-list-name"><strong>{displayName(item)}</strong></span></div>)}
            {!customersQuery.isPending && items.length === 0 && <p className="supplier-list-empty">{labels.noResult}</p>}
          </div>
        </aside>

        <article className="supplier-detail-panel">
          {mode === 'create' || mode === 'edit' ? <CustomerEditor mode={mode} draft={draft} labels={labels} error={formError ?? problemMessage} busy={busy} onDraftChange={(next) => { setDraft(next); setFormError(null); writeIdentity.current = null; }} onSubmit={saveDraft} onCancel={cancelEditor} /> : selected ? <CustomerDetail item={selected} displayName={displayName(selected)} secondaryName={secondaryName(selected)} labels={labels} busy={busy} onEdit={beginEdit} onToggleActive={() => submitUpdateActive(!selected.active)} onDelete={() => setDeleteOpen(true)} onRestore={() => submitLifecycle('restore')} /> : <div className="supplier-list-empty">{customersQuery.isPending ? '…' : problemMessage ?? labels.noResult}</div>}
        </article>
      </div>

      {deleteOpen && selected && <div className="supplier-dialog-backdrop" role="presentation" onMouseDown={() => setDeleteOpen(false)}><div className="supplier-dialog" role="dialog" aria-modal="true" aria-labelledby="customer-delete-dialog-title" onMouseDown={(event) => event.stopPropagation()}><div className="supplier-dialog-icon" aria-hidden="true">!</div><div><h2 id="customer-delete-dialog-title">{labels.deleteTitle}</h2><p>{displayName(selected)}</p><p className="supplier-dialog-message">{labels.deleteMessage}</p></div><div className="supplier-dialog-actions"><button type="button" className="supplier-secondary-action" onClick={() => setDeleteOpen(false)}>{labels.cancel}</button><button type="button" className="supplier-danger-action" disabled={busy} onClick={() => submitLifecycle('soft-delete')}>{labels.confirmDelete}</button></div></div></div>}
    </section>
  );
}

function CustomerDetail({ item, displayName, secondaryName, labels, busy, onEdit, onToggleActive, onDelete, onRestore }: { item: CustomerMasterItem; displayName: string; secondaryName: string; labels: Labels; busy: boolean; onEdit: () => void; onToggleActive: () => void; onDelete: () => void; onRestore: () => void }) {
  return <><header className="supplier-detail-header"><div><div className="supplier-detail-title-row"><h2>{displayName}</h2><StatusPill item={item} labels={labels} /></div>{secondaryName && <p>{secondaryName}</p>}</div><div className="supplier-detail-actions">{item.deletedAt ? <button className="supplier-primary-action" type="button" disabled={busy} onClick={onRestore}>{labels.restore}</button> : <><button className="supplier-secondary-action" type="button" disabled={busy} onClick={onEdit}>{labels.edit}</button><button className="supplier-secondary-action" type="button" disabled={busy} onClick={onToggleActive}>{item.active ? labels.deactivate : labels.activate}</button><button className="supplier-danger-ghost-action" type="button" disabled={busy} onClick={onDelete}>{labels.softDelete}</button></>}</div></header><DetailSection title={labels.basicInfo}><DetailField label={labels.code} value={item.code} fallback={labels.notProvided} /><DetailField label={labels.nameZhTw} value={item.nameZhTw} fallback={labels.notProvided} /><DetailField label={labels.nameThTh} value={item.nameThTh} fallback={labels.notProvided} /></DetailSection><DetailSection title={labels.contactInfo}><DetailField label={labels.phone} value={item.phone} fallback={labels.notProvided} /></DetailSection></>;
}
function CustomerEditor({ mode, draft, labels, error, busy, onDraftChange, onSubmit, onCancel }: { mode: 'create' | 'edit'; draft: CustomerDraft; labels: Labels; error: string | null; busy: boolean; onDraftChange: (draft: CustomerDraft) => void; onSubmit: (event: FormEvent<HTMLFormElement>) => void; onCancel: () => void }) {
  const update = <K extends keyof CustomerDraft>(key: K, value: CustomerDraft[K]) => onDraftChange({ ...draft, [key]: value });
  return <form className="supplier-editor" onSubmit={onSubmit}><header className="supplier-detail-header supplier-editor-header"><h2>{mode === 'create' ? labels.createTitle : labels.editTitle}</h2></header><FormSection title={labels.basicInfo}><Field label={labels.code}><input value={draft.code} onChange={(event) => update('code', event.target.value)} /></Field><Field label={labels.nameZhTw}><input value={draft.nameZhTw} onChange={(event) => update('nameZhTw', event.target.value)} /></Field><Field label={labels.nameThTh}><input value={draft.nameThTh} onChange={(event) => update('nameThTh', event.target.value)} /></Field><fieldset className="supplier-state-fieldset supplier-form-wide"><legend className="field-label">{labels.activeState}</legend><label className={draft.active ? 'is-selected' : ''}><input type="radio" name="customer-active-state" checked={draft.active} onChange={() => update('active', true)} />{labels.active}</label><label className={!draft.active ? 'is-selected' : ''}><input type="radio" name="customer-active-state" checked={!draft.active} onChange={() => update('active', false)} />{labels.inactive}</label></fieldset></FormSection><FormSection title={labels.contactInfo}><Field label={labels.phone}><input value={draft.phone} inputMode="tel" onChange={(event) => update('phone', event.target.value)} /></Field></FormSection>{error && <div className="problem-banner" role="alert">{error}</div>}<footer className="supplier-editor-footer"><button className="supplier-secondary-action" type="button" disabled={busy} onClick={onCancel}>{labels.cancel}</button><button className="supplier-primary-action" type="submit" disabled={busy}>{busy ? labels.saving : labels.save}</button></footer></form>;
}
function DetailSection({ title, children }: { title: string; children: ReactNode }) { return <section className="supplier-detail-section"><h3>{title}</h3><div className="supplier-detail-fields">{children}</div></section>; }
function FormSection({ title, children }: { title: string; children: ReactNode }) { return <section className="supplier-detail-section"><h3>{title}</h3><div className="supplier-form-grid">{children}</div></section>; }
function DetailField({ label, value, fallback }: { label: string; value: string | null; fallback: string }) { return <div className="supplier-detail-field"><dt>{label}</dt><dd>{value || fallback}</dd></div>; }
function Field({ label, children }: { label: string; children: ReactNode }) { return <label><span className="field-label">{label}</span>{children}</label>; }
function StatusPill({ item, labels }: { item: CustomerMasterItem; labels: Labels }) { const state = item.deletedAt ? 'deleted' : item.active ? 'active' : 'inactive'; return <span className={`supplier-status-pill is-${state}`}>{labels[state]}</span>; }
function SearchIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="6.5" /><path d="m16 16 4 4" /></svg>; }
