import { useDeferredValue, useMemo, useRef, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiProblemError } from '../../app/api/apiTransport';
import { useOperationalLocale } from '../../app/i18n/locale';
import { ModuleSubnav, type ModuleSubnavItem } from '../../app/modules/ModuleSubnav';
import {
  createSalesProduct,
  listSalesProductGroupsForMaster,
  listSalesProducts,
  listSalesProductStorageLocations,
  updateSalesProduct,
  type MasterStatus,
  type SalesProductDraft,
  type SalesProductItem,
} from './productMasters';
import '../party/supplierMasterPrototype.css';

type EditorMode = 'detail' | 'create' | 'edit';
type Draft = {
  salesProductGroupId: string;
  nameZhTw: string;
  nameThTh: string;
  pricingBasis: 'WEIGHT_BASED_UNIT' | 'UNIT_BASED';
  packagingWeight: string;
  salesWeight: string;
  defaultStorageLocationId: string;
  active: boolean;
};
type SubmissionIdentity = { fingerprint: string; idempotencyKey: string };

const navItems: readonly ModuleSubnavItem[] = [
  { to: '/product', end: true, label: { 'zh-TW': '採購產品', 'th-TH': 'สินค้าจัดซื้อ' } },
  { to: '/product/sales-products', label: { 'zh-TW': '銷售產品', 'th-TH': 'สินค้าขาย' } },
  { to: '/product/sales-product-groups', label: { 'zh-TW': '銷售產品群組', 'th-TH': 'กลุ่มสินค้าขาย' } },
];

const copy = {
  'zh-TW': {
    nav: '產品管理', eyebrow: '產品管理', title: '銷售產品', newItem: '新增產品', search: '搜尋銷售產品',
    all: '全部', active: '使用中', inactive: '停用', deleted: '已刪除', records: '筆資料', basicInfo: '基本資料',
    group: '產品群組', nameZhTw: '中文名稱', nameThTh: '泰文名稱', pricingBasis: '計價基礎', weightBased: '重量基礎', unitBased: '數量基礎',
    packagingWeight: '包裝重量', salesWeight: '銷售重量', storage: '預設儲位', noStorage: '未指定', activeState: '啟用狀態',
    edit: '編輯', activate: '啟用', deactivate: '停用', save: '儲存', saving: '儲存中…', cancel: '取消', createTitle: '新增銷售產品', editTitle: '編輯銷售產品',
    noResult: '沒有符合條件的銷售產品。', required: '請確認產品群組、名稱與計價欄位。', queryFailed: '無法載入銷售產品', unexpected: '操作失敗，請重新整理後再試。',
    stale: '資料已被其他操作更新，請重新整理後再試。', notFound: '銷售產品或參考資料不存在。', deletedConflict: '這筆產品已刪除，不能修改。', notProvided: '—',
  },
  'th-TH': {
    nav: 'การตั้งค่าสินค้า', eyebrow: 'การตั้งค่าสินค้า', title: 'สินค้าขาย', newItem: 'เพิ่มสินค้า', search: 'ค้นหาสินค้าขาย',
    all: 'ทั้งหมด', active: 'ใช้งาน', inactive: 'ไม่ใช้งาน', deleted: 'ลบแล้ว', records: 'รายการ', basicInfo: 'ข้อมูลพื้นฐาน',
    group: 'กลุ่มสินค้า', nameZhTw: 'ชื่อภาษาจีน', nameThTh: 'ชื่อภาษาไทย', pricingBasis: 'เกณฑ์ราคา', weightBased: 'ตามน้ำหนัก', unitBased: 'ตามจำนวน',
    packagingWeight: 'น้ำหนักบรรจุ', salesWeight: 'น้ำหนักขาย', storage: 'ตำแหน่งจัดเก็บเริ่มต้น', noStorage: 'ไม่ระบุ', activeState: 'สถานะการใช้งาน',
    edit: 'แก้ไข', activate: 'เปิดใช้งาน', deactivate: 'ปิดใช้งาน', save: 'บันทึก', saving: 'กำลังบันทึก…', cancel: 'ยกเลิก', createTitle: 'เพิ่มสินค้าขาย', editTitle: 'แก้ไขสินค้าขาย',
    noResult: 'ไม่พบสินค้าขายที่ตรงกับเงื่อนไข', required: 'กรุณาตรวจสอบกลุ่มสินค้า ชื่อ และข้อมูลการคิดราคา', queryFailed: 'ไม่สามารถโหลดสินค้าขายได้', unexpected: 'ดำเนินการไม่สำเร็จ กรุณารีเฟรชแล้วลองใหม่',
    stale: 'ข้อมูลถูกแก้ไขแล้ว กรุณารีเฟรชแล้วลองใหม่', notFound: 'ไม่พบสินค้าขายหรือข้อมูลอ้างอิง', deletedConflict: 'สินค้านี้ถูกลบแล้วและไม่สามารถแก้ไขได้', notProvided: '—',
  },
} as const;

