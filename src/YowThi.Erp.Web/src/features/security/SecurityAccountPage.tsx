import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useRef, useState, type FormEvent } from 'react';

import { useOperationalLocale } from '../../app/i18n/locale';
import {
  ApiProblemError,
  createSecurityAccount,
  listSecurityAccounts,
  updateSecurityAccount,
  type SecurityAccountDraftRequest,
  type SecurityAccountItem,
} from './securityAccount';
import '../party/supplierMasterPrototype.css';
import './securityAccount.css';

type EditorMode = 'detail' | 'create' | 'edit';
type Draft = {
  displayName: string;
  active: boolean;
  identityIssuer: string;
  identitySubject: string;
  capabilities: string[];
};
type SubmissionIdentity = { fingerprint: string; idempotencyKey: string };

const copy = {
  'zh-TW': {
    eyebrow: '系統管理', title: '帳號與權限', newAccount: '新增帳號', search: '搜尋帳號名稱或登入身分',
    accounts: '帳號', records: '筆資料', active: '使用中', inactive: '停用', protected: '測試管理員', edit: '編輯',
    basic: '帳號資料', displayName: '顯示名稱', activeState: '啟用狀態', identity: '外部登入身分', issuer: '簽發者', subject: '識別碼',
    permissions: '模組與操作權限', noPermissions: '未授權任何操作', save: '儲存', saving: '儲存中…', cancel: '取消',
    createTitle: '新增帳號', editTitle: '編輯帳號', noResult: '沒有符合條件的帳號。', queryFailed: '無法載入帳號資料',
    unexpected: '操作失敗，請重新整理後再試。', invalid: '請填寫顯示名稱；外部登入身分必須同時填寫兩個欄位或全部留空。',
    protectedMessage: '開發測試管理員由系統維護，不能從一般帳號管理降低權限或停用。', notProvided: '—',
  },
  'th-TH': {
    eyebrow: 'จัดการระบบ', title: 'บัญชีและสิทธิ์', newAccount: 'เพิ่มบัญชี', search: 'ค้นหาชื่อบัญชีหรือข้อมูลเข้าสู่ระบบ',
    accounts: 'บัญชี', records: 'รายการ', active: 'ใช้งาน', inactive: 'ไม่ใช้งาน', protected: 'ผู้ดูแลทดสอบ', edit: 'แก้ไข',
    basic: 'ข้อมูลบัญชี', displayName: 'ชื่อที่แสดง', activeState: 'สถานะการใช้งาน', identity: 'ข้อมูลเข้าสู่ระบบภายนอก', issuer: 'ผู้ออกข้อมูล', subject: 'รหัสระบุตัวตน',
    permissions: 'สิทธิ์โมดูลและการทำงาน', noPermissions: 'ยังไม่ได้ให้สิทธิ์การทำงาน', save: 'บันทึก', saving: 'กำลังบันทึก…', cancel: 'ยกเลิก',
    createTitle: 'เพิ่มบัญชี', editTitle: 'แก้ไขบัญชี', noResult: 'ไม่พบบัญชีที่ตรงกับเงื่อนไข', queryFailed: 'ไม่สามารถโหลดข้อมูลบัญชีได้',
    unexpected: 'ดำเนินการไม่สำเร็จ กรุณารีเฟรชแล้วลองใหม่', invalid: 'กรุณาระบุชื่อที่แสดง และข้อมูลเข้าสู่ระบบภายนอกต้องกรอกทั้งสองช่องหรือเว้นว่างทั้งหมด',
    protectedMessage: 'ผู้ดูแลทดสอบสำหรับการพัฒนาถูกดูแลโดยระบบ และไม่สามารถลดสิทธิ์หรือปิดใช้งานจากการจัดการบัญชีทั่วไปได้', notProvided: '—',
  },
} as const;

function emptyDraft(): Draft {
  return { displayName: '', active: true, identityIssuer: '', identitySubject: '', capabilities: [] };
}

function toDraft(item: SecurityAccountItem): Draft {
  return {
    displayName: item.displayName,
    active: item.active,
    identityIssuer: item.identityIssuer ?? '',
    identitySubject: item.identitySubject ?? '',
    capabilities: [...item.capabilities],
  };
}

function toRequest(draft: Draft): SecurityAccountDraftRequest {
  const optional = (value: string) => value.trim() || null;
  return {
    displayName: draft.displayName.trim(),
    active: draft.active,
    identityIssuer: optional(draft.identityIssuer),
    identitySubject: optional(draft.identitySubject),
    capabilities: [...draft.capabilities].sort(),
  };
}

