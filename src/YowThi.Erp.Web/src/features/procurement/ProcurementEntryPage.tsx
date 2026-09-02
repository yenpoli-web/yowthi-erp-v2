import { useMutation } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState } from 'react';

import {
  ApiProblemError,
  confirmProcurementEntry,
  type ConfirmProcurementEntryRequest,
  type OperationalLocale,
  type ProcurementSourceType,
} from './confirmProcurementEntry';

interface Submission {
  request: ConfirmProcurementEntryRequest;
  idempotencyKey: string;
  locale: OperationalLocale;
}

interface SubmissionIdentity {
  fingerprint: string;
  idempotencyKey: string;
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

const copy = {
  'zh-TW': {
    eyebrow: 'P6 · 採購',
    title: '確認採購登錄',
    intro: '此畫面直接呼叫正式 ConfirmProcurementEntry command。現階段尚未建立 Master lookup query，因此產品、來源與儲位以 UUID 輸入；不使用 mock 資料。',
    locale: '介面語言',
    date: '採購日期',
    product: '採購產品 UUID',
    sourceType: '來源類型',
    supplier: '供應商 UUID',
    farmer: '農民 UUID',
    quantity: '淨數量',
    unitPrice: '單價',
    companyPickup: '公司取貨',
    location: '收貨儲位 UUID（可留空）',
    locationHint: '留空時由系統使用可解析的唯一預設；無法解析時會要求明確選擇。',
    submit: '確認採購登錄',
    submitting: '確認中…',
    validation: '請檢查輸入欄位。',
    uuid: 'UUID 格式不正確。',
    quantityPositive: 'P6 目前依 PROC-003 safe control 要求淨數量大於 0。',
    priceNonNegative: '單價不可小於 0。',
    sourceRequired: '請提供與來源類型相符的來源 UUID。',
    locationRequired: '目前無法解析唯一收貨儲位，請填入明確的收貨儲位 UUID 後重新確認。',
    batchCompleted: '此採購批次已 COMPLETED；依 PROC-001，正常 late entry 目前被阻擋。',
    idempotencyConflict: '此 Idempotency-Key 已被不同 command request 使用。請修改輸入或重新開始一次確認。',
    success: '採購登錄已確認',
    entryId: 'Procurement Entry',
    batchId: 'Procurement Batch',
    payableId: 'Payable',
    amount: '金額 THB',
    receiptLocation: '收貨儲位',
    transportBasis: 'Company Pickup transport basis',
    noTransportBasis: '未建立',
    technicalNote: 'UUID 欄位是目前 V1-C5 的技術輸入方式；正式 Master selector 需要新增 read/query contract，不會在前端自行推導或硬編業務資料。',
    unexpected: '發生未預期錯誤。',
  },
  'th-TH': {
    eyebrow: 'P6 · จัดซื้อ',
    title: 'ยืนยันรายการจัดซื้อ',
    intro: 'หน้านี้เรียก ConfirmProcurementEntry จริงโดยตรง ขณะนี้ยังไม่มี Master lookup query จึงกรอก UUID ของสินค้า แหล่งที่มา และตำแหน่งจัดเก็บโดยไม่ใช้ mock data',
    locale: 'ภาษา',
    date: 'วันที่จัดซื้อ',
    product: 'UUID สินค้าจัดซื้อ',
    sourceType: 'ประเภทแหล่งที่มา',
    supplier: 'UUID Supplier',
    farmer: 'UUID Farmer',
    quantity: 'ปริมาณสุทธิ',
    unitPrice: 'ราคาต่อหน่วย',
    companyPickup: 'บริษัทไปรับสินค้า',
    location: 'UUID ตำแหน่งรับสินค้า (ไม่บังคับ)',
    locationHint: 'หากเว้นว่าง ระบบจะใช้ค่าเริ่มต้นที่ระบุได้อย่างชัดเจน หากระบุไม่ได้ต้องเลือกตำแหน่งเอง',
    submit: 'ยืนยันรายการจัดซื้อ',
    submitting: 'กำลังยืนยัน…',
    validation: 'โปรดตรวจสอบข้อมูลที่กรอก',
    uuid: 'รูปแบบ UUID ไม่ถูกต้อง',
    quantityPositive: 'P6 ใช้ PROC-003 safe control ชั่วคราว โดยปริมาณสุทธิต้องมากกว่า 0',
    priceNonNegative: 'ราคาต่อหน่วยต้องไม่ติดลบ',
    sourceRequired: 'โปรดกรอก UUID ให้ตรงกับประเภทแหล่งที่มา',
    locationRequired: 'ระบบไม่สามารถระบุตำแหน่งรับสินค้าเพียงหนึ่งตำแหน่งได้ โปรดกรอก UUID ตำแหน่งรับสินค้าแล้วลองใหม่',
    batchCompleted: 'Procurement Batch นี้ COMPLETED แล้ว ตาม PROC-001 การเพิ่มรายการปกติภายหลังถูกบล็อกไว้ในขณะนี้',
    idempotencyConflict: 'Idempotency-Key นี้ถูกใช้กับ command request อื่นแล้ว โปรดแก้ข้อมูลหรือเริ่มการยืนยันใหม่',
    success: 'ยืนยันรายการจัดซื้อแล้ว',
    entryId: 'Procurement Entry',
    batchId: 'Procurement Batch',
    payableId: 'Payable',
    amount: 'จำนวนเงิน THB',
    receiptLocation: 'ตำแหน่งรับสินค้า',
    transportBasis: 'Company Pickup transport basis',
    noTransportBasis: 'ไม่ได้สร้าง',
    technicalNote: 'ช่อง UUID เป็นวิธีกรอกทางเทคนิคของ V1-C5 ขณะนี้ Master selector ต้องมี read/query contract จริง และจะไม่สร้างหรือเดาข้อมูลธุรกิจใน frontend',
    unexpected: 'เกิดข้อผิดพลาดที่ไม่คาดคิด',
  },
} as const;

export function ProcurementEntryPage() {
  const [locale, setLocale] = useState<OperationalLocale>(resolveInitialLocale);
  const [sourceType, setSourceType] = useState<ProcurementSourceType>('SUPPLIER');
  const [localError, setLocalError] = useState<string | null>(null);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);
  const labels = copy[locale];

