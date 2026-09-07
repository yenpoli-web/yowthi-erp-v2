import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useRef, useState, type FormEvent, type KeyboardEvent, type ReactNode } from 'react';
import { useOperationalLocale } from '../../app/i18n/locale';
import { ApiProblemError, changePartyLifecycle, type PartyLifecycleAction } from './partyLifecycle';
import { PartyMasterNavigation } from './PartyMasterNavigation';
import {
  createSupplier,
  listSuppliers,
  updateSupplier,
  type SupplierDraftRequest,
  type SupplierMasterItem,
  type SupplierMasterStatus,
} from './supplierMaster';
import './supplierMasterPrototype.css';

type EditorMode = 'detail' | 'create' | 'edit';
type SupplierDraft = {
  nameZhTw: string;
  nameThTh: string;
  phone: string;
  address: string;
  bankName: string;
  bankAccount: string;
  active: boolean;
};
type SubmissionIdentity = { fingerprint: string; idempotencyKey: string };

const copy = {
  'zh-TW': {
    eyebrow: '夥伴管理', title: '供應商', newSupplier: '新增供應商', search: '搜尋名稱、電話或銀行', supplierTab: '供應商', customerTab: '客戶',
    all: '全部', active: '使用中', inactive: '停用', deleted: '已刪除', supplier: '供應商', phone: '電話', status: '狀態', records: '筆資料',
    edit: '編輯', deactivate: '停用', activate: '啟用', softDelete: '刪除', restore: '恢復', basicInfo: '基本資料',
    nameZhTw: '中文名稱', nameThTh: '泰文名稱', contactInfo: '聯絡資料', address: '地址', paymentInfo: '銀行資料', bankName: '銀行名稱', bankAccount: '銀行帳號',
    activeState: '啟用狀態', nameRequired: '中文名稱與泰文名稱至少填寫一項。', save: '儲存', saving: '儲存中…', cancel: '取消', createTitle: '新增供應商', editTitle: '編輯供應商',
    deleteTitle: '刪除供應商', deleteMessage: '這會將資料設為軟刪除；之後可從「已刪除」恢復，並會出現在資料保護的永久刪除清單。', confirmDelete: '確認刪除',
    noResult: '沒有符合條件的供應商。', notProvided: '—', queryFailed: '無法載入供應商資料', unexpected: '操作失敗，請重新整理後再試。',
    stale: '資料已被其他操作更新，請重新整理後再試。', notFound: '供應商資料不存在。', deletedConflict: '這筆供應商已經刪除，請先恢復後再編輯。', idempotency: '相同操作識別已被其他內容使用。',
  },
  'th-TH': {
    eyebrow: 'จัดการคู่ค้า', title: 'ผู้จำหน่าย', newSupplier: 'เพิ่มผู้จำหน่าย', search: 'ค้นหาชื่อ โทรศัพท์ หรือธนาคาร', supplierTab: 'ผู้จำหน่าย', customerTab: 'ลูกค้า',
    all: 'ทั้งหมด', active: 'ใช้งาน', inactive: 'ไม่ใช้งาน', deleted: 'ลบแล้ว', supplier: 'ผู้จำหน่าย', phone: 'โทรศัพท์', status: 'สถานะ', records: 'รายการ',
    edit: 'แก้ไข', deactivate: 'ปิดใช้งาน', activate: 'เปิดใช้งาน', softDelete: 'ลบ', restore: 'กู้คืน', basicInfo: 'ข้อมูลพื้นฐาน',
    nameZhTw: 'ชื่อภาษาจีน', nameThTh: 'ชื่อภาษาไทย', contactInfo: 'ข้อมูลติดต่อ', address: 'ที่อยู่', paymentInfo: 'ข้อมูลธนาคาร', bankName: 'ชื่อธนาคาร', bankAccount: 'เลขบัญชีธนาคาร',
    activeState: 'สถานะการใช้งาน', nameRequired: 'ต้องระบุชื่อภาษาจีนหรือชื่อภาษาไทยอย่างน้อยหนึ่งรายการ', save: 'บันทึก', saving: 'กำลังบันทึก…', cancel: 'ยกเลิก', createTitle: 'เพิ่มผู้จำหน่าย', editTitle: 'แก้ไขผู้จำหน่าย',
    deleteTitle: 'ลบผู้จำหน่าย', deleteMessage: 'รายการจะถูกลบแบบเก็บประวัติ สามารถกู้คืนได้ และจะแสดงในพื้นที่คุ้มครองสำหรับการลบถาวร', confirmDelete: 'ยืนยันการลบ',
    noResult: 'ไม่พบผู้จำหน่ายที่ตรงกับเงื่อนไข', notProvided: '—', queryFailed: 'ไม่สามารถโหลดข้อมูลผู้จำหน่ายได้', unexpected: 'ดำเนินการไม่สำเร็จ กรุณารีเฟรชแล้วลองใหม่',
    stale: 'ข้อมูลถูกแก้ไขโดยการทำงานอื่น กรุณารีเฟรชแล้วลองใหม่', notFound: 'ไม่พบข้อมูลผู้จำหน่าย', deletedConflict: 'ผู้จำหน่ายนี้ถูกลบแล้ว กรุณากู้คืนก่อนแก้ไข', idempotency: 'รหัสการทำงานเดียวกันถูกใช้กับข้อมูลอื่นแล้ว',
  },
} as const;

