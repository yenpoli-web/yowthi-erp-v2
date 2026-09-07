import { useMemo, useState, type FormEvent, type KeyboardEvent, type ReactNode } from 'react';

import { useOperationalLocale } from '../../app/i18n/locale';
import {
  createPrototypeSupplier,
  getPrototypeSuppliers,
  restorePrototypeSupplier,
  softDeletePrototypeSupplier,
  togglePrototypeSupplierActive,
  updatePrototypeSupplier,
  usePrototypeSuppliers,
  type PrototypeSupplier,
  type PrototypeSupplierDraft,
} from './supplierMasterPrototypeStore';
import './supplierMasterPrototype.css';

type SupplierFilter = 'all' | 'active' | 'inactive' | 'deleted';
type EditorMode = 'detail' | 'create' | 'edit';

const copy = {
  'zh-TW': {
    eyebrow: '夥伴管理', title: '供應商', prototype: '操作原型', newSupplier: '新增供應商', search: '搜尋名稱、電話或銀行',
    all: '全部', active: '使用中', inactive: '停用', deleted: '已刪除', supplier: '供應商', phone: '電話', status: '狀態', records: '筆資料',
    edit: '編輯', deactivate: '停用', activate: '啟用', softDelete: '刪除', restore: '恢復', basicInfo: '基本資料',
    nameZhTw: '中文名稱', nameThTh: '泰文名稱', contactInfo: '聯絡資料', address: '地址', paymentInfo: '銀行資料', bankName: '銀行名稱', bankAccount: '銀行帳號',
    activeState: '啟用狀態', nameRequired: '中文名稱與泰文名稱至少填寫一項。', save: '儲存', cancel: '取消', createTitle: '新增供應商', editTitle: '編輯供應商',
    deleteTitle: '刪除供應商', deleteMessage: '這會將資料設為軟刪除；之後仍可從「已刪除」清單恢復，也會出現在資料保護區的硬刪除清單。', confirmDelete: '確認刪除',
    noResult: '沒有符合條件的供應商。', notProvided: '—',
  },
  'th-TH': {
    eyebrow: 'จัดการคู่ค้า', title: 'ผู้จำหน่าย', prototype: 'ต้นแบบการใช้งาน', newSupplier: 'เพิ่มผู้จำหน่าย', search: 'ค้นหาชื่อ โทรศัพท์ หรือธนาคาร',
    all: 'ทั้งหมด', active: 'ใช้งาน', inactive: 'ไม่ใช้งาน', deleted: 'ลบแล้ว', supplier: 'ผู้จำหน่าย', phone: 'โทรศัพท์', status: 'สถานะ', records: 'รายการ',
    edit: 'แก้ไข', deactivate: 'ปิดใช้งาน', activate: 'เปิดใช้งาน', softDelete: 'ลบ', restore: 'กู้คืน', basicInfo: 'ข้อมูลพื้นฐาน',
    nameZhTw: 'ชื่อภาษาจีน', nameThTh: 'ชื่อภาษาไทย', contactInfo: 'ข้อมูลติดต่อ', address: 'ที่อยู่', paymentInfo: 'ข้อมูลธนาคาร', bankName: 'ชื่อธนาคาร', bankAccount: 'เลขบัญชีธนาคาร',
    activeState: 'สถานะการใช้งาน', nameRequired: 'ต้องระบุชื่อภาษาจีนหรือชื่อภาษาไทยอย่างน้อยหนึ่งรายการ', save: 'บันทึก', cancel: 'ยกเลิก', createTitle: 'เพิ่มผู้จำหน่าย', editTitle: 'แก้ไขผู้จำหน่าย',
    deleteTitle: 'ลบผู้จำหน่าย', deleteMessage: 'รายการจะถูกลบแบบเก็บประวัติ สามารถกู้คืนได้ และจะแสดงในพื้นที่คุ้มครองสำหรับการลบถาวร', confirmDelete: 'ยืนยันการลบ',
    noResult: 'ไม่พบผู้จำหน่ายที่ตรงกับเงื่อนไข', notProvided: '—',
  },
} as const;

type PrototypeLabels = typeof copy['zh-TW'] | typeof copy['th-TH'];

type SupplierDraft = PrototypeSupplierDraft;

function toDraft(item: PrototypeSupplier): SupplierDraft {
  return {
    nameZhTw: item.nameZhTw,
    nameThTh: item.nameThTh,
    phone: item.phone,
    address: item.address,
    bankName: item.bankName,
    bankAccount: item.bankAccount,
    active: item.active,
  };
}

