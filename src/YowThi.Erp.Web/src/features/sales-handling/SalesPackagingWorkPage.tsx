import { useMutation, useQuery } from '@tanstack/react-query';
import { type FormEvent, useDeferredValue, useMemo, useRef, useState } from 'react';

import { LocaleControl } from '../../app/i18n/LocaleControl';
import { useOperationalLocale } from '../../app/i18n/locale';
import {
  ApiProblemError,
  recordSalesPackagingWork,
  type RecordSalesPackagingWorkRequest,
} from './recordSalesPackagingWork';
import {
  listSalesHandlingEmployeeOptions,
  listSalesHandlingPackagingItemOptions,
  listSalesHandlingSalesOptions,
} from './salesHandlingWorkOptions';

interface SubmissionIdentity {
  fingerprint: string;
  idempotencyKey: string;
}

const copy = {
  'zh-TW': {
    eyebrow: 'P7 · 銷售包裝作業',
    title: '登錄銷售包裝工作',
    intro: '此畫面直接使用正式 RecordSalesPackagingWork command。依 HANDLING-001，DRAFT 與 CONFIRMED Sale 都可以登錄；依 HANDLING-002，目前不假設同日、同人、同 Sale、同項目只能有一筆。',
    sale: '銷售單',
    saleSearch: '搜尋客戶名稱',
    saleSelect: '選擇 Sale',
    workDate: '工作日期',
    employee: '員工',
    employeeSearch: '搜尋員工名稱',
    employeeSelect: '選擇員工',
    item: '包裝 / Handling 項目',
    itemSearch: '搜尋項目名稱',
    itemSelect: '選擇項目',
    wage: '確認工資 THB',
    wageHint: 'v0.1 直接登錄已確認工資；此工作紀錄沒有 quantity / weight / hour 欄位。',
    loading: '載入中…',
    queryFailed: 'Sales Handling 選項查詢失敗',
    required: '請完整選擇 Sale、員工、項目並填寫工作日期。',
    wageInvalid: '確認工資必須是 0 或正整數 THB。',
    dailyWageClosed: '此員工該工作日的 Employee Daily Wage 已確認。依 LABOR-001 safe control，正常 late work 目前被阻擋。',
    employeeInactive: '所選員工目前不可用，請重新載入並選擇 active 員工。',
    itemInactive: '所選包裝項目目前不可用，請重新載入並選擇 active 項目。',
    saleUnavailable: '所選 Sale 已不存在或目前狀態不允許此操作。請重新載入。',
    idempotency: '此 Idempotency-Key 已被不同 request 使用。請重新開始一次登錄。',
    unexpected: '發生未預期錯誤。',
    submit: '登錄包裝工作',
    submitting: '登錄中…',
    success: '包裝工作已登錄',
    recordId: 'Work Record',
    rowVersion: 'Row version',
  },
  'th-TH': {
    eyebrow: 'P7 · งานบรรจุขาย',
    title: 'บันทึกงานบรรจุสำหรับการขาย',
    intro: 'หน้านี้ใช้ RecordSalesPackagingWork จริง ตาม HANDLING-001 สามารถบันทึกได้ทั้ง Sale สถานะ DRAFT และ CONFIRMED และตาม HANDLING-002 ยังไม่กำหนดว่าหนึ่งวัน/พนักงาน/Sale/รายการต้องมีได้เพียงหนึ่งรายการ',
    sale: 'การขาย',
    saleSearch: 'ค้นหาชื่อลูกค้า',
    saleSelect: 'เลือก Sale',
    workDate: 'วันที่ทำงาน',
    employee: 'พนักงาน',
    employeeSearch: 'ค้นหาชื่อพนักงาน',
    employeeSelect: 'เลือกพนักงาน',
    item: 'รายการบรรจุ / Handling',
    itemSearch: 'ค้นหาชื่อรายการ',
    itemSelect: 'เลือกรายการ',
    wage: 'ค่าจ้างที่ยืนยันแล้ว THB',
    wageHint: 'v0.1 บันทึกค่าจ้างที่ยืนยันแล้วโดยตรง และไม่มีช่อง quantity / weight / hour ใน work record นี้',
    loading: 'กำลังโหลด…',
    queryFailed: 'โหลดตัวเลือก Sales Handling ไม่สำเร็จ',
    required: 'โปรดเลือก Sale พนักงาน และรายการ พร้อมระบุวันที่ทำงานให้ครบ',
    wageInvalid: 'ค่าจ้างที่ยืนยันแล้วต้องเป็น 0 หรือจำนวนเต็มบวก THB',
    dailyWageClosed: 'Employee Daily Wage ของพนักงานในวันทำงานนี้ถูกยืนยันแล้ว ตาม LABOR-001 safe control งานย้อนหลังแบบปกติถูกบล็อกไว้',
    employeeInactive: 'พนักงานที่เลือกไม่พร้อมใช้งาน โปรดโหลดใหม่และเลือกพนักงาน active',
    itemInactive: 'รายการบรรจุที่เลือกไม่พร้อมใช้งาน โปรดโหลดใหม่และเลือกรายการ active',
    saleUnavailable: 'Sale ที่เลือกไม่มีอยู่หรือสถานะปัจจุบันไม่รองรับการทำงานนี้ โปรดโหลดใหม่',
    idempotency: 'Idempotency-Key นี้ถูกใช้กับ request อื่นแล้ว โปรดเริ่มการบันทึกใหม่',
    unexpected: 'เกิดข้อผิดพลาดที่ไม่คาดคิด',
    submit: 'บันทึกงานบรรจุ',
    submitting: 'กำลังบันทึก…',
    success: 'บันทึกงานบรรจุแล้ว',
    recordId: 'Work Record',
    rowVersion: 'Row version',
  },
} as const;

