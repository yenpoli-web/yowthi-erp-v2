import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useRef, useState } from 'react';

import { useOperationalLocale } from '../../app/i18n/locale';
import {
  listFinancePayableOptions,
  listFinanceReceivableOptions,
  type FinancePayableSettlementOption,
  type FinanceReceivableSettlementOption,
} from './financeSettlementOptions';
import {
  ApiProblemError,
  payPayable,
  receiveReceivable,
  type PayPayableRequest,
  type ReceiveReceivableRequest,
} from './financeSettlements';

type SettlementMode = 'payable' | 'receivable';

type SettlementMutationInput =
  | { mode: 'payable'; targetId: string; request: PayPayableRequest; idempotencyKey: string }
  | { mode: 'receivable'; targetId: string; request: ReceiveReceivableRequest; idempotencyKey: string };

interface SettlementMutationResult {
  mode: SettlementMode;
  transactionId: string;
  amountThb: number;
  outstandingThb: number;
  outstandingVersion: number;
  confirmedAt: string;
}

interface SubmissionIdentity {
  fingerprint: string;
  idempotencyKey: string;
}

const copy = {
  'zh-TW': {
    eyebrow: 'P7 · 財務',
    title: '應付 / 應收結算登記',
    intro: '此畫面用於應付款與應收款結算登記。金額留空代表依目前未結金額全額結算，填入金額則登記部分付款或收款。',
    payables: '付款',
    receivables: '收款',
    searchPayable: '搜尋應付對象',
    searchReceivable: '搜尋客戶',
    choosePayable: '選擇未結應付款',
    chooseReceivable: '選擇未結應收款',
    loading: '載入中…',
    currency: '泰銖',
    outstanding: '未結金額（泰銖）',
    version: '未結餘額版本',
    sourceKind: '應付類型',
    salesDate: '銷售日期',
    amount: '結算金額（泰銖，可留空）',
    amountHint: '留空代表全額結算；填入正整數代表部分結算，且不可超過目前未結金額。',
    submitPayable: '登記付款',
    submitReceivable: '登記收款',
    submitting: '登記中…',
    queryFailed: '結算選項查詢失敗',
    required: '請先選擇一筆可結算資料。',
    amountInvalid: '金額必須留空，或輸入大於 0 的整數泰銖。',
    exceeds: '輸入金額不可超過目前未結金額。',
    stale: '未結金額已被其他操作變更，請重新載入後再登記。',
    unavailable: '所選結算對象已不存在或不可結算，請重新載入。',
    transportBlocked: '公司取貨運輸結算目前不可用；OUT-003 尚未解除。',
    idempotency: '此操作識別碼已被不同請求使用，請重新開始一次登記。',
    unexpected: '發生未預期錯誤。',
    successPayment: '付款已登記',
    successReceipt: '收款已登記',
    transactionId: '交易',
    settledAmount: '結算金額（泰銖）',
    remaining: '剩餘未結金額（泰銖）',
    confirmedAt: '確認時間',
  },
  'th-TH': {
    eyebrow: 'P7 · การเงิน',
    title: 'บันทึกการชำระเจ้าหนี้ / รับชำระลูกหนี้',
    intro: 'หน้านี้ใช้บันทึกการชำระเจ้าหนี้และการรับชำระลูกหนี้ หากเว้นจำนวนเงินว่างจะชำระยอดคงค้างปัจจุบันทั้งหมด หากกรอกจำนวนเงินจะเป็นการชำระบางส่วน',
    payables: 'จ่ายเงิน',
    receivables: 'รับเงิน',
    searchPayable: 'ค้นหาผู้รับเงิน',
    searchReceivable: 'ค้นหาลูกค้า',
    choosePayable: 'เลือกเจ้าหนี้คงค้าง',
    chooseReceivable: 'เลือกลูกหนี้คงค้าง',
    loading: 'กำลังโหลด…',
    currency: 'บาท',
    outstanding: 'ยอดคงค้าง (บาท)',
    version: 'รุ่นยอดคงค้าง',
    sourceKind: 'ประเภทเจ้าหนี้',
    salesDate: 'วันที่ขาย',
    amount: 'จำนวนเงิน (บาท, เว้นว่างได้)',
    amountHint: 'เว้นว่างหมายถึงชำระทั้งหมด กรอกจำนวนเต็มบวกหมายถึงชำระบางส่วน และต้องไม่เกินยอดคงค้าง',
    submitPayable: 'บันทึกการจ่ายเงิน',
    submitReceivable: 'บันทึกการรับเงิน',
    submitting: 'กำลังบันทึก…',
    queryFailed: 'โหลดตัวเลือกการชำระไม่สำเร็จ',
    required: 'โปรดเลือกรายการที่ชำระได้ก่อน',
    amountInvalid: 'จำนวนเงินต้องเว้นว่าง หรือเป็นจำนวนเต็มบาทที่มากกว่า 0',
    exceeds: 'จำนวนเงินต้องไม่เกินยอดคงค้างปัจจุบัน',
    stale: 'ยอดคงค้างถูกเปลี่ยนโดยการทำงานอื่น โปรดโหลดใหม่ก่อนบันทึก',
    unavailable: 'รายการชำระที่เลือกไม่มีอยู่หรือไม่สามารถชำระได้ โปรดโหลดใหม่',
    transportBlocked: 'การชำระค่าขนส่งรับสินค้าโดยบริษัทยังใช้ไม่ได้ เนื่องจาก OUT-003 ยังไม่ถูกปลด',
    idempotency: 'รหัสการทำงานนี้ถูกใช้กับคำขออื่นแล้ว โปรดเริ่มการบันทึกใหม่',
    unexpected: 'เกิดข้อผิดพลาดที่ไม่คาดคิด',
    successPayment: 'บันทึกการจ่ายเงินแล้ว',
    successReceipt: 'บันทึกการรับเงินแล้ว',
    transactionId: 'รายการชำระ',
    settledAmount: 'จำนวนที่ชำระ (บาท)',
    remaining: 'ยอดคงค้างที่เหลือ (บาท)',
    confirmedAt: 'เวลายืนยัน',
  },
} as const;