function emptyDraft(): SupplierDraft {
  return {
    nameZhTw: '',
    nameThTh: '',
    phone: '',
    address: '',
    bankName: '',
    bankAccount: '',
    active: true,
  };
}

export function SupplierMasterPrototypePage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const items = usePrototypeSuppliers();
  const initial = getPrototypeSuppliers()[0]!;
  const [selectedId, setSelectedId] = useState(initial.id);
  const [mode, setMode] = useState<EditorMode>('detail');
  const [draft, setDraft] = useState<SupplierDraft>(() => toDraft(initial));
  const [filter, setFilter] = useState<SupplierFilter>('all');
  const [query, setQuery] = useState('');
  const [formError, setFormError] = useState<string | null>(null);
  const [deleteOpen, setDeleteOpen] = useState(false);

  const selected = items.find((item) => item.id === selectedId) ?? null;
  const filteredItems = useMemo(() => {
    const normalized = query.trim().toLocaleLowerCase();
    return items.filter((item) => {
      const matchesFilter = filter === 'all'
        || (filter === 'active' && item.active && !item.deleted)
        || (filter === 'inactive' && !item.active && !item.deleted)
        || (filter === 'deleted' && item.deleted);
      return matchesFilter && (normalized.length === 0
        || [item.nameZhTw, item.nameThTh, item.phone, item.bankName]
          .some((value) => value.toLocaleLowerCase().includes(normalized)));
    });
  }, [filter, items, query]);

  const displayName = (item: PrototypeSupplier) => {
    const primary = locale === 'zh-TW' ? item.nameZhTw : item.nameThTh;
    const fallback = locale === 'zh-TW' ? item.nameThTh : item.nameZhTw;
    return primary.trim() || fallback.trim() || labels.notProvided;
  };
  const secondaryName = (item: PrototypeSupplier) => (locale === 'zh-TW' ? item.nameThTh : item.nameZhTw).trim();

  function selectSupplier(item: PrototypeSupplier) {
    setSelectedId(item.id);
    setDraft(toDraft(item));
    setFormError(null);
    setMode('detail');
  }

  function handleListKey(event: KeyboardEvent<HTMLDivElement>, item: PrototypeSupplier) {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      selectSupplier(item);
    }
  }

  function beginCreate() {
    setSelectedId('');
    setDraft(emptyDraft());
    setFormError(null);
    setMode('create');
  }

  function beginEdit() {
    if (selected === null) return;
    setDraft(toDraft(selected));
    setFormError(null);
    setMode('edit');
  }

  function cancelEditor() {
    if (selected !== null) {
      setDraft(toDraft(selected));
      setMode('detail');
    } else {
      const fallback = getPrototypeSuppliers()[0] ?? null;
      if (fallback !== null) selectSupplier(fallback);
      else setMode('create');
    }
    setFormError(null);
  }

  function saveDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalized: PrototypeSupplierDraft = {
      nameZhTw: draft.nameZhTw.trim(),
      nameThTh: draft.nameThTh.trim(),
      phone: draft.phone.trim(),
      address: draft.address.trim(),
      bankName: draft.bankName.trim(),
      bankAccount: draft.bankAccount.trim(),
      active: draft.active,
    };
    if (!normalized.nameZhTw && !normalized.nameThTh) {
      setFormError(labels.nameRequired);
      return;
    }

    if (mode === 'create') {
      const created = createPrototypeSupplier(normalized);
      setSelectedId(created.id);
      setDraft(toDraft(created));
      setFilter('all');
    } else if (mode === 'edit' && selected !== null) {
      updatePrototypeSupplier(selected.id, normalized);
      setDraft(normalized);
    }
    setFormError(null);
    setMode('detail');
  }

  function toggleActive() {
    if (selected === null || selected.deleted) return;
    togglePrototypeSupplierActive(selected.id);
  }

  function softDelete() {
    if (selected === null) return;
    softDeletePrototypeSupplier(selected.id);
    setFilter('deleted');
    setDeleteOpen(false);
  }

  function restore() {
    if (selected === null) return;
    restorePrototypeSupplier(selected.id);
    setFilter('all');
  }

  return (
    <section className="supplier-master-prototype" aria-labelledby="supplier-master-title">
      <header className="supplier-master-header">
        <div><p className="eyebrow">{labels.eyebrow}</p><h1 id="supplier-master-title">{labels.title}</h1></div>
        <div className="supplier-master-header-actions">
          <span className="supplier-prototype-badge">{labels.prototype}</span>
          <button className="supplier-primary-action" type="button" onClick={beginCreate}><span aria-hidden="true">＋</span>{labels.newSupplier}</button>
        </div>
      </header>

      <div className="supplier-master-toolbar">
        <label className="supplier-search"><SearchIcon /><input type="search" value={query} aria-label={labels.search} onChange={(event) => setQuery(event.target.value)} /></label>
        <div className="supplier-filter-tabs" role="group" aria-label={labels.status}>
          {(['all', 'active', 'inactive', 'deleted'] as const).map((candidate) => (
            <button key={candidate} type="button" className={filter === candidate ? 'is-active' : ''} onClick={() => setFilter(candidate)}>{labels[candidate]}</button>
          ))}
        </div>
      </div>

      <div className="supplier-master-grid">
        <aside className="supplier-list-panel" aria-label={labels.supplier}>
          <div className="supplier-list-meta"><strong>{labels.supplier}</strong><span>{filteredItems.length} {labels.records}</span></div>
          <div className="supplier-list-heading" aria-hidden="true"><span>{labels.supplier}</span><span>{labels.phone}</span><span>{labels.status}</span></div>
          <div className="supplier-list-body">
            {filteredItems.map((item) => (
              <div key={item.id} role="button" tabIndex={0} className={`supplier-list-item${selectedId === item.id ? ' is-selected' : ''}`} onClick={() => selectSupplier(item)} onKeyDown={(event) => handleListKey(event, item)}>
                <span className="supplier-list-name"><strong>{displayName(item)}</strong>{secondaryName(item) && <small>{secondaryName(item)}</small>}</span>
                <span className="supplier-list-phone">{item.phone || labels.notProvided}</span>
                <StatusPill item={item} labels={labels} />
              </div>
            ))}
            {filteredItems.length === 0 && <p className="supplier-list-empty">{labels.noResult}</p>}
          </div>
        </aside>

        <article className="supplier-detail-panel">
          {mode === 'create' || mode === 'edit' ? (
            <SupplierEditor mode={mode} draft={draft} labels={labels} error={formError} onDraftChange={setDraft} onSubmit={saveDraft} onCancel={cancelEditor} />
          ) : selected !== null ? (
            <SupplierDetail item={selected} displayName={displayName(selected)} secondaryName={secondaryName(selected)} labels={labels} onEdit={beginEdit} onToggleActive={toggleActive} onDelete={() => setDeleteOpen(true)} onRestore={restore} />
          ) : (
            <SupplierEditor mode="create" draft={draft} labels={labels} error={formError} onDraftChange={setDraft} onSubmit={saveDraft} onCancel={cancelEditor} />
          )}
        </article>
      </div>

      {deleteOpen && selected !== null && (
        <div className="supplier-dialog-backdrop" role="presentation" onMouseDown={() => setDeleteOpen(false)}>
          <div className="supplier-dialog" role="dialog" aria-modal="true" aria-labelledby="supplier-delete-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="supplier-dialog-icon" aria-hidden="true">!</div>
            <div><h2 id="supplier-delete-dialog-title">{labels.deleteTitle}</h2><p>{displayName(selected)}</p><p className="supplier-dialog-message">{labels.deleteMessage}</p></div>
            <div className="supplier-dialog-actions"><button type="button" className="supplier-secondary-action" onClick={() => setDeleteOpen(false)}>{labels.cancel}</button><button type="button" className="supplier-danger-action" onClick={softDelete}>{labels.confirmDelete}</button></div>
          </div>
        </div>
      )}
    </section>
  );
}

