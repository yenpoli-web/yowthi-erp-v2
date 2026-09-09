import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { useDeferredValue, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useOperationalLocale } from '../../app/i18n/locale';
import { ModuleSubnav, type ModuleSubnavItem } from '../../app/modules/ModuleSubnav';
import { ApiProblemError } from '../../app/api/apiTransport';
import {
  changeSalesProductGroupLifecycle,
  createSalesProductGroup,
  listSalesProductGroups,
  updateSalesProductGroup,
  type SalesProductGroupDraftRequest,
  type SalesProductGroupMasterItem,
  type SalesProductGroupMasterStatus,
  type SalesProductGroupLifecycleAction,
} from './salesProductGroupMaster';
import '../party/supplierMasterPrototype.css';

type EditorMode = 'detail' | 'create' | 'edit';
type Draft = { nameZhTw: string; nameThTh: string; active: boolean };
type SubmissionIdentity = { fingerprint: string; idempotencyKey: string };

const navItems: readonly ModuleSubnavItem[] = [
  { to: '/product', end: true, label: { 'zh-TW': '採購產品', 'th-TH': 'สินค้าจัดซื้อ' } },
  { to: '/product/sales-products', label: { 'zh-TW': '銷售產品', 'th-TH': 'สินค้าขาย' } },
  { to: '/product/sales-product-groups', label: { 'zh-TW': '銷售產品群組', 'th-TH': 'กลุ่มสินค้าขาย' } },
];

const copy = {
  'zh-TW': {
    eyebrow: '產品主檔', title: '銷售產品群組', workEntry: '生命週期', newItem: '新增群組', search: '搜尋群組名稱',
    all: '全部', active: '使用中', inactive: '停用', deleted: '已刪除', item: '銷售產品群組', status: '狀態', records: '筆資料',
    edit: '編輯', deactivate: '停用', activate: '啟用', softDelete: '刪除', restore: '恢復', basicInfo: '基本資料',
    nameZhTw: '中文名稱', nameThTh: '泰文名稱', activeState: '啟用狀態', nameRequired: '中文名稱與泰文名稱至少填寫一項。',
    save: '儲存', saving: '儲存中…', cancel: '取消', createTitle: '新增銷售產品群組', editTitle: '編輯銷售產品群組',
    deleteTitle: '刪除銷售產品群組', deleteMessage: '這會將資料設為軟刪除；之後可從「已刪除」恢復。', confirmDelete: '確認刪除',
    noResult: '沒有符合條件的銷售產品群組。', notProvided: '—', queryFailed: '無法載入銷售產品群組', unexpected: '操作失敗，請重新整理後再試。',
    stale: '資料已被其他操作更新，請重新整理後再試。', notFound: '銷售產品群組不存在。', deletedConflict: '這筆群組已經刪除，請先恢復後再編輯。', idempotency: '相同操作識別已被其他內容使用。',
  },
  'th-TH': {
    eyebrow: 'ข้อมูลหลักสินค้า', title: 'กลุ่มสินค้าขาย', workEntry: 'วงจรข้อมูล', newItem: 'เพิ่มกลุ่ม', search: 'ค้นหาชื่อกลุ่ม',
    all: 'ทั้งหมด', active: 'ใช้งาน', inactive: 'ไม่ใช้งาน', deleted: 'ลบแล้ว', item: 'กลุ่มสินค้าขาย', status: 'สถานะ', records: 'รายการ',
    edit: 'แก้ไข', deactivate: 'ปิดใช้งาน', activate: 'เปิดใช้งาน', softDelete: 'ลบ', restore: 'กู้คืน', basicInfo: 'ข้อมูลพื้นฐาน',
    nameZhTw: 'ชื่อภาษาจีน', nameThTh: 'ชื่อภาษาไทย', activeState: 'สถานะการใช้งาน', nameRequired: 'ต้องระบุชื่อภาษาจีนหรือชื่อภาษาไทยอย่างน้อยหนึ่งรายการ',
    save: 'บันทึก', saving: 'กำลังบันทึก…', cancel: 'ยกเลิก', createTitle: 'เพิ่มกลุ่มสินค้าขาย', editTitle: 'แก้ไขกลุ่มสินค้าขาย',
    deleteTitle: 'ลบกลุ่มสินค้าขาย', deleteMessage: 'รายการจะถูกลบแบบเก็บประวัติ และสามารถกู้คืนได้ภายหลัง', confirmDelete: 'ยืนยันการลบ',
    noResult: 'ไม่พบกลุ่มสินค้าขายที่ตรงกับเงื่อนไข', notProvided: '—', queryFailed: 'ไม่สามารถโหลดกลุ่มสินค้าขายได้', unexpected: 'ดำเนินการไม่สำเร็จ กรุณารีเฟรชแล้วลองใหม่',
    stale: 'ข้อมูลถูกแก้ไขโดยการทำงานอื่น กรุณารีเฟรชแล้วลองใหม่', notFound: 'ไม่พบกลุ่มสินค้าขาย', deletedConflict: 'กลุ่มนี้ถูกลบแล้ว กรุณากู้คืนก่อนแก้ไข', idempotency: 'รหัสการทำงานเดียวกันถูกใช้กับข้อมูลอื่นแล้ว',
  },
} as const;