type Labels = typeof copy['zh-TW'] | typeof copy['th-TH'];

function toDraft(item: SupplierMasterItem): SupplierDraft {
  return {
    nameZhTw: item.nameZhTw ?? '', nameThTh: item.nameThTh ?? '', phone: item.phone ?? '', address: item.address ?? '',
    bankName: item.bankName ?? '', bankAccount: item.bankAccount ?? '', active: item.active,
  };
}
function emptyDraft(): SupplierDraft {
  return { nameZhTw: '', nameThTh: '', phone: '', address: '', bankName: '', bankAccount: '', active: true };
}
function toRequest(draft: SupplierDraft): SupplierDraftRequest {
  const optional = (value: string) => value.trim() || null;
  return {
    nameZhTw: optional(draft.nameZhTw), nameThTh: optional(draft.nameThTh), phone: optional(draft.phone), address: optional(draft.address),
    bankName: optional(draft.bankName), bankAccount: optional(draft.bankAccount), active: draft.active,
  };
}

export function SupplierMasterPage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState('');
  const [mode, setMode] = useState<EditorMode>('detail');
  const [draft, setDraft] = useState<SupplierDraft>(emptyDraft);
  const [filter, setFilter] = useState<SupplierMasterStatus>('all');
  const [search, setSearch] = useState('');
  const deferredSearch = useDeferredValue(search);
  const [formError, setFormError] = useState<string | null>(null);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const writeIdentity = useRef<SubmissionIdentity | null>(null);
  const lifecycleIdentity = useRef<SubmissionIdentity | null>(null);

  const suppliersQuery = useQuery({
    queryKey: ['supplier-master', locale, deferredSearch, filter],
    queryFn: ({ signal }) => listSuppliers({ locale, search: deferredSearch, status: filter, limit: 200, signal }),
    staleTime: 2_000,
  });
  const items = suppliersQuery.data?.items ?? [];
  const selected = items.find((item) => item.id === selectedId)
    ?? (mode === 'detail' ? items[0] ?? null : null);

  async function invalidateSupplierData() {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['supplier-master'] }),
      queryClient.invalidateQueries({ queryKey: ['party-lifecycle-options', 'suppliers'] }),
      queryClient.invalidateQueries({ queryKey: ['hard-delete-options', 'suppliers'] }),
    ]);
  }

  const createMutation = useMutation({
    mutationFn: (input: { request: SupplierDraftRequest; idempotencyKey: string }) => createSupplier(input.request, { locale, idempotencyKey: input.idempotencyKey }),
    onSuccess: async (result) => {
      setFilter('all'); setSelectedId(result.supplierId); setMode('detail'); writeIdentity.current = null;
      await invalidateSupplierData();
    },
  });
  const updateMutation = useMutation({
    mutationFn: (input: { id: string; expectedRowVersion: number; request: SupplierDraftRequest; idempotencyKey: string }) =>
      updateSupplier(input.id, { ...input.request, expectedRowVersion: input.expectedRowVersion }, { locale, idempotencyKey: input.idempotencyKey }),
    onSuccess: async (result) => {
      setSelectedId(result.supplierId); setMode('detail'); writeIdentity.current = null;
      await invalidateSupplierData();
    },
  });
  const lifecycleMutation = useMutation({
    mutationFn: (input: { id: string; action: PartyLifecycleAction; expectedRowVersion: number; idempotencyKey: string }) =>
      changePartyLifecycle('suppliers', input.id, input.action, { expectedRowVersion: input.expectedRowVersion }, { locale, idempotencyKey: input.idempotencyKey }),
    onSuccess: async (_, input) => {
      setFilter(input.action === 'soft-delete' ? 'deleted' : 'all'); setDeleteOpen(false); lifecycleIdentity.current = null;
      await invalidateSupplierData();
    },
  });

  const activeError = createMutation.error ?? updateMutation.error ?? lifecycleMutation.error ?? suppliersQuery.error;
  const problemMessage = useMemo(() => {
    if (!activeError) return null;
    if (!(activeError instanceof ApiProblemError)) return suppliersQuery.error ? labels.queryFailed : labels.unexpected;
    if (activeError.code === 'party.supplier.stale-row-version' || activeError.code === 'concurrency.stale-row-version') return labels.stale;
    if (activeError.code === 'party.supplier.not-found') return labels.notFound;
    if (activeError.code === 'party.supplier.deleted') return labels.deletedConflict;
    if (activeError.code === 'party.supplier.idempotency-key-reused' || activeError.code === 'idempotency.key-reused') return labels.idempotency;
    return `${activeError.code}${activeError.problem.traceId ? ` · ${activeError.problem.traceId}` : ''}`;
  }, [activeError, labels, suppliersQuery.error]);

  const displayName = (item: SupplierMasterItem) => {
    const primary = locale === 'zh-TW' ? item.nameZhTw : item.nameThTh;
    const fallback = locale === 'zh-TW' ? item.nameThTh : item.nameZhTw;
    return primary?.trim() || fallback?.trim() || labels.notProvided;
  };
  const secondaryName = (item: SupplierMasterItem) => (locale === 'zh-TW' ? item.nameThTh : item.nameZhTw)?.trim() ?? '';

  function selectSupplier(item: SupplierMasterItem) {
    setSelectedId(item.id); setDraft(toDraft(item)); setFormError(null); setMode('detail'); writeIdentity.current = null; lifecycleIdentity.current = null;
  }
  function handleListKey(event: KeyboardEvent<HTMLDivElement>, item: SupplierMasterItem) {
    if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); selectSupplier(item); }
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
    const fingerprint = JSON.stringify({ id: selected.id, rowVersion: selected.rowVersion, request });
    const idempotencyKey = crypto.randomUUID();
    writeIdentity.current = { fingerprint, idempotencyKey };
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
    <section className="supplier-master-prototype" aria-labelledby="supplier-master-title">
      <header className="supplier-master-header">
        <div><p className="eyebrow">{labels.eyebrow}</p><h1 id="supplier-master-title">{labels.title}</h1></div>
        <div className="supplier-master-header-actions"><PartyMasterNavigation activeKind="suppliers" /><button className="supplier-primary-action" type="button" onClick={beginCreate} disabled={busy}><span aria-hidden="true">＋</span>{labels.newSupplier}</button></div>
      </header>

      <div className="supplier-master-toolbar">
        <label className="supplier-search"><SearchIcon /><input type="search" value={search} aria-label={labels.search} onChange={(event) => setSearch(event.target.value)} /></label>
        <div className="supplier-filter-tabs" role="group" aria-label={labels.status}>
          {(['all', 'active', 'inactive', 'deleted'] as const).map((candidate) => <button key={candidate} type="button" className={filter === candidate ? 'is-active' : ''} onClick={() => { setFilter(candidate); setSelectedId(''); setMode('detail'); }}>{labels[candidate]}</button>)}
        </div>
      </div>

      <div className="supplier-master-grid">
        <aside className="supplier-list-panel" aria-label={labels.supplier}>
          <div className="supplier-list-meta"><strong>{labels.supplier}</strong><span>{items.length} {labels.records}</span></div>
          <div className="supplier-list-heading" aria-hidden="true"><span>{labels.supplier}</span><span>{labels.phone}</span><span>{labels.status}</span></div>
          <div className="supplier-list-body">
            {items.map((item) => <div key={item.id} role="button" tabIndex={0} className={`supplier-list-item${selected?.id === item.id ? ' is-selected' : ''}`} onClick={() => selectSupplier(item)} onKeyDown={(event) => handleListKey(event, item)}><span className="supplier-list-name"><strong>{displayName(item)}</strong>{secondaryName(item) && <small>{secondaryName(item)}</small>}</span><span className="supplier-list-phone">{item.phone || labels.notProvided}</span><StatusPill item={item} labels={labels} /></div>)}
            {!suppliersQuery.isPending && items.length === 0 && <p className="supplier-list-empty">{labels.noResult}</p>}
          </div>
        </aside>

        <article className="supplier-detail-panel">
          {mode === 'create' || mode === 'edit' ? <SupplierEditor mode={mode} draft={draft} labels={labels} error={formError ?? problemMessage} busy={busy} onDraftChange={(next) => { setDraft(next); setFormError(null); writeIdentity.current = null; }} onSubmit={saveDraft} onCancel={cancelEditor} /> : selected ? <SupplierDetail item={selected} displayName={displayName(selected)} secondaryName={secondaryName(selected)} labels={labels} busy={busy} onEdit={beginEdit} onToggleActive={() => submitUpdateActive(!selected.active)} onDelete={() => setDeleteOpen(true)} onRestore={() => submitLifecycle('restore')} /> : <div className="supplier-list-empty">{suppliersQuery.isPending ? '…' : problemMessage ?? labels.noResult}</div>}
        </article>
      </div>

      {deleteOpen && selected && <div className="supplier-dialog-backdrop" role="presentation" onMouseDown={() => setDeleteOpen(false)}><div className="supplier-dialog" role="dialog" aria-modal="true" aria-labelledby="supplier-delete-dialog-title" onMouseDown={(event) => event.stopPropagation()}><div className="supplier-dialog-icon" aria-hidden="true">!</div><div><h2 id="supplier-delete-dialog-title">{labels.deleteTitle}</h2><p>{displayName(selected)}</p><p className="supplier-dialog-message">{labels.deleteMessage}</p></div><div className="supplier-dialog-actions"><button type="button" className="supplier-secondary-action" onClick={() => setDeleteOpen(false)}>{labels.cancel}</button><button type="button" className="supplier-danger-action" disabled={busy} onClick={() => submitLifecycle('soft-delete')}>{labels.confirmDelete}</button></div></div></div>}
    </section>
  );
}