const emptyDraft = (): Draft => ({ salesProductGroupId: '', nameZhTw: '', nameThTh: '', pricingBasis: 'UNIT_BASED', packagingWeight: '', salesWeight: '', defaultStorageLocationId: '', active: true });
const toDraft = (item: SalesProductItem): Draft => ({ salesProductGroupId: item.salesProductGroupId, nameZhTw: item.nameZhTw ?? '', nameThTh: item.nameThTh ?? '', pricingBasis: item.pricingBasis, packagingWeight: item.packagingWeight?.toString() ?? '', salesWeight: item.salesWeight?.toString() ?? '', defaultStorageLocationId: item.defaultStorageLocationId ?? '', active: item.active });
function toRequest(draft: Draft): SalesProductDraft | null {
  const packagingWeight = draft.packagingWeight.trim() ? Number(draft.packagingWeight) : null;
  const salesWeight = draft.salesWeight.trim() ? Number(draft.salesWeight) : null;
  if (!draft.salesProductGroupId || (!draft.nameZhTw.trim() && !draft.nameThTh.trim()) || (packagingWeight !== null && (!Number.isFinite(packagingWeight) || packagingWeight <= 0))) return null;
  if (draft.pricingBasis === 'WEIGHT_BASED_UNIT' && (salesWeight === null || !Number.isFinite(salesWeight) || salesWeight < 0)) return null;
  if (draft.pricingBasis === 'UNIT_BASED' && salesWeight !== null) return null;
  return { salesProductGroupId: draft.salesProductGroupId, nameZhTw: draft.nameZhTw.trim() || null, nameThTh: draft.nameThTh.trim() || null, pricingBasis: draft.pricingBasis, packagingWeight, salesWeight, defaultStorageLocationId: draft.defaultStorageLocationId || null, active: draft.active };
}