export function SecurityAccountPage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const deferredSearch = useDeferredValue(search);
  const [selectedId, setSelectedId] = useState('');
  const [mode, setMode] = useState<EditorMode>('detail');
  const [draft, setDraft] = useState<Draft>(emptyDraft);
  const [formError, setFormError] = useState<string | null>(null);
  const writeIdentity = useRef<SubmissionIdentity | null>(null);

  const accountsQuery = useQuery({
    queryKey: ['security-accounts', locale, deferredSearch],
    queryFn: ({ signal }) => listSecurityAccounts({ locale, search: deferredSearch, signal }),
    staleTime: 2_000,
  });
  const items = accountsQuery.data?.items ?? [];
  const selected = items.find((item) => item.id === selectedId)
    ?? (mode === 'detail' ? items[0] ?? null : null);
  const availableCapabilities = accountsQuery.data?.availableCapabilities ?? [];

  const createMutation = useMutation({
    mutationFn: (input: { request: SecurityAccountDraftRequest; idempotencyKey: string }) =>
      createSecurityAccount(input.request, { locale, idempotencyKey: input.idempotencyKey }),
    onSuccess: async (result) => {
      setSelectedId(result.accountId);
      setMode('detail');
      writeIdentity.current = null;
      await queryClient.invalidateQueries({ queryKey: ['security-accounts'] });
    },
  });
  const updateMutation = useMutation({
    mutationFn: (input: { id: string; expectedRowVersion: number; request: SecurityAccountDraftRequest; idempotencyKey: string }) =>
      updateSecurityAccount(input.id, { ...input.request, expectedRowVersion: input.expectedRowVersion }, { locale, idempotencyKey: input.idempotencyKey }),
    onSuccess: async (result) => {
      setSelectedId(result.accountId);
      setMode('detail');
      writeIdentity.current = null;
      await queryClient.invalidateQueries({ queryKey: ['security-accounts'] });
    },
  });

  const activeError = createMutation.error ?? updateMutation.error ?? accountsQuery.error;
  const problemMessage = useMemo(() => {
    if (!activeError) return null;
    if (!(activeError instanceof ApiProblemError)) return accountsQuery.error ? labels.queryFailed : labels.unexpected;
    return `${activeError.code}${activeError.problem.traceId ? ` · ${activeError.problem.traceId}` : ''}`;
  }, [activeError, accountsQuery.error, labels]);

  function beginCreate() {
    setSelectedId('');
    setDraft(emptyDraft());
    setFormError(null);
    setMode('create');
    writeIdentity.current = null;
  }

  function beginEdit() {
    if (!selected || selected.isDevelopmentTestAdmin) return;
    setDraft(toDraft(selected));
    setFormError(null);
    setMode('edit');
    writeIdentity.current = null;
  }

  function cancelEditor() {
    setMode('detail');
    setFormError(null);
    writeIdentity.current = null;
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const request = toRequest(draft);
    if (!request.displayName || Boolean(request.identityIssuer) !== Boolean(request.identitySubject)) {
      setFormError(labels.invalid);
      return;
    }

    setFormError(null);
    const fingerprint = JSON.stringify({ mode, selectedId, rowVersion: selected?.rowVersion ?? null, request });
    if (writeIdentity.current?.fingerprint !== fingerprint) {
      writeIdentity.current = { fingerprint, idempotencyKey: crypto.randomUUID() };
    }

    if (mode === 'create') {
      createMutation.mutate({ request, idempotencyKey: writeIdentity.current.idempotencyKey });
    } else if (mode === 'edit' && selected) {
      updateMutation.mutate({ id: selected.id, expectedRowVersion: selected.rowVersion, request, idempotencyKey: writeIdentity.current.idempotencyKey });
    }
  }

  const busy = createMutation.isPending || updateMutation.isPending;

  return (
    <section className="supplier-master-prototype security-account-page" aria-labelledby="security-account-title">
      <header className="supplier-master-header">
        <div><p className="eyebrow">{labels.eyebrow}</p><h1 id="security-account-title">{labels.title}</h1></div>
        <button className="supplier-primary-action" type="button" onClick={beginCreate} disabled={busy}><span aria-hidden="true">＋</span>{labels.newAccount}</button>
      </header>

      <div className="supplier-master-toolbar">
        <label className="supplier-search"><span aria-hidden="true">⌕</span><input type="search" value={search} aria-label={labels.search} onChange={(event) => setSearch(event.target.value)} /></label>
      </div>

      <div className="supplier-master-grid">
        <aside className="supplier-list-panel" aria-label={labels.accounts}>
          <div className="supplier-list-meta"><strong>{labels.accounts}</strong><span>{items.length} {labels.records}</span></div>
          <div className="security-account-list">
            {items.map((item) => (
              <button key={item.id} type="button" className={`security-account-list-item${selected?.id === item.id ? ' is-selected' : ''}`} onClick={() => { setSelectedId(item.id); setMode('detail'); }}>
                <span><strong>{item.displayName}</strong><small>{item.isDevelopmentTestAdmin ? labels.protected : item.identitySubject || labels.notProvided}</small></span>
                <span className={`supplier-status-pill is-${item.active ? 'active' : 'inactive'}`}>{item.active ? labels.active : labels.inactive}</span>
              </button>
            ))}
            {!accountsQuery.isPending && items.length === 0 ? <p className="supplier-list-empty">{labels.noResult}</p> : null}
          </div>
        </aside>

        <article className="supplier-detail-panel">
          {mode === 'create' || mode === 'edit' ? (
            <AccountEditor
              mode={mode}
              draft={draft}
              labels={labels}
              availableCapabilities={availableCapabilities}
              busy={busy}
              error={formError ?? problemMessage}
              onDraftChange={(next) => { setDraft(next); setFormError(null); writeIdentity.current = null; }}
              onSubmit={submit}
              onCancel={cancelEditor}
            />
          ) : selected ? (
            <>
              <header className="supplier-detail-header">
                <div><div className="supplier-detail-title-row"><h2>{selected.displayName}</h2><span className={`supplier-status-pill is-${selected.active ? 'active' : 'inactive'}`}>{selected.active ? labels.active : labels.inactive}</span></div></div>
                {!selected.isDevelopmentTestAdmin ? <button className="supplier-secondary-action" type="button" onClick={beginEdit}>{labels.edit}</button> : null}
              </header>
              {selected.isDevelopmentTestAdmin ? <div className="security-protected-banner">{labels.protectedMessage}</div> : null}
              <section className="supplier-detail-section"><h3>{labels.basic}</h3><div className="supplier-detail-fields"><div className="supplier-detail-field"><dt>{labels.displayName}</dt><dd>{selected.displayName}</dd></div><div className="supplier-detail-field"><dt>{labels.activeState}</dt><dd>{selected.active ? labels.active : labels.inactive}</dd></div></div></section>
              <section className="supplier-detail-section"><h3>{labels.identity}</h3><div className="supplier-detail-fields"><div className="supplier-detail-field"><dt>{labels.issuer}</dt><dd>{selected.identityIssuer ?? labels.notProvided}</dd></div><div className="supplier-detail-field"><dt>{labels.subject}</dt><dd>{selected.identitySubject ?? labels.notProvided}</dd></div></div></section>
              <section className="supplier-detail-section"><h3>{labels.permissions}</h3><div className="security-capability-chips">{selected.capabilities.length ? selected.capabilities.map((capability) => <span key={capability}>{capabilityLabel(capability, locale)}</span>) : <p>{labels.noPermissions}</p>}</div></section>
            </>
          ) : <div className="supplier-list-empty">{accountsQuery.isPending ? '…' : problemMessage ?? labels.noResult}</div>}
        </article>
      </div>
    </section>
  );
}

