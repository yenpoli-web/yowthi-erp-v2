import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useRef, useState } from 'react';

import { useOperationalLocale } from '../../app/i18n/locale';
import { ApiProblemError, confirmSales, type ConfirmSalesRequest } from './confirmSales';
import {
  getSalesConfirmationWorkspace,
  listSalesConfirmationOptions,
} from './salesConfirmationOptions';

interface SubmissionIdentity {
  fingerprint: string;
  idempotencyKey: string;
}

const copy = {
  'zh-TW': {
    eyebrow: 'P7 · 銷售',
    title: '確認銷售',
    intro: '此畫面確認既有 DRAFT Sale，直接使用正式 ConfirmSales command。DRAFT Sale 建立目前不在既有 command/API contract 內，因此此處不自行新增建立銷售單的 Business Rule。',
    search: '搜尋客戶名稱',
    select: '選擇待確認銷售',
    loading: '載入中…',
    salesId: '銷售',
    weightBased: '依重量計價',
    unitBased: '依件數計價',
    customer: '客戶',
    date: '銷售日期',
    rowVersion: '資料版本',
    details: '銷售明細',
    quantity: '數量',
    pricing: '計價基礎',
    weight: '銷售重量',
    unitPrice: '單價',
    amount: '金額（泰銖）',
    autoAllocation: '本階段提交空的 manual allocation override 集合，使用既有 OUTSOURCED oldest→newest、再 IN_HOUSE oldest→newest 自動分配。',
    sales001: '此來源批次的可售庫存橫跨多個儲位。依 SALES-001，目前必須阻擋確認；尚未建立儲位指定機制。',
    insufficient: '可售庫存不足，無法完成目前銷售的自動分配。',
    stale: '銷售資料已被其他操作更新。請重新載入目前草稿銷售後再確認。',
    idempotency: '此操作識別碼已被不同請求使用。請重新開始一次確認。',
    queryFailed: '銷售確認資料查詢失敗',
    unexpected: '發生未預期錯誤。',
    submit: '確認銷售',
    submitting: '確認中…',
    success: '銷售已確認',
    receivable: '應收款',
    allocationRevision: '分配修訂',
    inventoryOperation: '庫存操作',
    noDraft: '目前沒有可確認的草稿銷售。',
  },
  'th-TH': {
    eyebrow: 'P7 · การขาย',
    title: 'ยืนยันการขาย',
    intro: 'หน้านี้ยืนยัน Sale สถานะ DRAFT ที่มีอยู่แล้วและใช้ ConfirmSales จริง การสร้าง DRAFT Sale ยังไม่มี command/API contract ปัจจุบัน จึงไม่สร้าง Business Rule เพิ่มเองในหน้านี้',
    search: 'ค้นหาชื่อลูกค้า',
    select: 'เลือกการขายที่รอยืนยัน',
    loading: 'กำลังโหลด…',
    salesId: 'การขาย',
    weightBased: 'คิดราคาตามน้ำหนัก',
    unitBased: 'คิดราคาต่อหน่วย',
    customer: 'ลูกค้า',
    date: 'วันที่ขาย',
    rowVersion: 'รุ่นข้อมูล',
    details: 'รายละเอียดการขาย',
    quantity: 'ปริมาณ',
    pricing: 'เกณฑ์ราคา',
    weight: 'น้ำหนักขาย',
    unitPrice: 'ราคาต่อหน่วย',
    amount: 'จำนวนเงิน (บาท)',
    autoAllocation: 'ขั้นนี้ส่ง manual allocation override เป็นชุดว่าง และใช้ลำดับอัตโนมัติ OUTSOURCED เก่าสุด→ใหม่สุด แล้ว IN_HOUSE เก่าสุด→ใหม่สุดตาม contract เดิม',
    sales001: 'สินค้าคงคลังที่ขายได้ของล็อตต้นทางนี้อยู่หลายตำแหน่ง ตาม SALES-001 ต้องหยุดการยืนยัน และยังไม่มีกลไกระบุตำแหน่งจัดเก็บ',
    insufficient: 'สินค้าคงคลังที่ขายได้ไม่เพียงพอสำหรับการจัดสรรอัตโนมัติ',
    stale: 'ข้อมูลการขายถูกแก้ไขโดยการทำงานอื่น โปรดโหลดการขายแบบร่างปัจจุบันใหม่ก่อนยืนยัน',
    idempotency: 'รหัสการทำงานนี้ถูกใช้กับคำขออื่น โปรดเริ่มการยืนยันใหม่',
    queryFailed: 'โหลดข้อมูลยืนยันการขายไม่สำเร็จ',
    unexpected: 'เกิดข้อผิดพลาดที่ไม่คาดคิด',
    submit: 'ยืนยันการขาย',
    submitting: 'กำลังยืนยัน…',
    success: 'ยืนยันการขายแล้ว',
    receivable: 'ลูกหนี้',
    allocationRevision: 'รุ่นการจัดสรร',
    inventoryOperation: 'รายการสินค้าคงคลัง',
    noDraft: 'ไม่มีการขายแบบร่างที่พร้อมยืนยันในขณะนี้',
  },
} as const;