function SupplierDetail({ item, displayName, secondaryName, labels, onEdit, onToggleActive, onDelete, onRestore }: {
  item: PrototypeSupplier; displayName: string; secondaryName: string; labels: PrototypeLabels; onEdit: () => void; onToggleActive: () => void; onDelete: () => void; onRestore: () => void;
}) {
  return <>
    <header className="supplier-detail-header">
      <div><div className="supplier-detail-title-row"><h2>{displayName}</h2><StatusPill item={item} labels={labels} /></div>{secondaryName && <p>{secondaryName}</p>}</div>
      <div className="supplier-detail-actions">
        {item.deleted ? <button className="supplier-primary-action" type="button" onClick={onRestore}>{labels.restore}</button> : <>
          <button className="supplier-secondary-action" type="button" onClick={onEdit}>{labels.edit}</button>
          <button className="supplier-secondary-action" type="button" onClick={onToggleActive}>{item.active ? labels.deactivate : labels.activate}</button>
          <button className="supplier-danger-ghost-action" type="button" onClick={onDelete}>{labels.softDelete}</button>
        </>}
      </div>
    </header>
    <DetailSection title={labels.basicInfo}><DetailField label={labels.nameZhTw} value={item.nameZhTw} fallback={labels.notProvided} /><DetailField label={labels.nameThTh} value={item.nameThTh} fallback={labels.notProvided} /></DetailSection>
    <DetailSection title={labels.contactInfo}><DetailField label={labels.phone} value={item.phone} fallback={labels.notProvided} /><DetailField label={labels.address} value={item.address} fallback={labels.notProvided} wide /></DetailSection>
    <DetailSection title={labels.paymentInfo}><DetailField label={labels.bankName} value={item.bankName} fallback={labels.notProvided} /><DetailField label={labels.bankAccount} value={item.bankAccount} fallback={labels.notProvided} /></DetailSection>
  </>;
}