export function SalesProductMasterPage() {
  const { locale } = useOperationalLocale(); const labels = copy[locale]; const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState(''); const [mode, setMode] = useState<EditorMode>('detail'); const [draft, setDraft] = useState<Draft>(emptyDraft);
  const [filter, setFilter] = useState<MasterStatus>('all'); const [search, setSearch] = useState(''); const deferredSearch = useDeferredValue(search); const [formError, setFormError] = useState<string | null>(null); const identity = useRef<SubmissionIdentity | null>(null);

  const itemsQuery = useQuery({ queryKey: ['sales-product-master', locale, deferredSearch, filter], queryFn: ({ signal }) => listSalesProducts({ locale, search: deferredSearch, status: filter, limit: 200, signal }), staleTime: 2_000 });
  const groupsQuery = useQuery({ queryKey: ['sales-product-master-groups', locale], queryFn: ({ signal }) => listSalesProductGroupsForMaster(locale, signal), staleTime: 10_000 });
  const storageQuery = useQuery({ queryKey: ['sales-product-storage-options', locale], queryFn: ({ signal }) => listSalesProductStorageLocations(locale, signal), staleTime: 10_000 });
  const items = itemsQuery.data?.items ?? []; const selected = items.find((item) => item.id === selectedId) ?? (mode === 'detail' ? items[0] ?? null : null);

  const createMutation = useMutation({ mutationFn: (input: { request: SalesProductDraft; idempotencyKey: string }) => createSalesProduct(input.request, { locale, idempotencyKey: input.idempotencyKey }), onSuccess: async (result) => { setFilter('all'); setSelectedId(result.salesProductId); setMode('detail'); identity.current = null; await invalidate(); } });
  const updateMutation = useMutation({ mutationFn: (input: { id: string; rowVersion: number; request: SalesProductDraft; idempotencyKey: string }) => updateSalesProduct(input.id, { ...input.request, expectedRowVersion: input.rowVersion }, { locale, idempotencyKey: input.idempotencyKey }), onSuccess: async (result) => { setSelectedId(result.salesProductId); setMode('detail'); identity.current = null; await invalidate(); } });
  async function invalidate() { await Promise.all([queryClient.invalidateQueries({ queryKey: ['sales-product-master'] }), queryClient.invalidateQueries({ queryKey: ['sales-confirmation-options'] }), queryClient.invalidateQueries({ queryKey: ['outsourced-supply-detail-options'] })]); }

  const activeError = createMutation.error ?? updateMutation.error ?? itemsQuery.error ?? groupsQuery.error ?? storageQuery.error;
  const problemMessage = useMemo(() => { if (!activeError) return null; if (!(activeError instanceof ApiProblemError)) return itemsQuery.error ? labels.queryFailed : labels.unexpected; if (activeError.code.includes('stale-row-version')) return labels.stale; if (activeError.code.includes('not-found')) return labels.notFound; if (activeError.code.includes('.deleted')) return labels.deletedConflict; return `${activeError.code}${activeError.problem.traceId ? ` · ${activeError.problem.traceId}` : ''}`; }, [activeError, itemsQuery.error, labels]);
  const displayName = (item: SalesProductItem) => (locale === 'zh-TW' ? item.nameZhTw : item.nameThTh)?.trim() || (locale === 'zh-TW' ? item.nameThTh : item.nameZhTw)?.trim() || labels.notProvided;
  const busy = createMutation.isPending || updateMutation.isPending;
  function select(item: SalesProductItem) { setSelectedId(item.id); setDraft(toDraft(item)); setMode('detail'); setFormError(null); identity.current = null; }
  function beginCreate() { setSelectedId(''); setDraft(emptyDraft()); setMode('create'); setFormError(null); identity.current = null; }
  function beginEdit() { if (selected && !selected.deletedAt) { setDraft(toDraft(selected)); setMode('edit'); setFormError(null); identity.current = null; } }
  function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); const request = toRequest(draft); if (!request) { setFormError(labels.required); return; } setFormError(null); const fingerprint = JSON.stringify({ mode, selectedId, rowVersion: selected?.rowVersion ?? null, request }); if (identity.current?.fingerprint !== fingerprint) identity.current = { fingerprint, idempotencyKey: crypto.randomUUID() }; if (mode === 'create') createMutation.mutate({ request, idempotencyKey: identity.current.idempotencyKey }); else if (mode === 'edit' && selected) updateMutation.mutate({ id: selected.id, rowVersion: selected.rowVersion, request, idempotencyKey: identity.current.idempotencyKey }); }
  function toggleActive() { if (!selected || selected.deletedAt) return; const request = toRequest(toDraft(selected)); if (request) updateMutation.mutate({ id: selected.id, rowVersion: selected.rowVersion, request: { ...request, active: !selected.active }, idempotencyKey: crypto.randomUUID() }); }

  return <section className="supplier-master-prototype" aria-labelledby="sales-product-master-title"><ModuleSubnav locale={locale} items={navItems} ariaLabel={labels.nav} />
    <header className="supplier-master-header"><div><p className="eyebrow">{labels.eyebrow}</p><h1 id="sales-product-master-title">{labels.title}</h1></div><button className="supplier-primary-action" type="button" onClick={beginCreate} disabled={busy}>＋{labels.newItem}</button></header>
    <div className="supplier-master-toolbar"><label className="supplier-search"><input type="search" value={search} aria-label={labels.search} onChange={(e) => setSearch(e.target.value)} /></label><div className="supplier-filter-tabs">{(['all','active','inactive','deleted'] as const).map((candidate) => <button key={candidate} type="button" className={filter === candidate ? 'is-active' : ''} onClick={() => { setFilter(candidate); setSelectedId(''); setMode('detail'); }}>{labels[candidate]}</button>)}</div></div>
    <div className="supplier-master-grid"><aside className="supplier-list-panel"><div className="supplier-list-meta"><strong>{labels.title}</strong><span>{items.length} {labels.records}</span></div><div className="supplier-list-body">{items.map((item) => <button type="button" key={item.id} className={`supplier-list-item${selected?.id === item.id ? ' is-selected' : ''}`} onClick={() => select(item)}><span className="supplier-list-name"><strong>{displayName(item)}</strong><small>{item.salesProductGroupDisplayName}</small></span><StatusPill item={item} labels={labels} /></button>)}{!itemsQuery.isPending && items.length === 0 && <p className="supplier-list-empty">{labels.noResult}</p>}</div></aside>
      <article className="supplier-detail-panel">{mode !== 'detail' ? <form className="supplier-editor" onSubmit={submit}><header className="supplier-detail-header supplier-editor-header"><h2>{mode === 'create' ? labels.createTitle : labels.editTitle}</h2></header><section className="supplier-detail-section"><h3>{labels.basicInfo}</h3><div className="supplier-form-grid"><label><span className="field-label">{labels.group}</span><select value={draft.salesProductGroupId} onChange={(e) => setDraft({ ...draft, salesProductGroupId: e.target.value })}><option value="">—</option>{groupsQuery.data?.items.map((item) => <option key={item.id} value={item.id}>{item.displayName}</option>)}</select></label><TextField label={labels.nameZhTw} value={draft.nameZhTw} onChange={(value) => setDraft({ ...draft, nameZhTw: value })} /><TextField label={labels.nameThTh} value={draft.nameThTh} onChange={(value) => setDraft({ ...draft, nameThTh: value })} /><label><span className="field-label">{labels.pricingBasis}</span><select value={draft.pricingBasis} onChange={(e) => setDraft({ ...draft, pricingBasis: e.target.value as Draft['pricingBasis'], salesWeight: e.target.value === 'UNIT_BASED' ? '' : draft.salesWeight })}><option value="UNIT_BASED">{labels.unitBased}</option><option value="WEIGHT_BASED_UNIT">{labels.weightBased}</option></select></label><NumberField label={labels.packagingWeight} value={draft.packagingWeight} onChange={(value) => setDraft({ ...draft, packagingWeight: value })} /><NumberField label={labels.salesWeight} value={draft.salesWeight} disabled={draft.pricingBasis === 'UNIT_BASED'} onChange={(value) => setDraft({ ...draft, salesWeight: value })} /><label><span className="field-label">{labels.storage}</span><select value={draft.defaultStorageLocationId} onChange={(e) => setDraft({ ...draft, defaultStorageLocationId: e.target.value })}><option value="">{labels.noStorage}</option>{storageQuery.data?.items.map((item) => <option key={item.id} value={item.id}>{item.code ? `${item.code} · ` : ''}{item.displayName}</option>)}</select></label><fieldset className="supplier-state-fieldset supplier-form-wide"><legend className="field-label">{labels.activeState}</legend><label className={draft.active ? 'is-selected' : ''}><input type="radio" checked={draft.active} onChange={() => setDraft({ ...draft, active: true })} />{labels.active}</label><label className={!draft.active ? 'is-selected' : ''}><input type="radio" checked={!draft.active} onChange={() => setDraft({ ...draft, active: false })} />{labels.inactive}</label></fieldset></div></section>{(formError ?? problemMessage) && <div className="problem-banner" role="alert">{formError ?? problemMessage}</div>}<footer className="supplier-editor-footer"><button type="button" className="supplier-secondary-action" onClick={() => setMode('detail')}>{labels.cancel}</button><button className="supplier-primary-action" type="submit" disabled={busy}>{busy ? labels.saving : labels.save}</button></footer></form> : selected ? <><header className="supplier-detail-header"><div><div className="supplier-detail-title-row"><h2>{displayName(selected)}</h2><StatusPill item={selected} labels={labels} /></div></div>{!selected.deletedAt && <div className="supplier-detail-actions"><button className="supplier-secondary-action" type="button" onClick={beginEdit}>{labels.edit}</button><button className="supplier-secondary-action" type="button" onClick={toggleActive}>{selected.active ? labels.deactivate : labels.activate}</button></div>}</header><section className="supplier-detail-section"><h3>{labels.basicInfo}</h3><dl className="supplier-detail-fields"><Detail label={labels.group} value={selected.salesProductGroupDisplayName} /><Detail label={labels.nameZhTw} value={selected.nameZhTw || labels.notProvided} /><Detail label={labels.nameThTh} value={selected.nameThTh || labels.notProvided} /><Detail label={labels.pricingBasis} value={selected.pricingBasis === 'WEIGHT_BASED_UNIT' ? labels.weightBased : labels.unitBased} /><Detail label={labels.packagingWeight} value={selected.packagingWeight?.toString() ?? labels.notProvided} /><Detail label={labels.salesWeight} value={selected.salesWeight?.toString() ?? labels.notProvided} /><Detail label={labels.storage} value={selected.defaultStorageLocationDisplayName || labels.noStorage} /></dl></section>{problemMessage && <div className="problem-banner" role="alert">{problemMessage}</div>}</> : <p className="supplier-list-empty">{itemsQuery.isPending ? '…' : labels.noResult}</p>}</article></div>
  </section>;
}

function TextField({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) { return <label><span className="field-label">{label}</span><input value={value} onChange={(e) => onChange(e.target.value)} /></label>; }
function NumberField({ label, value, disabled, onChange }: { label: string; value: string; disabled?: boolean; onChange: (value: string) => void }) { return <label><span className="field-label">{label}</span><input type="number" step="any" min="0" disabled={disabled} value={value} onChange={(e) => onChange(e.target.value)} /></label>; }
function Detail({ label, value }: { label: string; value: string }) { return <div className="supplier-detail-field"><dt>{label}</dt><dd>{value}</dd></div>; }
function StatusPill({ item, labels }: { item: SalesProductItem; labels: typeof copy['zh-TW'] | typeof copy['th-TH'] }) { const state = item.deletedAt ? 'deleted' : item.active ? 'active' : 'inactive'; return <span className={`supplier-status-pill is-${state}`}>{labels[state]}</span>; }