export function SalesConfirmationPage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [salesId, setSalesId] = useState('');
  const deferredSearch = useDeferredValue(search);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);

  const salesQuery = useQuery({
    queryKey: ['sales-confirmation-options', locale, deferredSearch],
    queryFn: ({ signal }) => listSalesConfirmationOptions({ locale, search: deferredSearch, limit: 50, signal }),
    staleTime: 15_000,
  });

  const workspaceQuery = useQuery({
    queryKey: ['sales-confirmation-workspace', salesId, locale],
    queryFn: ({ signal }) => getSalesConfirmationWorkspace(salesId, locale, signal),
    enabled: salesId !== '',
    staleTime: 5_000,
  });

  const mutation = useMutation({
    mutationFn: ({ request, idempotencyKey }: { request: ConfirmSalesRequest; idempotencyKey: string }) =>
      confirmSales(salesId, request, { idempotencyKey, locale }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['sales-confirmation-options'] });
    },
  });

  const apiProblem = mutation.error instanceof ApiProblemError ? mutation.error : null;
  const problemMessage = useMemo(() => {
    if (apiProblem === null) return mutation.error ? labels.unexpected : null;
    switch (apiProblem.code) {
      case 'sales.issue-location-required': return labels.sales001;
      case 'inventory.insufficient-stock': return labels.insufficient;
      case 'concurrency.stale-row-version': return labels.stale;
      case 'idempotency.key-reused': return labels.idempotency;
      default: return `${apiProblem.code}${apiProblem.problem.traceId ? ` · ${apiProblem.problem.traceId}` : ''}`;
    }
  }, [apiProblem, labels, mutation.error]);

  const queryError = salesQuery.error ?? workspaceQuery.error;
  const queryErrorMessage = queryError
    ? queryError instanceof ApiProblemError
      ? `${labels.queryFailed}: ${queryError.code}`
      : labels.queryFailed
    : null;

  const workspace = workspaceQuery.data ?? null;

  function handleConfirm() {
    if (workspace === null) return;
    mutation.reset();
    const request: ConfirmSalesRequest = {
      expectedRowVersion: workspace.rowVersion,
      manualAllocationOverrides: [],
    };
    const fingerprint = JSON.stringify({ salesId: workspace.salesId, request });
    if (submissionIdentity.current?.fingerprint !== fingerprint) {
      submissionIdentity.current = { fingerprint, idempotencyKey: crypto.randomUUID() };
    }
    mutation.mutate({ request, idempotencyKey: submissionIdentity.current.idempotencyKey });
  }

  return (
    <section className="procurement-page" aria-labelledby="sales-confirmation-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="sales-confirmation-title">{labels.title}</h1>
        </div>
      </header>

      <div className="procurement-grid">
        <div className="entry-form">
          <div className="option-picker">
            <span className="field-label">{labels.select}</span>
            <input value={search} onChange={(event) => setSearch(event.target.value)} />
            <select value={salesId} onChange={(event) => { setSalesId(event.target.value); mutation.reset(); submissionIdentity.current = null; }}>
              <option value="">{salesQuery.isPending ? labels.loading : labels.select}</option>
              {(salesQuery.data?.items ?? []).map((item) => (
                <option key={item.id} value={item.id}>{item.salesDate} · {item.customerDisplayName}</option>
              ))}
            </select>
            {salesQuery.isSuccess && salesQuery.data.items.length === 0 && <small>{labels.noDraft}</small>}
          </div>

          {workspaceQuery.isPending && salesId !== '' && <p>{labels.loading}</p>}
          {workspace && (
            <>
              <dl>
                <ResultRow label={labels.customer} value={workspace.customerDisplayName} />
                <ResultRow label={labels.date} value={workspace.salesDate} />
                <ResultRow label={labels.rowVersion} value={String(workspace.rowVersion)} />
              </dl>
              <h2>{labels.details}</h2>
              <dl>
                {workspace.details.map((detail) => (
                  <div className="result-row" key={detail.id}>
                    <dt>#{detail.lineNumber} · {detail.productDisplayName}</dt>
                    <dd>
                      {labels.quantity}: {detail.quantity} · {labels.pricing}: {pricingBasisLabel(detail.pricingBasis, labels)} · {labels.weight}: {detail.salesWeight ?? '—'} · {labels.unitPrice}: {detail.unitPrice} · {labels.amount}: {new Intl.NumberFormat(locale).format(detail.amountThb)}
                    </dd>
                  </div>
                ))}
              </dl>
              {(problemMessage ?? queryErrorMessage) && <div className="problem-banner" role="alert">{problemMessage ?? queryErrorMessage}</div>}
              <button className="primary-action" type="button" onClick={handleConfirm} disabled={mutation.isPending || workspace.details.length === 0}>
                {mutation.isPending ? labels.submitting : labels.submit}
              </button>
            </>
          )}
          {!workspace && queryErrorMessage && <div className="problem-banner" role="alert">{queryErrorMessage}</div>}
        </div>

        <aside className="result-panel" aria-live="polite">
          {mutation.data ? (
            <>
              <p className="eyebrow">{labels.success}</p>
              <dl>
                <ResultRow label={labels.salesId} value={mutation.data.salesId} />
                <ResultRow label={labels.receivable} value={mutation.data.receivableId} />
                <ResultRow label={labels.allocationRevision} value={mutation.data.allocationRevisionId} />
                <ResultRow label={labels.inventoryOperation} value={mutation.data.inventoryOperationId} />
                <ResultRow label={labels.rowVersion} value={String(mutation.data.rowVersion)} />
              </dl>
            </>
          ) : (
            <div className="result-placeholder" aria-hidden="true"><span>YowThi ERP V2</span></div>
          )}
        </aside>
      </div>
    </section>
  );
}

function pricingBasisLabel(basis: string, labels: typeof copy['zh-TW'] | typeof copy['th-TH']): string {
  if (basis === 'WEIGHT_BASED_UNIT') return labels.weightBased;
  if (basis === 'UNIT_BASED') return labels.unitBased;
  return basis;
}

function ResultRow({ label, value }: { label: string; value: string }) {
  return <div className="result-row"><dt>{label}</dt><dd>{value}</dd></div>;
}
