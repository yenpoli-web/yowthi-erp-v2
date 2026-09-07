import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useRef, useState } from 'react';

import { useOperationalLocale } from '../../app/i18n/locale';
import { ApiProblemError, hardDeleteTarget } from './hardDelete';
import { listSuppliers, type SupplierMasterItem } from '../party/supplierMaster';
import './dataProtectionPrototype.css';

type SubmissionIdentity = { fingerprint: string; idempotencyKey: string };

const copy = {
  'zh-TW': {
    eyebrow: '資料保護', title: '硬刪除', supplier: '供應商', deletedData: '已軟刪除資料', records: '筆資料', phone: '電話', deletedAt: '刪除時間', rowVersion: '資料版本', bankAccount: '銀行帳號', hardDelete: '永久刪除', dialogTitle: '永久刪除供應商', irreversible: '此操作會永久移除資料，無法恢復。', cancel: '取消', confirm: '確認永久刪除', empty: '目前沒有已軟刪除的供應商。', notProvided: '—', queryFailed: '無法載入已刪除資料', dependencyBlocked: '這筆資料仍有其他資料相依，不能永久刪除。', stale: '資料版本已變更，請重新整理後再試。', notFound: '資料不存在。', unexpected: '永久刪除失敗，請重新整理後再試。',
  },
  'th-TH': {
    eyebrow: 'การคุ้มครองข้อมูล', title: 'ลบถาวร', supplier: 'ผู้จำหน่าย', deletedData: 'ข้อมูลที่ลบแบบเก็บประวัติแล้ว', records: 'รายการ', phone: 'โทรศัพท์', deletedAt: 'เวลาที่ลบ', rowVersion: 'เวอร์ชันข้อมูล', bankAccount: 'เลขบัญชีธนาคาร', hardDelete: 'ลบถาวร', dialogTitle: 'ลบผู้จำหน่ายถาวร', irreversible: 'การดำเนินการนี้จะลบข้อมูลอย่างถาวรและไม่สามารถกู้คืนได้', cancel: 'ยกเลิก', confirm: 'ยืนยันการลบถาวร', empty: 'ขณะนี้ไม่มีผู้จำหน่ายที่ถูกลบแบบเก็บประวัติ', notProvided: '—', queryFailed: 'ไม่สามารถโหลดข้อมูลที่ลบแล้วได้', dependencyBlocked: 'ข้อมูลนี้ยังมีรายการอื่นอ้างอิงอยู่ จึงไม่สามารถลบถาวรได้', stale: 'เวอร์ชันข้อมูลเปลี่ยนแล้ว กรุณารีเฟรชแล้วลองใหม่', notFound: 'ไม่พบข้อมูล', unexpected: 'ลบถาวรไม่สำเร็จ กรุณารีเฟรชแล้วลองใหม่',
  },
} as const;