  useEffect(() => {
    document.documentElement.lang = locale;
  }, [locale]);

  const mutation = useMutation({
    mutationFn: ({ request, idempotencyKey, locale: requestLocale }: Submission) =>
      confirmProcurementEntry(request, {
        idempotencyKey,
        locale: requestLocale,
      }),
  });

  const apiProblem = mutation.error instanceof ApiProblemError ? mutation.error : null;
  const problemMessage = useMemo(() => {
    if (apiProblem === null) {
      return mutation.error ? labels.unexpected : null;
    }

    switch (apiProblem.code) {
      case 'procurement.receipt-location-required':
        return labels.locationRequired;
      case 'procurement.batch-completed':
        return labels.batchCompleted;
      case 'procurement.net-quantity-zero-unverified':
        return labels.quantityPositive;
      case 'idempotency.key-reused':
        return labels.idempotencyConflict;
      default:
        return `${apiProblem.code}${apiProblem.problem.traceId ? ` · ${apiProblem.problem.traceId}` : ''}`;
    }
  }, [apiProblem, labels, mutation.error]);

  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLocalError(null);
    mutation.reset();

    const formData = new FormData(event.currentTarget);

    try {
      const procurementProductId = requiredUuid(formData, 'procurementProductId', labels.uuid);
      const sourceId = requiredUuid(formData, 'sourceId', labels.uuid);
      const receiptStorageLocationId = optionalUuid(
        formData,
        'receiptStorageLocationId',
        labels.uuid,
      );
      const netQuantity = requiredNumber(formData, 'netQuantity');
      const unitPrice = requiredNumber(formData, 'unitPrice');

      if (netQuantity <= 0) {
        throw new Error(labels.quantityPositive);
      }

      if (unitPrice < 0) {
        throw new Error(labels.priceNonNegative);
      }

      const request: ConfirmProcurementEntryRequest = {
        procurementDate: requiredText(formData, 'procurementDate'),
        procurementProductId,
        sourceType,
        supplierId: sourceType === 'SUPPLIER' ? sourceId : null,
        farmerId: sourceType === 'FARMER' ? sourceId : null,
        netQuantity,
        unitPrice,
        companyPickup: formData.get('companyPickup') === 'on',
        receiptStorageLocationId,
      };

      const fingerprint = JSON.stringify(request);
      if (submissionIdentity.current?.fingerprint !== fingerprint) {
        submissionIdentity.current = {
          fingerprint,
          idempotencyKey: crypto.randomUUID(),
        };
      }

      mutation.mutate({
        request,
        idempotencyKey: submissionIdentity.current.idempotencyKey,
        locale,
      });
    } catch (error) {
      setLocalError(error instanceof Error ? error.message : labels.validation);
    }
  }

  return (
    <section className="procurement-page" aria-labelledby="procurement-entry-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="procurement-entry-title">{labels.title}</h1>
          <p className="page-intro">{labels.intro}</p>
        </div>

        <label className="locale-control">
          <span>{labels.locale}</span>
          <select value={locale} onChange={(event) => setLocale(event.target.value as OperationalLocale)}>
            <option value="zh-TW">繁體中文</option>
            <option value="th-TH">ไทย</option>
          </select>
        </label>
      </header>

      <div className="procurement-grid">
        <form className="entry-form" onSubmit={handleSubmit}>
          <div className="field-grid">
            <label>
              <span>{labels.date}</span>
              <input type="date" name="procurementDate" required />
            </label>

            <label>
              <span>{labels.product}</span>
              <input name="procurementProductId" inputMode="text" autoComplete="off" required />
            </label>

            <label>
              <span>{labels.sourceType}</span>
              <select
                name="sourceType"
                value={sourceType}
                onChange={(event) => setSourceType(event.target.value as ProcurementSourceType)}
              >
                <option value="SUPPLIER">SUPPLIER</option>
                <option value="FARMER">FARMER</option>
              </select>
            </label>

            <label>
              <span>{sourceType === 'SUPPLIER' ? labels.supplier : labels.farmer}</span>
              <input name="sourceId" inputMode="text" autoComplete="off" required />
            </label>

            <label>
              <span>{labels.quantity}</span>
              <input type="number" name="netQuantity" min="0" step="any" inputMode="decimal" required />
            </label>

            <label>
              <span>{labels.unitPrice}</span>
              <input type="number" name="unitPrice" min="0" step="any" inputMode="decimal" required />
            </label>

            <label className="full-width">
              <span>{labels.location}</span>
              <input name="receiptStorageLocationId" inputMode="text" autoComplete="off" />
              <small>{labels.locationHint}</small>
            </label>
          </div>

          <label className="checkbox-field">
            <input type="checkbox" name="companyPickup" />
            <span>{labels.companyPickup}</span>
          </label>

          {(localError ?? problemMessage) && (
            <div className="problem-banner" role="alert">
              {localError ?? problemMessage}
            </div>
          )}

          <button className="primary-action" type="submit" disabled={mutation.isPending}>
            {mutation.isPending ? labels.submitting : labels.submit}
          </button>

          <p className="technical-note">{labels.technicalNote}</p>
        </form>

        <aside className="result-panel" aria-live="polite">
          {mutation.data ? (
            <>
              <p className="eyebrow">{labels.success}</p>
              <dl>
                <ResultRow label={labels.entryId} value={mutation.data.procurementEntryId} />
                <ResultRow label={labels.batchId} value={mutation.data.procurementBatchId} />
                <ResultRow label={labels.payableId} value={mutation.data.payableId} />
                <ResultRow
                  label={labels.amount}
                  value={new Intl.NumberFormat(locale).format(mutation.data.amountThb)}
                />
                <ResultRow label={labels.receiptLocation} value={mutation.data.receiptStorageLocationId} />
                <ResultRow
                  label={labels.transportBasis}
                  value={mutation.data.companyPickupTransportBasisId ?? labels.noTransportBasis}
                />
              </dl>
            </>
          ) : (
            <div className="result-placeholder" aria-hidden="true">
              <span>YowThi ERP V2</span>
            </div>
          )}
        </aside>
      </div>
    </section>
  );
}

function ResultRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="result-row">
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function resolveInitialLocale(): OperationalLocale {
  const configured = normalizeLocale(import.meta.env.VITE_DEFAULT_LOCALE);
  if (configured !== null) {
    return configured;
  }

  for (const candidate of navigator.languages) {
    const normalized = normalizeLocale(candidate);
    if (normalized !== null) {
      return normalized;
    }
  }

  return 'zh-TW';
}

function normalizeLocale(value: string | undefined): OperationalLocale | null {
  if (value?.toLowerCase() === 'zh-tw') {
    return 'zh-TW';
  }

  if (value?.toLowerCase() === 'th-th') {
    return 'th-TH';
  }

  return null;
}

function requiredText(formData: FormData, name: string): string {
  const value = formData.get(name);
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error(`${name} is required.`);
  }

  return value.trim();
}

function requiredUuid(formData: FormData, name: string, errorMessage: string): string {
  const value = requiredText(formData, name);
  if (!uuidPattern.test(value)) {
    throw new Error(errorMessage);
  }

  return value;
}

function optionalUuid(formData: FormData, name: string, errorMessage: string): string | null {
  const value = formData.get(name);
  if (typeof value !== 'string' || value.trim() === '') {
    return null;
  }

  const trimmed = value.trim();
  if (!uuidPattern.test(trimmed)) {
    throw new Error(errorMessage);
  }

  return trimmed;
}

function requiredNumber(formData: FormData, name: string): number {
  const raw = requiredText(formData, name);
  const value = Number(raw);
  if (!Number.isFinite(value)) {
    throw new Error(`${name} must be a finite number.`);
  }

  return value;
}
