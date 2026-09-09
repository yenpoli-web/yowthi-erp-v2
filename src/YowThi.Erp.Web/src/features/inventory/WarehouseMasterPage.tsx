import { useDeferredValue, useMemo, useRef, useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiProblemError } from '../../app/api/apiTransport';
import { useOperationalLocale } from '../../app/i18n/locale';
import { ModuleSubnav, type ModuleSubnavItem } from '../../app/modules/ModuleSubnav';
import { createWarehouse, listWarehouses, updateWarehouse, type InfrastructureMasterStatus, type WarehouseDraft, type WarehouseMasterItem } from './inventoryManagement';
import '../party/supplierMasterPrototype.css';

type EditorMode = 'detail' | 'create' | 'edit';
type Draft = { code: string; nameZhTw: string; nameThTh: string; active: boolean };
type SubmissionIdentity = { fingerprint: string; idempotencyKey: string };

const navItems: readonly ModuleSubnavItem[] = [
  { to: '/inventory', end: true, label: { 'zh-TW': '庫存現況', 'th-TH': 'ยอดคงเหลือ' } },
  { to: '/inventory/operations', label: { 'zh-TW': '調撥與調整', 'th-TH': 'โอนและปรับปรุง' } },
  { to: '/inventory/warehouses', label: { 'zh-TW': '倉庫', 'th-TH': 'คลัง' } },
  { to: '/inventory/storage-locations', label: { 'zh-TW': '儲位', 'th-TH': 'ตำแหน่งจัดเก็บ' } },
];

const copy = {
  'zh-TW': {
    nav: '庫存管理', eyebrow: '庫存基礎資料', title: '倉庫', lifecycle: '生命週期', newItem: '新增倉庫', search: '搜尋編號或名稱',
    all: '全部', active: '使用中', inactive: '停用', deleted: '已刪除', records: '筆資料', basicInfo: '基本資料', code: '編號', nameZhTw: '中文名稱', nameThTh: '泰文名稱', activeState: '啟用狀態',
    edit: '編輯', activate: '啟用', deactivate: '停用', save: '儲存', saving: '儲存中…', cancel: '取消', createTitle: '新增倉庫', editTitle: '編輯倉庫',
    noResult: '沒有符合條件的倉庫。', required: '中文名稱與泰文名稱至少填寫一項。', queryFailed: '無法載入倉庫', unexpected: '操作失敗，請重新整理後再試。', stale: '資料已被其他操作更新，請重新整理後再試。', notFound: '倉庫不存在。', deletedConflict: '這筆倉庫已刪除，請從生命週期頁恢復後再修改。', notProvided: '—',
  },
  'th-TH': {
    nav: 'สินค้าคงคลัง', eyebrow: 'ข้อมูลพื้นฐานคลัง', title: 'คลัง', lifecycle: 'วงจรข้อมูล', newItem: 'เพิ่มคลัง', search: 'ค้นหารหัสหรือชื่อ',
    all: 'ทั้งหมด', active: 'ใช้งาน', inactive: 'ไม่ใช้งาน', deleted: 'ลบแล้ว', records: 'รายการ', basicInfo: 'ข้อมูลพื้นฐาน', code: 'รหัส', nameZhTw: 'ชื่อภาษาจีน', nameThTh: 'ชื่อภาษาไทย', activeState: 'สถานะการใช้งาน',
    edit: 'แก้ไข', activate: 'เปิดใช้งาน', deactivate: 'ปิดใช้งาน', save: 'บันทึก', saving: 'กำลังบันทึก…', cancel: 'ยกเลิก', createTitle: 'เพิ่มคลัง', editTitle: 'แก้ไขคลัง',
    noResult: 'ไม่พบคลังที่ตรงกับเงื่อนไข', required: 'ต้องระบุชื่อภาษาจีนหรือภาษาไทยอย่างน้อยหนึ่งรายการ', queryFailed: 'ไม่สามารถโหลดคลังได้', unexpected: 'ดำเนินการไม่สำเร็จ กรุณารีเฟรชแล้วลองใหม่', stale: 'ข้อมูลถูกแก้ไขแล้ว กรุณารีเฟรชแล้วลองใหม่', notFound: 'ไม่พบคลัง', deletedConflict: 'คลังนี้ถูกลบแล้ว กรุณากู้คืนจากหน้าวงจรข้อมูลก่อนแก้ไข', notProvided: '—',
  },
} as const;

const emptyDraft = (): Draft => ({ code: '', nameZhTw: '', nameThTh: '', active: true });
const toDraft = (item: WarehouseMasterItem): Draft => ({ code: item.code ?? '', nameZhTw: item.nameZhTw ?? '', nameThTh: item.nameThTh ?? '', active: item.active });
const toRequest = (draft: Draft): WarehouseDraft => ({ code: draft.code.trim() || null, nameZhTw: draft.nameZhTw.trim() || null, nameThTh: draft.nameThTh.trim() || null, active: draft.active });