export function SalesPackagingWorkPage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const [saleSearch, setSaleSearch] = useState('');
  const [employeeSearch, setEmployeeSearch] = useState('');
  const [itemSearch, setItemSearch] = useState('');
  const [salesId, setSalesId] = useState('');
  const [employeeId, setEmployeeId] = useState('');
  const [itemId, setItemId] = useState('');
  const [localError, setLocalError] = useState<string | null>(null);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);

  const deferredSaleSearch = useDeferredValue(saleSearch);
  const deferredEmployeeSearch = useDeferredValue(employeeSearch);
  const deferredItemSearch = useDeferredValue(itemSearch);

  const salesQuery = useQuery({
    queryKey: ['sales-handling-work-options', 'sales', locale, deferredSaleSearch],
    queryFn: ({ signal }) =>
      listSalesHandlingSalesOptions({ locale, search: deferredSaleSearch, limit: 50, signal }),
    staleTime: 15_000,
  });

  const employeeQuery = useQuery({
    queryKey: ['sales-handling-work-options', 'employees', locale, deferredEmployeeSearch],
    queryFn: ({ signal }) =>
      listSalesHandlingEmployeeOptions({ locale, search: deferredEmployeeSearch, limit: 50, signal }),
    staleTime: 30_000,
  });

  const itemQuery = useQuery({
    queryKey: ['sales-handling-work-options', 'packaging-items', locale, deferredItemSearch],
    queryFn: ({ signal }) =>
      listSalesHandlingPackagingItemOptions({ locale, search: deferredItemSearch, limit: 50, signal }),
    staleTime: 30_000,
  });

  const mutation = useMutation({
    mutationFn: ({ request, idempotencyKey }: { request: RecordSalesPackagingWorkRequest; idempotencyKey: string }) =>
      recordSalesPackagingWork(request, { idempotencyKey, locale }),
  });

  const apiProblem = mutation.error instanceof ApiProblemError ? mutation.error : null;
  const problemMessage = useMemo(() => {
    if (apiProblem === null) {
      return mutation.error ? labels.unexpected : null;
    }

    switch (apiProblem.code) {
      case 'labor.daily-wage-already-confirmed':
        return labels.dailyWageClosed;
      case 'sales-handling.employee-not-found':
      case 'sales-handling.employee-inactive':
        return labels.employeeInactive;
      case 'sales-handling.item-not-found':
      case 'sales-handling.item-inactive':
        return labels.itemInactive;
      case 'sales-handling.sales-not-found':
      case 'sales-handling.sales-state-not-allowed':
        return labels.saleUnavailable;
      case 'idempotency.key-reused':
        return labels.idempotency;
      default:
        return `${apiProblem.code}${apiProblem.problem.traceId ? ` · ${apiProblem.problem.traceId}` : ''}`;
    }
  }, [apiProblem, labels, mutation.error]);

  const optionError = salesQuery.error ?? employeeQuery.error ?? itemQuery.error;
  const optionErrorMessage = optionError
    ? optionError instanceof ApiProblemError
      ? `${labels.queryFailed}: ${optionError.code}`
      : labels.queryFailed
    : null;

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    mutation.reset();
    setLocalError(null);

    const formData = new FormData(event.currentTarget);
    const workDate = formData.get('workDate');
    const rawWage = formData.get('confirmedWageThb');

    if (salesId === '' || employeeId === '' || itemId === '' || typeof workDate !== 'string' || workDate === '') {
      setLocalError(labels.required);
      return;
    }

    const confirmedWageThb = typeof rawWage === 'string' ? Number(rawWage) : Number.NaN;
    if (!Number.isSafeInteger(confirmedWageThb) || confirmedWageThb < 0) {
      setLocalError(labels.wageInvalid);
      return;
    }

    const request: RecordSalesPackagingWorkRequest = {
      salesId,
      workDate,
      employeeId,
      salesPackagingItemId: itemId,
      confirmedWageThb,
    };
    const fingerprint = JSON.stringify(request);
    if (submissionIdentity.current?.fingerprint !== fingerprint) {
      submissionIdentity.current = {
        fingerprint,
        idempotencyKey: crypto.randomUUID(),
      };
    }

    mutation.mutate({ request, idempotencyKey: submissionIdentity.current.idempotencyKey });
  }

  const submitDisabled =
    mutation.isPending
    || salesId === ''
    || employeeId === ''
    || itemId === ''
    || salesQuery.isError
    || employeeQuery.isError
    || itemQuery.isError;

  return (
    <section className="procurement-page" aria-labelledby="sales-packaging-work-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="sales-packaging-work-title">{labels.title}</h1>
        </div>
        <LocaleControl />
      </header>

      <div className="procurement-grid">
        <form className="entry-form" onSubmit={handleSubmit}>
          <div className="field-grid">
            <div className="option-picker full-width">
              <span className="field-label">{labels.sale}</span>
              <input
                value={saleSearch}
                onChange={(event) => setSaleSearch(event.target.value)}
              />
              <select
                value={salesId}
                onChange={(event) => {
                  setSalesId(event.target.value);
                  mutation.reset();
                  submissionIdentity.current = null;
                }}
                required
              >
                <option value="">{salesQuery.isPending ? labels.loading : labels.saleSelect}</option>
                {(salesQuery.data?.items ?? []).map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.salesDate} · {item.customerDisplayName} · {item.status}
                  </option>
                ))}
              </select>
            </div>

            <label>
              <span>{labels.workDate}</span>
              <input type="date" name="workDate" required />
            </label>

            <label>
              <span>{labels.wage}</span>
              <input type="number" name="confirmedWageThb" min="0" step="1" inputMode="numeric" required />
            </label>

            <div className="option-picker full-width">
              <span className="field-label">{labels.employee}</span>
              <input
                value={employeeSearch}
                onChange={(event) => setEmployeeSearch(event.target.value)}
              />
              <select
                value={employeeId}
                onChange={(event) => {
                  setEmployeeId(event.target.value);
                  mutation.reset();
                  submissionIdentity.current = null;
                }}
                required
              >
                <option value="">{employeeQuery.isPending ? labels.loading : labels.employeeSelect}</option>
                {(employeeQuery.data?.items ?? []).map((item) => (
                  <option key={item.id} value={item.id}>{item.displayName}</option>
                ))}
              </select>
            </div>

            <div className="option-picker full-width">
              <span className="field-label">{labels.item}</span>
              <input
                value={itemSearch}
                onChange={(event) => setItemSearch(event.target.value)}
              />
              <select
                value={itemId}
                onChange={(event) => {
                  setItemId(event.target.value);
                  mutation.reset();
                  submissionIdentity.current = null;
                }}
                required
              >
                <option value="">{itemQuery.isPending ? labels.loading : labels.itemSelect}</option>
                {(itemQuery.data?.items ?? []).map((item) => (
                  <option key={item.id} value={item.id}>{item.displayName}</option>
                ))}
              </select>
            </div>
          </div>


          {(localError ?? problemMessage ?? optionErrorMessage) && (
            <div className="problem-banner" role="alert">
              {localError ?? problemMessage ?? optionErrorMessage}
            </div>
          )}

          <button className="primary-action" type="submit" disabled={submitDisabled}>
            {mutation.isPending ? labels.submitting : labels.submit}
          </button>
        </form>

        <aside className="result-panel" aria-live="polite">
          {mutation.data ? (
            <>
              <p className="eyebrow">{labels.success}</p>
              <dl>
                <ResultRow label={labels.recordId} value={mutation.data.salesPackagingWorkRecordId} />
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

function ResultRow({ label, value }: { label: string; value: string }) {
  return <div className="result-row"><dt>{label}</dt><dd>{value}</dd></div>;
}