function SupplierDetail({ item, displayName, secondaryName, labels, busy, onEdit, onToggleActive, onDelete, onRestore }: { item: SupplierMasterItem; displayName: string; secondaryName: string; labels: Labels; busy: boolean; onEdit: () => void; onToggleActive: () => void; onDelete: () => void; onRestore: () => void }) {
  return <><header className="supplier-detail-header"><div><div className="supplier-detail-title-row"><h2>{displayName}</h2><StatusPill item={item} labels={labels} /></div>{secondaryName && <p>{secondaryName}</p>}</div><div className="supplier-detail-actions">{item.deletedAt ? <button className="supplier-primary-action" type="button" disabled={busy} onClick={onRestore}>{labels.restore}</button> : <><button className="supplier-secondary-action" type="button" disabled={busy} onClick={onEdit}>{labels.edit}</button><button className="supplier-secondary-action" type="button" disabled={busy} onClick={onToggleActive}>{item.active ? labels.deactivate : labels.activate}</button><button className="supplier-danger-ghost-action" type="button" disabled={busy} onClick={onDelete}>{labels.softDelete}</button></>}</div></header><DetailSection title={labels.basicInfo}><DetailField label={labels.nameZhTw} value={item.nameZhTw} fallback={labels.notProvided} /><DetailField label={labels.nameThTh} value={item.nameThTh} fallback={labels.notProvided} /></DetailSection><DetailSection title={labels.contactInfo}><DetailField label={labels.phone} value={item.phone} fallback={labels.notProvided} /><DetailField label={labels.address} value={item.address} fallback={labels.notProvided} wide /></DetailSection><DetailSection title={labels.paymentInfo}><DetailField label={labels.bankName} value={item.bankName} fallback={labels.notProvided} /><DetailField label={labels.bankAccount} value={item.bankAccount} fallback={labels.notProvided} /></DetailSection></>;
}
function SupplierEditor({ mode, draft, labels, error, busy, onDraftChange, onSubmit, onCancel }: { mode: 'create' | 'edit'; draft: SupplierDraft; labels: Labels; error: string | null; busy: boolean; onDraftChange: (draft: SupplierDraft) => void; onSubmit: (event: FormEvent<HTMLFormElement>) => void; onCancel: () => void }) {
  const update = <K extends keyof SupplierDraft>(key: K, value: SupplierDraft[K]) => onDraftChange({ ...draft, [key]: value });
  return <form className="supplier-editor" onSubmit={onSubmit}><header className="supplier-detail-header supplier-editor-header"><h2>{mode === 'create' ? labels.createTitle : labels.editTitle}</h2></header><FormSection title={labels.basicInfo}><Field label={labels.nameZhTw}><input value={draft.nameZhTw} onChange={(event) => update('nameZhTw', event.target.value)} /></Field><Field label={labels.nameThTh}><input value={draft.nameThTh} onChange={(event) => update('nameThTh', event.target.value)} /></Field><fieldset className="supplier-state-fieldset supplier-form-wide"><legend className="field-label">{labels.activeState}</legend><label className={draft.active ? 'is-selected' : ''}><input type="radio" name="supplier-active-state" checked={draft.active} onChange={() => update('active', true)} />{labels.active}</label><label className={!draft.active ? 'is-selected' : ''}><input type="radio" name="supplier-active-state" checked={!draft.active} onChange={() => update('active', false)} />{labels.inactive}</label></fieldset></FormSection><FormSection title={labels.contactInfo}><Field label={labels.phone}><input value={draft.phone} inputMode="tel" onChange={(event) => update('phone', event.target.value)} /></Field><Field label={labels.address} wide><textarea rows={3} value={draft.address} onChange={(event) => update('address', event.target.value)} /></Field></FormSection><FormSection title={labels.paymentInfo}><Field label={labels.bankName}><input value={draft.bankName} onChange={(event) => update('bankName', event.target.value)} /></Field><Field label={labels.bankAccount}><input value={draft.bankAccount} onChange={(event) => update('bankAccount', event.target.value)} /></Field></FormSection>{error && <div className="problem-banner" role="alert">{error}</div>}<footer className="supplier-editor-footer"><button className="supplier-secondary-action" type="button" disabled={busy} onClick={onCancel}>{labels.cancel}</button><button className="supplier-primary-action" type="submit" disabled={busy}>{busy ? labels.saving : labels.save}</button></footer></form>;
}
function DetailSection({ title, children }: { title: string; children: ReactNode }) { return <section className="supplier-detail-section"><h3>{title}</h3><div className="supplier-detail-fields">{children}</div></section>; }
function FormSection({ title, children }: { title: string; children: ReactNode }) { return <section className="supplier-detail-section"><h3>{title}</h3><div className="supplier-form-grid">{children}</div></section>; }
function DetailField({ label, value, fallback, wide = false }: { label: string; value: string | null; fallback: string; wide?: boolean }) { return <div className={`supplier-detail-field${wide ? ' is-wide' : ''}`}><dt>{label}</dt><dd>{value || fallback}</dd></div>; }
function Field({ label, children, wide = false }: { label: string; children: ReactNode; wide?: boolean }) { return <label className={wide ? 'supplier-form-wide' : undefined}><span className="field-label">{label}</span>{children}</label>; }
function StatusPill({ item, labels }: { item: SupplierMasterItem; labels: Labels }) { const state = item.deletedAt ? 'deleted' : item.active ? 'active' : 'inactive'; return <span className={`supplier-status-pill is-${state}`}>{labels[state]}</span>; }
function SearchIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="6.5" /><path d="m16 16 4 4" /></svg>; }