function AccountEditor({ mode, draft, labels, availableCapabilities, busy, error, onDraftChange, onSubmit, onCancel }: {
  mode: 'create' | 'edit'; draft: Draft; labels: typeof copy['zh-TW'] | typeof copy['th-TH']; availableCapabilities: string[]; busy: boolean; error: string | null;
  onDraftChange: (draft: Draft) => void; onSubmit: (event: FormEvent<HTMLFormElement>) => void; onCancel: () => void;
}) {
  const update = <K extends keyof Draft>(key: K, value: Draft[K]) => onDraftChange({ ...draft, [key]: value });
  const toggleCapability = (capability: string) => update('capabilities', draft.capabilities.includes(capability) ? draft.capabilities.filter((item) => item !== capability) : [...draft.capabilities, capability]);
  return (
    <form className="supplier-editor" onSubmit={onSubmit}>
      <header className="supplier-detail-header supplier-editor-header"><h2>{mode === 'create' ? labels.createTitle : labels.editTitle}</h2></header>
      <section className="supplier-detail-section"><h3>{labels.basic}</h3><div className="supplier-form-grid"><label><span className="field-label">{labels.displayName}</span><input value={draft.displayName} onChange={(event) => update('displayName', event.target.value)} /></label><fieldset className="supplier-state-fieldset"><legend className="field-label">{labels.activeState}</legend><label className={draft.active ? 'is-selected' : ''}><input type="radio" name="account-active" checked={draft.active} onChange={() => update('active', true)} />{labels.active}</label><label className={!draft.active ? 'is-selected' : ''}><input type="radio" name="account-active" checked={!draft.active} onChange={() => update('active', false)} />{labels.inactive}</label></fieldset></div></section>
      <section className="supplier-detail-section"><h3>{labels.identity}</h3><div className="supplier-form-grid"><label><span className="field-label">{labels.issuer}</span><input value={draft.identityIssuer} onChange={(event) => update('identityIssuer', event.target.value)} /></label><label><span className="field-label">{labels.subject}</span><input value={draft.identitySubject} onChange={(event) => update('identitySubject', event.target.value)} /></label></div></section>
      <section className="supplier-detail-section"><h3>{labels.permissions}</h3><div className="security-capability-grid">{availableCapabilities.map((capability) => <label key={capability} className={draft.capabilities.includes(capability) ? 'is-selected' : ''}><input type="checkbox" checked={draft.capabilities.includes(capability)} onChange={() => toggleCapability(capability)} /><span>{capabilityLabel(capability, document.documentElement.lang === 'th-TH' ? 'th-TH' : 'zh-TW')}</span></label>)}</div></section>
      {error ? <div className="problem-banner" role="alert">{error}</div> : null}
      <footer className="supplier-editor-footer"><button className="supplier-secondary-action" type="button" disabled={busy} onClick={onCancel}>{labels.cancel}</button><button className="supplier-primary-action" type="submit" disabled={busy}>{busy ? labels.saving : labels.save}</button></footer>
    </form>
  );
}