export function FinanceSettlementPage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const queryClient = useQueryClient();
  const [mode, setMode] = useState<SettlementMode>('payable');
  const [search, setSearch] = useState('');
  const [selectedId, setSelectedId] = useState('');
  const [amountText, setAmountText] = useState('');
  const [localError, setLocalError] = useState<string | null>(null);
  const deferredSearch = useDeferredValue(search);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);
  const numberFormat = useMemo(() => new Intl.NumberFormat(locale), [locale]);

  const payableQuery = useQuery({
    queryKey: ['finance-settlement-options', 'payables', locale, deferredSearch],
    queryFn: ({ signal }) => listFinancePayableOptions({ locale, search: deferredSearch, limit: 50, signal }),
    enabled: mode === 'payable',
    staleTime: 5_000,
  });

  const receivableQuery = useQuery({
    queryKey: ['finance-settlement-options', 'receivables', locale, deferredSearch],
    queryFn: ({ signal }) => listFinanceReceivableOptions({ locale, search: deferredSearch, limit: 50, signal }),
    enabled: mode === 'receivable',
    staleTime: 5_000,
  });

  const selectedPayable = mode === 'payable'
    ? payableQuery.data?.items.find((item) => item.payableId === selectedId) ?? null
    : null;
  const selectedReceivable = mode === 'receivable'
    ? receivableQuery.data?.items.find((item) => item.receivableId === selectedId) ?? null
    : null;
  const selectedOutstanding = selectedPayable?.outstandingThb ?? selectedReceivable?.outstandingThb ?? null;

  const mutation = useMutation<SettlementMutationResult, Error, SettlementMutationInput>({
    mutationFn: async (input) => {
      if (input.mode === 'payable') {
        const result = await payPayable(input.targetId, input.request, { idempotencyKey: input.idempotencyKey, locale });
        return {
          mode: 'payable',
          transactionId: result.paymentId,
          amountThb: result.amountThb,
          outstandingThb: result.outstandingThb,
          outstandingVersion: result.outstandingVersion,
          confirmedAt: result.confirmedAt,
        };
      }

      const result = await receiveReceivable(input.targetId, input.request, { idempotencyKey: input.idempotencyKey, locale });
      return {
        mode: 'receivable',
        transactionId: result.receiptId,
        amountThb: result.amountThb,
        outstandingThb: result.outstandingThb,
        outstandingVersion: result.outstandingVersion,
        confirmedAt: result.confirmedAt,
      };
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['finance-settlement-options'] });
    },
  });

  const apiProblem = mutation.error instanceof ApiProblemError ? mutation.error : null;
  const problemMessage = useMemo(() => {
    if (apiProblem === null) return mutation.error ? labels.unexpected : null;

    switch (apiProblem.code) {
      case 'finance.payable-not-found':
      case 'finance.receivable-not-found':
        return labels.unavailable;
      case 'finance.outstanding-changed':
        return labels.stale;
      case 'finance.payment-exceeds-outstanding':
      case 'finance.receipt-exceeds-outstanding':
        return labels.exceeds;
      case 'finance.transport-settlement-unavailable':
        return labels.transportBlocked;
      case 'finance.invalid-input':
        return labels.amountInvalid;
      case 'idempotency.key-reused':
        return labels.idempotency;
      default:
        return `${apiProblem.code}${apiProblem.problem.traceId ? ` · ${apiProblem.problem.traceId}` : ''}`;
    }
  }, [apiProblem, labels, mutation.error]);

  const activeQuery = mode === 'payable' ? payableQuery : receivableQuery;
  const queryErrorMessage = activeQuery.error
    ? activeQuery.error instanceof ApiProblemError
      ? `${labels.queryFailed}: ${activeQuery.error.code}`
      : labels.queryFailed
    : null;

  function resetSelection(nextMode?: SettlementMode) {
    if (nextMode) setMode(nextMode);
    setSelectedId('');
    setAmountText('');
    setLocalError(null);
    submissionIdentity.current = null;
    mutation.reset();
  }

  function handleSubmit() {
    const selected = selectedPayable ?? selectedReceivable;
    if (!selected) {
      setLocalError(labels.required);
      return;
    }

    let amountThb: number | null = null;
    const trimmed = amountText.trim();
    if (trimmed !== '') {
      amountThb = Number(trimmed);
      if (!Number.isSafeInteger(amountThb) || amountThb <= 0) {
        setLocalError(labels.amountInvalid);
        return;
      }
      if (amountThb > selected.outstandingThb) {
        setLocalError(labels.exceeds);
        return;
      }
    }

    setLocalError(null);
    mutation.reset();

    const expectedOutstandingVersion = selected.outstandingVersion;
    const request = { amountThb, expectedOutstandingVersion };
    const targetId = mode === 'payable'
      ? (selected as FinancePayableSettlementOption).payableId
      : (selected as FinanceReceivableSettlementOption).receivableId;
    const fingerprint = JSON.stringify({ mode, targetId, request });
    if (submissionIdentity.current?.fingerprint !== fingerprint) {
      submissionIdentity.current = { fingerprint, idempotencyKey: crypto.randomUUID() };
    }

    if (mode === 'payable') {
      mutation.mutate({ mode, targetId, request, idempotencyKey: submissionIdentity.current.idempotencyKey });
    } else {
      mutation.mutate({ mode, targetId, request, idempotencyKey: submissionIdentity.current.idempotencyKey });
    }
  }

  const options = mode === 'payable' ? payableQuery.data?.items ?? [] : receivableQuery.data?.items ?? [];

  return (
    <section className="procurement-page" aria-labelledby="finance-settlement-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="finance-settlement-title">{labels.title}</h1>
        </div>
      </header>

      <div className="procurement-grid">
        <div className="entry-form">
          <div className="field-grid">
            <button className="primary-action" type="button" disabled={mode === 'payable'} onClick={() => resetSelection('payable')}>
              {labels.payables}
            </button>
            <button className="primary-action" type="button" disabled={mode === 'receivable'} onClick={() => resetSelection('receivable')}>
              {labels.receivables}
            </button>

            <div className="option-picker full-width">
              <span className="field-label">{mode === 'payable' ? labels.payables : labels.receivables}</span>
              <input
                value={search}
                onChange={(event) => { setSearch(event.target.value); resetSelection(); }}
              />
              <select
                value={selectedId}
                onChange={(event) => { setSelectedId(event.target.value); setAmountText(''); setLocalError(null); submissionIdentity.current = null; mutation.reset(); }}
              >
                <option value="">{activeQuery.isPending ? labels.loading : mode === 'payable' ? labels.choosePayable : labels.chooseReceivable}</option>
                {options.map((item) => mode === 'payable' ? (
                  <option key={(item as FinancePayableSettlementOption).payableId} value={(item as FinancePayableSettlementOption).payableId}>
                    {(item as FinancePayableSettlementOption).sourceDisplayName} · {payableKindLabel((item as FinancePayableSettlementOption).payableKind, locale)} · {numberFormat.format(item.outstandingThb)} {labels.currency}
                  </option>
                ) : (
                  <option key={(item as FinanceReceivableSettlementOption).receivableId} value={(item as FinanceReceivableSettlementOption).receivableId}>
                    {(item as FinanceReceivableSettlementOption).salesDate} · {(item as FinanceReceivableSettlementOption).customerDisplayName} · {numberFormat.format(item.outstandingThb)} {labels.currency}
                  </option>
                ))}
              </select>
            </div>

            <label className="full-width">
              <span>{labels.amount}</span>
              <input
                type="number"
                min="1"
                step="1"
                inputMode="numeric"
                value={amountText}
                onChange={(event) => { setAmountText(event.target.value); setLocalError(null); mutation.reset(); }}
                disabled={selectedOutstanding === null}
              />
            </label>
          </div>


          {selectedPayable && (
            <dl>
              <ResultRow label={labels.sourceKind} value={payableKindLabel(selectedPayable.payableKind, locale)} />
              <ResultRow label={labels.outstanding} value={numberFormat.format(selectedPayable.outstandingThb)} />
              <ResultRow label={labels.version} value={String(selectedPayable.outstandingVersion)} />
            </dl>
          )}

          {selectedReceivable && (
            <dl>
              <ResultRow label={labels.salesDate} value={selectedReceivable.salesDate} />
              <ResultRow label={labels.outstanding} value={numberFormat.format(selectedReceivable.outstandingThb)} />
              <ResultRow label={labels.version} value={String(selectedReceivable.outstandingVersion)} />
            </dl>
          )}

          {(localError ?? problemMessage ?? queryErrorMessage) && (
            <div className="problem-banner" role="alert">{localError ?? problemMessage ?? queryErrorMessage}</div>
          )}

          <button className="primary-action" type="button" onClick={handleSubmit} disabled={mutation.isPending || selectedOutstanding === null}>
            {mutation.isPending ? labels.submitting : mode === 'payable' ? labels.submitPayable : labels.submitReceivable}
          </button>
        </div>

        <aside className="result-panel" aria-live="polite">
          {mutation.data ? (
            <>
              <p className="eyebrow">{mutation.data.mode === 'payable' ? labels.successPayment : labels.successReceipt}</p>
              <dl>
                <ResultRow label={labels.transactionId} value={mutation.data.transactionId} />
                <ResultRow label={labels.settledAmount} value={numberFormat.format(mutation.data.amountThb)} />
                <ResultRow label={labels.remaining} value={numberFormat.format(mutation.data.outstandingThb)} />
                <ResultRow label={labels.version} value={String(mutation.data.outstandingVersion)} />
                <ResultRow label={labels.confirmedAt} value={mutation.data.confirmedAt} />
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

function payableKindLabel(kind: string, locale: 'zh-TW' | 'th-TH'): string {
  const zh: Record<string, string> = {
    PROCUREMENT_SUPPLIER: '供應商採購',
    PROCUREMENT_FARMER: '農戶採購',
    COMPANY_PICKUP_TRANSPORT: '公司取貨運輸',
    OUTSOURCED_VENDOR: '委外供應',
    EMPLOYEE_DAILY_WAGE: '員工每日工資',
  };
  const th: Record<string, string> = {
    PROCUREMENT_SUPPLIER: 'เจ้าหนี้จัดซื้อจากผู้จำหน่าย',
    PROCUREMENT_FARMER: 'เจ้าหนี้จัดซื้อจากเกษตรกร',
    COMPANY_PICKUP_TRANSPORT: 'ค่าขนส่งรับสินค้าโดยบริษัท',
    OUTSOURCED_VENDOR: 'เจ้าหนี้งานภายนอก',
    EMPLOYEE_DAILY_WAGE: 'ค่าแรงรายวันพนักงาน',
  };
  return (locale === 'zh-TW' ? zh : th)[kind] ?? kind;
}

function ResultRow({ label, value }: { label: string; value: string }) {
  return <div className="result-row"><dt>{label}</dt><dd>{value}</dd></div>;
}