type Labels = typeof copy['zh-TW'] | typeof copy['th-TH'];
const emptyDraft = (): Draft => ({ nameZhTw: '', nameThTh: '', active: true });
const toDraft = (item: SalesProductGroupMasterItem): Draft => ({ nameZhTw: item.nameZhTw ?? '', nameThTh: item.nameThTh ?? '', active: item.active });
const toRequest = (draft: Draft): SalesProductGroupDraftRequest => ({ nameZhTw: draft.nameZhTw.trim() || null, nameThTh: draft.nameThTh.trim() || null, active: draft.active });

export function SalesProductGroupMasterPage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState('');
  const [mode, setMode] = useState<EditorMode>('detail');
  const [draft, setDraft] = useState<Draft>(emptyDraft);
  const [filter, setFilter] = useState<SalesProductGroupMasterStatus>('all');
  const [search, setSearch] = useState('');
  const deferredSearch = useDeferredValue(search);
  const [formError, setFormError] = useState<string | null>(null);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const writeIdentity = useRef<SubmissionIdentity | null>(null);
  const lifecycleIdentity = useRef<SubmissionIdentity | null>(null);

  const itemsQuery = useQuery({ queryKey: ['sales-product-group-master', locale, deferredSearch, filter], queryFn: ({ signal }) => listSalesProductGroups({ locale, search: deferredSearch, status: filter, limit: 200, signal }), staleTime: 2_000 });
  const items = itemsQuery.data?.items ?? [];
  const selected = items.find((item) => item.id === selectedId) ?? (mode === 'detail' ? items[0] ?? null : null);
  async function invalidateData() { await Promise.all([queryClient.invalidateQueries({ queryKey: ['sales-product-group-master'] }), queryClient.invalidateQueries({ queryKey: ['sales-product-group-lifecycle-options'] })]); }

  const createMutation = useMutation({ mutationFn: (input: { request: SalesProductGroupDraftRequest; idempotencyKey: string }) => createSalesProductGroup(input.request, { locale, idempotencyKey: input.idempotencyKey }), onSuccess: async (result) => { setFilter('all'); setSelectedId(result.salesProductGroupId); setMode('detail'); writeIdentity.current = null; await invalidateData(); } });
  const updateMutation = useMutation({ mutationFn: (input: { id: string; expectedRowVersion: number; request: SalesProductGroupDraftRequest; idempotencyKey: string }) => updateSalesProductGroup(input.id, { ...input.request, expectedRowVersion: input.expectedRowVersion }, { locale, idempotencyKey: input.idempotencyKey }), onSuccess: async (result) => { setSelectedId(result.salesProductGroupId); setMode('detail'); writeIdentity.current = null; await invalidateData(); } });
  const lifecycleMutation = useMutation({ mutationFn: (input: { id: string; action: SalesProductGroupLifecycleAction; expectedRowVersion: number; idempotencyKey: string }) => changeSalesProductGroupLifecycle(input.id, input.action, input.expectedRowVersion, { locale, idempotencyKey: input.idempotencyKey }), onSuccess: async (_, input) => { setFilter(input.action === 'soft-delete' ? 'deleted' : 'all'); setDeleteOpen(false); lifecycleIdentity.current = null; await invalidateData(); } });

  const activeError = createMutation.error ?? updateMutation.error ?? lifecycleMutation.error ?? itemsQuery.error;
  const problemMessage = useMemo(() => {
    if (!activeError) return null;
    if (!(activeError instanceof ApiProblemError)) return itemsQuery.error ? labels.queryFailed : labels.unexpected;
    if (activeError.code === 'product.sales-product-group.stale-row-version' || activeError.code === 'concurrency.stale-row-version') return labels.stale;
    if (activeError.code === 'product.sales-product-group.not-found' || activeError.code === 'product.sales-product-group-not-found') return labels.notFound;
    if (activeError.code === 'product.sales-product-group.deleted') return labels.deletedConflict;
    if (activeError.code === 'product.sales-product-group.idempotency-key-reused' || activeError.code === 'idempotency.key-reused') return labels.idempotency;
    return `${activeError.code}${activeError.problem.traceId ? ` · ${activeError.problem.traceId}` : ``}`;
  }, [activeError, labels, itemsQuery.error]);

  const displayName = (item: SalesProductGroupMasterItem) => (locale === 'zh-TW' ? item.nameZhTw : item.nameThTh)?.trim() || (locale === 'zh-TW' ? item.nameThTh : item.nameZhTw)?.trim() || labels.notProvided;
  const secondaryName = (item: SalesProductGroupMasterItem) => (locale === 'zh-TW' ? item.nameThTh : item.nameZhTw)?.trim() ?? '';
  const busy = createMutation.isPending || updateMutation.isPending || lifecycleMutation.isPending;
  function selectItem(item: SalesProductGroupMasterItem) { setSelectedId(item.id); setDraft(toDraft(item)); setFormError(null); setMode('detail'); writeIdentity.current = null; lifecycleIdentity.current = null; }
  function handleListKey(event: KeyboardEvent<HTMLDivElement>, item: SalesProductGroupMasterItem) { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); selectItem(item); } }
  function beginCreate() { setSelectedId(''); setDraft(emptyDraft()); setFormError(null); setMode('create'); writeIdentity.current = null; }
  function beginEdit() { if (selected) { setDraft(toDraft(selected)); setFormError(null); setMode('edit'); writeIdentity.current = null; } }
  function cancelEditor() { setMode('detail'); setFormError(null); writeIdentity.current = null; if (selected) setDraft(toDraft(selected)); }
  function saveDraft(event: FormEvent<HTMLFormElement>) { event.preventDefault(); const request = toRequest(draft); if (!request.nameZhTw && !request.nameThTh) { setFormError(labels.nameRequired); return; } setFormError(null); const fingerprint = JSON.stringify({ mode, selectedId, rowVersion: selected?.rowVersion ?? null, request }); if (writeIdentity.current?.fingerprint !== fingerprint) writeIdentity.current = { fingerprint, idempotencyKey: crypto.randomUUID() }; if (mode === 'create') createMutation.mutate({ request, idempotencyKey: writeIdentity.current.idempotencyKey }); else if (mode === 'edit' && selected) updateMutation.mutate({ id: selected.id, expectedRowVersion: selected.rowVersion, request, idempotencyKey: writeIdentity.current.idempotencyKey }); }
  function toggleActive() { if (!selected || selected.deletedAt) return; const request = { ...toRequest(toDraft(selected)), active: !selected.active }; updateMutation.mutate({ id: selected.id, expectedRowVersion: selected.rowVersion, request, idempotencyKey: crypto.randomUUID() }); }
  function submitLifecycle(action: SalesProductGroupLifecycleAction) { if (!selected) return; const fingerprint = JSON.stringify({ id: selected.id, action, rowVersion: selected.rowVersion }); if (lifecycleIdentity.current?.fingerprint !== fingerprint) lifecycleIdentity.current = { fingerprint, idempotencyKey: crypto.randomUUID() }; lifecycleMutation.mutate({ id: selected.id, action, expectedRowVersion: selected.rowVersion, idempotencyKey: lifecycleIdentity.current.idempotencyKey }); }

  return <section className="supplier-master-prototype" aria-labelledby="sales-product-group-master-title">
    <ModuleSubnav locale={locale} items={navItems} ariaLabel={labels.eyebrow} />
    <header className="supplier-master-header"><div><p className="eyebrow">{labels.eyebrow}</p><h1 id="sales-product-group-master-title">{labels.title}</h1></div><div className="supplier-master-header-actions"><Link className="supplier-secondary-action" to="/product/lifecycle">{labels.workEntry}</Link><button className="supplier-primary-action" type="button" onClick={beginCreate} disabled={busy}>＋{labels.newItem}</button></div></header>
    <div className="supplier-master-toolbar"><label className="supplier-search"><input type="search" value={search} aria-label={labels.search} onChange={(event) => setSearch(event.target.value)} /></label><div className="supplier-filter-tabs" role="group" aria-label={labels.status}>{(['all','active','inactive','deleted'] as const).map(candidate => <button key={candidate} type="button" className={filter === candidate ? 'is-active' : ''} onClick={() => { setFilter(candidate); setSelectedId(''); setMode('detail'); }}>{labels[candidate]}</button>)}</div></div>
    <div className="supplier-master-grid"><aside className="supplier-list-panel" aria-label={labels.item}><div className="supplier-list-meta"><strong>{labels.item}</strong><span>{items.length} {labels.records}</span></div><div className="supplier-list-body">{items.map(item => <div key={item.id} role="button" tabIndex={0} className={`supplier-list-item${selected?.id === item.id ? ` is-selected` : ``}`} onClick={() => selectItem(item)} onKeyDown={(event) => handleListKey(event,item)}><span className="supplier-list-name"><strong>{displayName(item)}</strong>{secondaryName(item) && <small>{secondaryName(item)}</small>}</span><StatusPill item={item} labels={labels}/></div>)}{!itemsQuery.isPending && items.length === 0 && <p className="supplier-list-empty">{labels.noResult}</p>}</div></aside>
      <article className="supplier-detail-panel">{mode === 'create' || mode === 'edit' ? <form className="supplier-editor" onSubmit={saveDraft}><header className="supplier-detail-header supplier-editor-header"><h2>{mode === 'create' ? labels.createTitle : labels.editTitle}</h2></header><section className="supplier-detail-section"><h3>{labels.basicInfo}</h3><div className="supplier-form-grid"><label><span className="field-label">{labels.nameZhTw}</span><input value={draft.nameZhTw} onChange={e => { setDraft({...draft,nameZhTw:e.target.value}); setFormError(null); writeIdentity.current=null; }}/></label><label><span className="field-label">{labels.nameThTh}</span><input value={draft.nameThTh} onChange={e => { setDraft({...draft,nameThTh:e.target.value}); setFormError(null); writeIdentity.current=null; }}/></label><fieldset className="supplier-state-fieldset supplier-form-wide"><legend className="field-label">{labels.activeState}</legend><label className={draft.active?'is-selected':''}><input type="radio" name="sales-product-group-active" checked={draft.active} onChange={() => setDraft({...draft,active:true})}/>{labels.active}</label><label className={!draft.active?'is-selected':''}><input type="radio" name="sales-product-group-active" checked={!draft.active} onChange={() => setDraft({...draft,active:false})}/>{labels.inactive}</label></fieldset></div></section>{(formError ?? problemMessage) && <div className="problem-banner" role="alert">{formError ?? problemMessage}</div>}<footer className="supplier-editor-footer"><button className="supplier-secondary-action" type="button" disabled={busy} onClick={cancelEditor}>{labels.cancel}</button><button className="supplier-primary-action" type="submit" disabled={busy}>{busy?labels.saving:labels.save}</button></footer></form> : selected ? <><header className="supplier-detail-header"><div><div className="supplier-detail-title-row"><h2>{displayName(selected)}</h2><StatusPill item={selected} labels={labels}/></div>{secondaryName(selected) && <p>{secondaryName(selected)}</p>}</div><div className="supplier-detail-actions">{selected.deletedAt ? <button className="supplier-primary-action" type="button" disabled={busy} onClick={() => submitLifecycle('restore')}>{labels.restore}</button> : <><button className="supplier-secondary-action" type="button" disabled={busy} onClick={beginEdit}>{labels.edit}</button><button className="supplier-secondary-action" type="button" disabled={busy} onClick={toggleActive}>{selected.active?labels.deactivate:labels.activate}</button><button className="supplier-danger-ghost-action" type="button" disabled={busy} onClick={() => setDeleteOpen(true)}>{labels.softDelete}</button></>}</div></header><section className="supplier-detail-section"><h3>{labels.basicInfo}</h3><dl className="supplier-detail-fields"><div className="supplier-detail-field"><dt>{labels.nameZhTw}</dt><dd>{selected.nameZhTw || labels.notProvided}</dd></div><div className="supplier-detail-field"><dt>{labels.nameThTh}</dt><dd>{selected.nameThTh || labels.notProvided}</dd></div></dl></section>{problemMessage && <div className="problem-banner" role="alert">{problemMessage}</div>}</> : <div className="supplier-list-empty">{itemsQuery.isPending?'…':problemMessage??labels.noResult}</div>}</article>
    </div>
    {deleteOpen && selected && <div className="supplier-dialog-backdrop" role="presentation" onMouseDown={() => setDeleteOpen(false)}><div className="supplier-dialog" role="dialog" aria-modal="true" aria-labelledby="sales-product-group-delete-title" onMouseDown={e=>e.stopPropagation()}><div className="supplier-dialog-icon" aria-hidden="true">!</div><div><h2 id="sales-product-group-delete-title">{labels.deleteTitle}</h2><p>{displayName(selected)}</p><p className="supplier-dialog-message">{labels.deleteMessage}</p></div><div className="supplier-dialog-actions"><button type="button" className="supplier-secondary-action" onClick={() => setDeleteOpen(false)}>{labels.cancel}</button><button type="button" className="supplier-danger-action" disabled={busy} onClick={() => submitLifecycle('soft-delete')}>{labels.confirmDelete}</button></div></div></div>}
  </section>;
}

function StatusPill({item,labels}:{item:SalesProductGroupMasterItem;labels:Labels}) { const state=item.deletedAt?'deleted':item.active?'active':'inactive'; return <span className={`supplier-status-pill is-${state}`}>{labels[state]}</span>; }