export function DataProtectionPage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState('');
  const [confirmOpen, setConfirmOpen] = useState(false);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);

  const suppliersQuery = useQuery({
    queryKey: ['supplier-master', locale, '', 'deleted'],
    queryFn: ({ signal }) => listSuppliers({ locale, status: 'deleted', limit: 200, signal }),
    staleTime: 2_000,
  });
  const deletedItems = suppliersQuery.data?.items ?? [];
  const selected = deletedItems.find((item) => item.id === selectedId) ?? deletedItems[0] ?? null;

  const mutation = useMutation({
    mutationFn: (input: { item: SupplierMasterItem; idempotencyKey: string }) => hardDeleteTarget(
      'suppliers',
      input.item.id,
      { expectedRowVersion: input.item.rowVersion },
      { idempotencyKey: input.idempotencyKey, locale },
    ),
    onSuccess: async () => {
      setSelectedId(''); setConfirmOpen(false); submissionIdentity.current = null;
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['supplier-master'] }),
        queryClient.invalidateQueries({ queryKey: ['hard-delete-options', 'suppliers'] }),
        queryClient.invalidateQueries({ queryKey: ['party-lifecycle-options', 'suppliers'] }),
      ]);
    },
  });

  const problemMessage = useMemo(() => {
    const error = mutation.error ?? suppliersQuery.error;
    if (!error) return null;
    if (!(error instanceof ApiProblemError)) return suppliersQuery.error ? labels.queryFailed : labels.unexpected;
    if (error.code.endsWith('-dependency-blocked')) return labels.dependencyBlocked;
    if (error.code === 'concurrency.stale-row-version') return labels.stale;
    if (error.code.endsWith('-not-found')) return labels.notFound;
    return `${error.code}${error.problem.traceId ? ` · ${error.problem.traceId}` : ''}`;
  }, [labels, mutation.error, suppliersQuery.error]);

  const displayName = (item: SupplierMasterItem) => {
    const primary = locale === 'zh-TW' ? item.nameZhTw : item.nameThTh;
    const fallback = locale === 'zh-TW' ? item.nameThTh : item.nameZhTw;
    return primary?.trim() || fallback?.trim() || labels.notProvided;
  };
  function formatDeletedAt(value: string | null) {
    if (!value) return labels.notProvided;
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
  }
  function confirmHardDelete() {
    if (!selected) return;
    const fingerprint = JSON.stringify({ id: selected.id, rowVersion: selected.rowVersion });
    if (submissionIdentity.current?.fingerprint !== fingerprint) submissionIdentity.current = { fingerprint, idempotencyKey: crypto.randomUUID() };
    mutation.mutate({ item: selected, idempotencyKey: submissionIdentity.current.idempotencyKey });
  }

  return (
    <section className="protection-prototype" aria-labelledby="protection-title">
      <header className="protection-prototype-header"><div><p className="eyebrow">{labels.eyebrow}</p><h1 id="protection-title">{labels.title}</h1></div></header>
      <div className="protection-prototype-grid">
        <aside className="protection-list-panel" aria-label={labels.deletedData}>
          <div className="protection-list-meta"><div><strong>{labels.deletedData}</strong><span>{labels.supplier}</span></div><span>{deletedItems.length} {labels.records}</span></div>
          <div className="protection-list-body">
            {deletedItems.map((item) => <button key={item.id} type="button" className={`protection-list-item${selected?.id === item.id ? ' is-selected' : ''}`} onClick={() => { setSelectedId(item.id); submissionIdentity.current = null; }}><span className="protection-list-name"><strong>{displayName(item)}</strong><small>{item.phone || labels.notProvided}</small></span><span className="protection-deleted-pill">{labels.deletedData}</span></button>)}
            {!suppliersQuery.isPending && deletedItems.length === 0 && <p className="protection-empty">{problemMessage ?? labels.empty}</p>}
          </div>
        </aside>
        <article className="protection-detail-panel">
          {selected ? <><header className="protection-detail-header"><div><p className="eyebrow">{labels.supplier}</p><h2>{displayName(selected)}</h2></div></header><dl className="protection-detail-fields"><DetailRow label={labels.phone} value={selected.phone || labels.notProvided} /><DetailRow label={labels.bankAccount} value={selected.bankAccount || labels.notProvided} /><DetailRow label={labels.deletedAt} value={formatDeletedAt(selected.deletedAt)} wide /><DetailRow label={labels.rowVersion} value={String(selected.rowVersion)} /></dl>{problemMessage && <div className="problem-banner" role="alert">{problemMessage}</div>}<footer className="protection-detail-footer"><button className="protection-danger-action" type="button" disabled={mutation.isPending} onClick={() => setConfirmOpen(true)}>{labels.hardDelete}</button></footer></> : <div className="protection-detail-empty">{suppliersQuery.isPending ? '…' : problemMessage ?? labels.empty}</div>}
        </article>
      </div>
      {confirmOpen && selected && <div className="protection-dialog-backdrop" role="presentation" onMouseDown={() => setConfirmOpen(false)}><div className="protection-dialog" role="dialog" aria-modal="true" aria-labelledby="protection-dialog-title" onMouseDown={(event) => event.stopPropagation()}><div className="protection-dialog-icon" aria-hidden="true">!</div><div><h2 id="protection-dialog-title">{labels.dialogTitle}</h2><p>{displayName(selected)}</p><p className="protection-dialog-warning">{labels.irreversible}</p></div><div className="protection-dialog-actions"><button className="protection-secondary-action" type="button" onClick={() => setConfirmOpen(false)}>{labels.cancel}</button><button className="protection-danger-action" type="button" disabled={mutation.isPending} onClick={confirmHardDelete}>{labels.confirm}</button></div></div></div>}
    </section>
  );
}

function DetailRow({ label, value, wide = false }: { label: string; value: string; wide?: boolean }) {
  return <div className={`protection-detail-row${wide ? ' is-wide' : ''}`}><dt>{label}</dt><dd>{value}</dd></div>;
}
