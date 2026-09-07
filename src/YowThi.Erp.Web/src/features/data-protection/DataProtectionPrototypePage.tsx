import { useMemo, useState } from 'react';

import { useOperationalLocale } from '../../app/i18n/locale';
import {
  getPrototypeSuppliers,
  hardDeletePrototypeSupplier,
  usePrototypeSuppliers,
  type PrototypeSupplier,
} from '../party/supplierMasterPrototypeStore';
import './dataProtectionPrototype.css';

const copy = {
  'zh-TW': {
    eyebrow: '資料保護',
    title: '硬刪除',
    prototype: '操作原型',
    supplier: '供應商',
    deletedData: '已軟刪除資料',
    records: '筆資料',
    phone: '電話',
    deletedAt: '刪除時間',
    rowVersion: '資料版本',
    bankAccount: '銀行帳號',
    hardDelete: '永久刪除',
    dialogTitle: '永久刪除供應商',
    irreversible: '此操作會永久移除資料，無法恢復。',
    cancel: '取消',
    confirm: '確認永久刪除',
    empty: '目前沒有已軟刪除的供應商。',
    notProvided: '—',
  },
  'th-TH': {
    eyebrow: 'การคุ้มครองข้อมูล',
    title: 'ลบถาวร',
    prototype: 'ต้นแบบการใช้งาน',
    supplier: 'ผู้จำหน่าย',
    deletedData: 'ข้อมูลที่ลบแบบเก็บประวัติแล้ว',
    records: 'รายการ',
    phone: 'โทรศัพท์',
    deletedAt: 'เวลาที่ลบ',
    rowVersion: 'เวอร์ชันข้อมูล',
    bankAccount: 'เลขบัญชีธนาคาร',
    hardDelete: 'ลบถาวร',
    dialogTitle: 'ลบผู้จำหน่ายถาวร',
    irreversible: 'การดำเนินการนี้จะลบข้อมูลอย่างถาวรและไม่สามารถกู้คืนได้',
    cancel: 'ยกเลิก',
    confirm: 'ยืนยันการลบถาวร',
    empty: 'ขณะนี้ไม่มีผู้จำหน่ายที่ถูกลบแบบเก็บประวัติ',
    notProvided: '—',
  },
} as const;

export function DataProtectionPrototypePage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const suppliers = usePrototypeSuppliers();
  const deletedItems = useMemo(() => suppliers.filter((item) => item.deleted), [suppliers]);
  const [selectedId, setSelectedId] = useState(() => deletedItems[0]?.id ?? '');
  const [confirmOpen, setConfirmOpen] = useState(false);
  const selected = deletedItems.find((item) => item.id === selectedId) ?? deletedItems[0] ?? null;

  const displayName = (item: PrototypeSupplier) => {
    const primary = locale === 'zh-TW' ? item.nameZhTw : item.nameThTh;
    const fallback = locale === 'zh-TW' ? item.nameThTh : item.nameZhTw;
    return primary.trim() || fallback.trim() || labels.notProvided;
  };

  function formatDeletedAt(value: string | null) {
    if (!value) return labels.notProvided;
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
  }

  function confirmHardDelete() {
    if (selected === null) return;
    const currentId = selected.id;
    hardDeletePrototypeSupplier(currentId);
    const next = getPrototypeSuppliers().find((item) => item.deleted && item.id !== currentId) ?? null;
    setSelectedId(next?.id ?? '');
    setConfirmOpen(false);
  }

  return (
    <section className="protection-prototype" aria-labelledby="protection-prototype-title">
      <header className="protection-prototype-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="protection-prototype-title">{labels.title}</h1>
        </div>
        <span className="protection-prototype-badge">{labels.prototype}</span>
      </header>

      <div className="protection-prototype-grid">
        <aside className="protection-list-panel" aria-label={labels.deletedData}>
          <div className="protection-list-meta">
            <div><strong>{labels.deletedData}</strong><span>{labels.supplier}</span></div>
            <span>{deletedItems.length} {labels.records}</span>
          </div>
          <div className="protection-list-body">
            {deletedItems.map((item) => (
              <button
                key={item.id}
                type="button"
                className={`protection-list-item${selected?.id === item.id ? ' is-selected' : ''}`}
                onClick={() => setSelectedId(item.id)}
              >
                <span className="protection-list-name"><strong>{displayName(item)}</strong><small>{item.phone || labels.notProvided}</small></span>
                <span className="protection-deleted-pill">{labels.deletedData}</span>
              </button>
            ))}
            {deletedItems.length === 0 && <p className="protection-empty">{labels.empty}</p>}
          </div>
        </aside>

        <article className="protection-detail-panel">
          {selected !== null ? (
            <>
              <header className="protection-detail-header">
                <div>
                  <p className="eyebrow">{labels.supplier}</p>
                  <h2>{displayName(selected)}</h2>
                </div>
              </header>
              <dl className="protection-detail-fields">
                <DetailRow label={labels.phone} value={selected.phone || labels.notProvided} />
                <DetailRow label={labels.bankAccount} value={selected.bankAccount || labels.notProvided} />
                <DetailRow label={labels.deletedAt} value={formatDeletedAt(selected.deletedAt)} wide />
                <DetailRow label={labels.rowVersion} value={String(selected.rowVersion)} />
              </dl>
              <footer className="protection-detail-footer">
                <button className="protection-danger-action" type="button" onClick={() => setConfirmOpen(true)}>{labels.hardDelete}</button>
              </footer>
            </>
          ) : (
            <div className="protection-detail-empty">{labels.empty}</div>
          )}
        </article>
      </div>

      {confirmOpen && selected !== null && (
        <div className="protection-dialog-backdrop" role="presentation" onMouseDown={() => setConfirmOpen(false)}>
          <div className="protection-dialog" role="dialog" aria-modal="true" aria-labelledby="protection-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="protection-dialog-icon" aria-hidden="true">!</div>
            <div>
              <h2 id="protection-dialog-title">{labels.dialogTitle}</h2>
              <p>{displayName(selected)}</p>
              <p className="protection-dialog-warning">{labels.irreversible}</p>
            </div>
            <div className="protection-dialog-actions">
              <button className="protection-secondary-action" type="button" onClick={() => setConfirmOpen(false)}>{labels.cancel}</button>
              <button className="protection-danger-action" type="button" onClick={confirmHardDelete}>{labels.confirm}</button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}

function DetailRow({ label, value, wide = false }: { label: string; value: string; wide?: boolean }) {
  return <div className={`protection-detail-row${wide ? ' is-wide' : ''}`}><dt>{label}</dt><dd>{value}</dd></div>;
}