export function WarehouseMasterPage() {
  const { locale } = useOperationalLocale(); const labels = copy[locale]; const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState(''); const [mode, setMode] = useState<EditorMode>('detail'); const [draft, setDraft] = useState<Draft>(emptyDraft);
  const [filter, setFilter] = useState<InfrastructureMasterStatus>('all'); const [search, setSearch] = useState(''); const deferredSearch = useDeferredValue(search); const [formError, setFormError] = useState<string | null>(null); const identity = useRef<SubmissionIdentity | null>(null);
  const itemsQuery = useQuery({ queryKey: ['warehouse-master', locale, deferredSearch, filter], queryFn: ({ signal }) => listWarehouses({ locale, search: deferredSearch, status: filter, limit: 200, signal }), staleTime: 2_000 });
  const items = itemsQuery.data?.items ?? []; const selected = items.find((item) => item.id === selectedId) ?? (mode === 'detail' ? items[0] ?? null : null);
  const createMutation = useMutation({ mutationFn: (input: { request: WarehouseDraft; idempotencyKey: string }) => createWarehouse(input.request, { locale, idempotencyKey: input.idempotencyKey }), onSuccess: async (result) => { setFilter('all'); setSelectedId(result.warehouseId); setMode('detail'); identity.current = null; await invalidate(); } });
  const updateMutation = useMutation({ mutationFn: (input: { id: string; rowVersion: number; request: WarehouseDraft; idempotencyKey: string }) => updateWarehouse(input.id, { ...input.request, expectedRowVersion: input.rowVersion }, { locale, idempotencyKey: input.idempotencyKey }), onSuccess: async (result) => { setSelectedId(result.warehouseId); setMode('detail'); identity.current = null; await invalidate(); } });
  async function invalidate() { await Promise.all([queryClient.invalidateQueries({ queryKey: ['warehouse-master'] }), queryClient.invalidateQueries({ queryKey: ['storage-location-master-warehouses'] }), queryClient.invalidateQueries({ queryKey: ['product-storage-options'] })]); }
  const activeError = createMutation.error ?? updateMutation.error ?? itemsQuery.error;
  const problemMessage = useMemo(() => { if (!activeError) return null; if (!(activeError instanceof ApiProblemError)) return itemsQuery.error ? labels.queryFailed : labels.unexpected; if (activeError.code.includes('stale-row-version')) return labels.stale; if (activeError.code.includes('not-found')) return labels.notFound; if (activeError.code.includes('.deleted')) return labels.deletedConflict; return `${activeError.code}${activeError.problem.traceId ? ` · ${activeError.problem.traceId}` : ''}`; }, [activeError, itemsQuery.error, labels]);
  const displayName = (item: WarehouseMasterItem) => (locale === 'zh-TW' ? item.nameZhTw : item.nameThTh)?.trim() || (locale === 'zh-TW' ? item.nameThTh : item.nameZhTw)?.trim() || labels.notProvided;
  const busy = createMutation.isPending || updateMutation.isPending;
  function select(item: WarehouseMasterItem) { setSelectedId(item.id); setDraft(toDraft(item)); setMode('detail'); setFormError(null); identity.current = null; }
  function beginCreate() { setSelectedId(''); setDraft(emptyDraft()); setMode('create'); setFormError(null); identity.current = null; }
  function beginEdit() { if (selected && !selected.deletedAt) { setDraft(toDraft(selected)); setMode('edit'); setFormError(null); identity.current = null; } }
  function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); const request = toRequest(draft); if (!request.nameZhTw && !request.nameThTh) { setFormError(labels.required); return; } setFormError(null); const fingerprint = JSON.stringify({ mode, selectedId, rowVersion: selected?.rowVersion ?? null, request }); if (identity.current?.fingerprint !== fingerprint) identity.current = { fingerprint, idempotencyKey: crypto.randomUUID() }; if (mode === 'create') createMutation.mutate({ request, idempotencyKey: identity.current.idempotencyKey }); else if (mode === 'edit' && selected) updateMutation.mutate({ id: selected.id, rowVersion: selected.rowVersion, request, idempotencyKey: identity.current.idempotencyKey }); }
  function toggleActive() { if (!selected || selected.deletedAt) return; updateMutation.mutate({ id: selected.id, rowVersion: selected.rowVersion, request: { ...toRequest(toDraft(selected)), active: !selected.active }, idempotencyKey: crypto.randomUUID() }); }

  return <section className="supplier-master-prototype" aria-labelledby="warehouse-master-title"><ModuleSubnav locale={locale} items={navItems} ariaLabel={labels.nav} />
    <header className="supplier-master-header"><div><p className="eyebrow">{labels.eyebrow}</p><h1 id="warehouse-master-title">{labels.title}</h1></div><div className="supplier-master-header-actions"><Link className="supplier-secondary-action" to="/infrastructure">{labels.lifecycle}</Link><button className="supplier-primary-action" type="button" onClick={beginCreate} disabled={busy}>＋{labels.newItem}</button></div></header>
    <div className="supplier-master-toolbar"><label className="supplier-search"><input type="search" value={search} aria-label={labels.search} onChange={(e) => setSearch(e.target.value)} /></label><div className="supplier-filter-tabs">{(['all','active','inactive','deleted'] as const).map((candidate) => <button key={candidate} type="button" className={filter === candidate ? 'is-active' : ''} onClick={() => { setFilter(candidate); setSelectedId(''); setMode('detail'); }}>{labels[candidate]}</button>)}</div></div>
    <div className="supplier-master-grid"><aside className="supplier-list-panel"><div className="supplier-list-meta"><strong>{labels.title}</strong><span>{items.length} {labels.records}</span></div><div className="supplier-list-body">{items.map((item) => <button type="button" key={item.id} className={`supplier-list-item${selected?.id === item.id ? ' is-selected' : ''}`} onClick={() => select(item)}><span className="supplier-list-name"><strong>{displayName(item)}</strong><small>{item.code || labels.notProvided}</small></span><StatusPill item={item} labels={labels} /></button>)}{!itemsQuery.isPending && items.length === 0 && <p className="supplier-list-empty">{labels.noResult}</p>}</div></aside>
      <article className="supplier-detail-panel">{mode !== 'detail' ? <form className="supplier-editor" onSubmit={submit}><header className="supplier-detail-header supplier-editor-header"><h2>{mode === 'create' ? labels.createTitle : labels.editTitle}</h2></header><section className="supplier-detail-section"><h3>{labels.basicInfo}</h3><div className="supplier-form-grid"><Field label={labels.code} value={draft.code} onChange={(value) => setDraft({ ...draft, code: value })} /><Field label={labels.nameZhTw} value={draft.nameZhTw} onChange={(value) => setDraft({ ...draft, nameZhTw: value })} /><Field label={labels.nameThTh} value={draft.nameThTh} onChange={(value) => setDraft({ ...draft, nameThTh: value })} /><fieldset className="supplier-state-fieldset supplier-form-wide"><legend className="field-label">{labels.activeState}</legend><label className={draft.active ? 'is-selected' : ''}><input type="radio" checked={draft.active} onChange={() => setDraft({ ...draft, active: true })} />{labels.active}</label><label className={!draft.active ? 'is-selected' : ''}><input type="radio" checked={!draft.active} onChange={() => setDraft({ ...draft, active: false })} />{labels.inactive}</label></fieldset></div></section>{(formError ?? problemMessage) && <div className="problem-banner" role="alert">{formError ?? problemMessage}</div>}<footer className="supplier-editor-footer"><button type="button" className="supplier-secondary-action" onClick={() => setMode('detail')}>{labels.cancel}</button><button className="supplier-primary-action" type="submit" disabled={busy}>{busy ? labels.saving : labels.save}</button></footer></form> : selected ? <><header className="supplier-detail-header"><div><div className="supplier-detail-title-row"><h2>{displayName(selected)}</h2><StatusPill item={selected} labels={labels} /></div></div>{!selected.deletedAt && <div className="supplier-detail-actions"><button className="supplier-secondary-action" type="button" onClick={beginEdit}>{labels.edit}</button><button className="supplier-secondary-action" type="button" onClick={toggleActive}>{selected.active ? labels.deactivate : labels.activate}</button></div>}</header><section className="supplier-detail-section"><h3>{labels.basicInfo}</h3><dl className="supplier-detail-fields"><Detail label={labels.code} value={selected.code || labels.notProvided} /><Detail label={labels.nameZhTw} value={selected.nameZhTw || labels.notProvided} /><Detail label={labels.nameThTh} value={selected.nameThTh || labels.notProvided} /></dl></section>{problemMessage && <div className="problem-banner" role="alert">{problemMessage}</div>}</> : <p className="supplier-list-empty">{itemsQuery.isPending ? '…' : labels.noResult}</p>}</article></div>
  </section>;
}

function Field({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) { return <label><span className="field-label">{label}</span><input value={value} onChange={(event) => onChange(event.target.value)} /></label>; }
function Detail({ label, value }: { label: string; value: string }) { return <div className="supplier-detail-field"><dt>{label}</dt><dd>{value}</dd></div>; }
function StatusPill({ item, labels }: { item: WarehouseMasterItem; labels: typeof copy['zh-TW'] | typeof copy['th-TH'] }) { const state = item.deletedAt ? 'deleted' : item.active ? 'active' : 'inactive'; return <span className={`supplier-status-pill is-${state}`}>{labels[state]}</span>; }