function SupplierEditor({ mode, draft, labels, error, onDraftChange, onSubmit, onCancel }: {
  mode: 'create' | 'edit'; draft: SupplierDraft; labels: PrototypeLabels; error: string | null; onDraftChange: (draft: SupplierDraft) => void; onSubmit: (event: FormEvent<HTMLFormElement>) => void; onCancel: () => void;
}) {
  const update = <K extends keyof SupplierDraft>(key: K, value: SupplierDraft[K]) => onDraftChange({ ...draft, [key]: value });
  return <form className="supplier-editor" onSubmit={onSubmit}>
    <header className="supplier-detail-header supplier-editor-header"><h2>{mode === 'create' ? labels.createTitle : labels.editTitle}</h2></header>
    <FormSection title={labels.basicInfo}>
      <Field label={labels.nameZhTw}><input value={draft.nameZhTw} onChange={(event) => update('nameZhTw', event.target.value)} /></Field>
      <Field label={labels.nameThTh}><input value={draft.nameThTh} onChange={(event) => update('nameThTh', event.target.value)} /></Field>
      <fieldset className="supplier-state-fieldset supplier-form-wide"><legend className="field-label">{labels.activeState}</legend>
        <label className={draft.active ? 'is-selected' : ''}><input type="radio" name="supplier-active-state" checked={draft.active} onChange={() => update('active', true)} />{labels.active}</label>
        <label className={!draft.active ? 'is-selected' : ''}><input type="radio" name="supplier-active-state" checked={!draft.active} onChange={() => update('active', false)} />{labels.inactive}</label>
      </fieldset>
    </FormSection>
    <FormSection title={labels.contactInfo}>
      <Field label={labels.phone}><input value={draft.phone} inputMode="tel" onChange={(event) => update('phone', event.target.value)} /></Field>
      <Field label={labels.address} wide><textarea rows={3} value={draft.address} onChange={(event) => update('address', event.target.value)} /></Field>
    </FormSection>
    <FormSection title={labels.paymentInfo}>
      <Field label={labels.bankName}><input value={draft.bankName} onChange={(event) => update('bankName', event.target.value)} /></Field>
      <Field label={labels.bankAccount}><input value={draft.bankAccount} onChange={(event) => update('bankAccount', event.target.value)} /></Field>
    </FormSection>
    {error && <div className="problem-banner" role="alert">{error}</div>}
    <footer className="supplier-editor-footer">
      <button className="supplier-secondary-action" type="button" onClick={onCancel}>{labels.cancel}</button>
      <button className="supplier-primary-action" type="submit">{labels.save}</button>
    </footer>
  </form>;
}

function DetailSection({ title, children }: { title: string; children: ReactNode }) {
  return <section className="supplier-detail-section"><h3>{title}</h3><div className="supplier-detail-fields">{children}</div></section>;
}
function FormSection({ title, children }: { title: string; children: ReactNode }) {
  return <section className="supplier-detail-section"><h3>{title}</h3><div className="supplier-form-grid">{children}</div></section>;
}
function DetailField({ label, value, fallback, wide = false }: { label: string; value: string; fallback: string; wide?: boolean }) {
  return <div className={`supplier-detail-field${wide ? ' is-wide' : ''}`}><dt>{label}</dt><dd>{value || fallback}</dd></div>;
}
function Field({ label, children, wide = false }: { label: string; children: ReactNode; wide?: boolean }) {
  return <label className={wide ? 'supplier-form-wide' : undefined}><span className="field-label">{label}</span>{children}</label>;
}
function StatusPill({ item, labels }: { item: PrototypeSupplier; labels: PrototypeLabels }) {
  const state = item.deleted ? 'deleted' : item.active ? 'active' : 'inactive';
  return <span className={`supplier-status-pill is-${state}`}>{labels[state]}</span>;
}
function SearchIcon() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="6.5" /><path d="m16 16 4 4" /></svg>;
}