const capabilityLabels: Record<string, { 'zh-TW': string; 'th-TH': string }> = {
  'procurement.confirm': { 'zh-TW': '採購登記', 'th-TH': 'บันทึกจัดซื้อ' },
  'procurement.batch.lifecycle': { 'zh-TW': '採購批次控制', 'th-TH': 'ควบคุมชุดจัดซื้อ' },
  'processing.confirm': { 'zh-TW': '加工登記', 'th-TH': 'บันทึกการแปรรูป' },
  'outsourced.confirm': { 'zh-TW': '委外登記', 'th-TH': 'บันทึกงานภายนอก' },
  'sales.confirm': { 'zh-TW': '銷售確認', 'th-TH': 'ยืนยันการขาย' },
  'sales.correct-allocation': { 'zh-TW': '銷售配置修正', 'th-TH': 'แก้ไขการจัดสรรการขาย' },
  'sales-handling.work-record.record': { 'zh-TW': '銷售作業登記', 'th-TH': 'บันทึกงานบรรจุขาย' },
  'sales-handling.packaging-item.lifecycle': { 'zh-TW': '銷售作業項目控制', 'th-TH': 'ควบคุมรายการงานบรรจุ' },
  'product.sales-product-group.lifecycle': { 'zh-TW': '銷售產品群組控制', 'th-TH': 'ควบคุมกลุ่มสินค้าขาย' },
  'infrastructure.container.lifecycle': { 'zh-TW': '容器控制', 'th-TH': 'ควบคุมภาชนะ' },
  'infrastructure.warehouse.lifecycle': { 'zh-TW': '倉庫控制', 'th-TH': 'ควบคุมคลังสินค้า' },
  'labor.daily-wage.confirm': { 'zh-TW': '每日工資確認', 'th-TH': 'ยืนยันค่าจ้างรายวัน' },
  'finance.pay': { 'zh-TW': '收付款登記', 'th-TH': 'บันทึกรับจ่ายเงิน' },
  'finance.correct': { 'zh-TW': '財務修正', 'th-TH': 'แก้ไขการเงิน' },
  'inventory.adjust': { 'zh-TW': '庫存調整', 'th-TH': 'ปรับสินค้าคงคลัง' },
  'party.supplier.lifecycle': { 'zh-TW': '供應商管理', 'th-TH': 'จัดการผู้จำหน่าย' },
  'party.farmer.lifecycle': { 'zh-TW': '農戶管理', 'th-TH': 'จัดการเกษตรกร' },
  'party.employee.lifecycle': { 'zh-TW': '員工管理', 'th-TH': 'จัดการพนักงาน' },
  'party.customer.lifecycle': { 'zh-TW': '客戶管理', 'th-TH': 'จัดการลูกค้า' },
  'party.outsourced-vendor.lifecycle': { 'zh-TW': '委外供應商管理', 'th-TH': 'จัดการผู้รับจ้างภายนอก' },
  'data-protection.hard-delete': { 'zh-TW': '永久刪除', 'th-TH': 'ลบถาวร' },
  'security.account.manage': { 'zh-TW': '帳號與權限管理', 'th-TH': 'จัดการบัญชีและสิทธิ์' },
};

function capabilityLabel(capability: string, locale: 'zh-TW' | 'th-TH'): string {
  return capabilityLabels[capability]?.[locale] ?? capability;
}
