import { useMutation, useQuery } from '@tanstack/react-query';
import { useDeferredValue, useMemo, useRef, useState } from 'react';

import { useOperationalLocale } from '../../app/i18n/locale';
import {
  ApiProblemError,
  confirmEmployeeDailyWage,
  type ConfirmEmployeeDailyWageRequest,
} from './confirmEmployeeDailyWage';
import {
  getLaborDailyWageWorkspace,
  listLaborDailyWageEmployeeOptions,
  type LaborProcessingWageTarget,
} from './laborDailyWageOptions';

interface SubmissionIdentity {
  fingerprint: string;
  idempotencyKey: string;
}

const copy = {
  'zh-TW': {
    eyebrow: 'P7 · 工資',
    title: '確認員工每日工資',
    intro: '此畫面直接使用正式 ConfirmEmployeeDailyWage command。系統依員工與工作日期彙總尚未結算的 Processing outputs 與 Sales Packaging work；只有 Processing applied wage rate 可依既有 command contract 覆寫。',
    employee: '員工',
    employeeSearch: '搜尋員工名稱',
    employeeSelect: '選擇員工',
    workDate: '工作日期',
    workspace: '待結算工資來源',
    loading: '載入中…',
    queryFailed: '每日工資資料查詢失敗',
    processing: '加工工資來源',
    noProcessing: '此日期沒有尚未結算的加工工資來源。',
    configuredRate: '設定工資率',
    appliedRate: '套用工資率',
    quantity: '彙總數量',
    packaging: '銷售包裝工資',
    packagingRecords: '工作紀錄數',
    packagingTotal: '已確認包裝工資合計（泰銖）',
    noPackaging: '此日期沒有尚未結算的銷售包裝工資紀錄。',
    alreadyConfirmed: '此員工該工作日的每日工資已確認；依 LABOR-001，不可再次確認。',
    invalidRate: '套用工資率必須是 0 或正數。',
    employeeUnavailable: '所選員工目前不存在或不可用，請重新載入並選擇啟用中的員工。',
    targetChanged: '加工工資對象已變更；請重新載入待結算工資來源後再確認。',
    wageInvalid: '工資金額計算無效，無法確認。',
    concurrent: '相關工資來源已被其他操作變更，請重新載入後再確認。',
    idempotency: '此操作識別碼已被不同請求使用，請重新開始一次確認。',
    unexpected: '發生未預期錯誤。',
    submit: '確認每日工資',
    submitting: '確認中…',
    success: '每日工資已確認',
    dailyWageId: '每日工資',
    payableId: '應付款',
    processingTotal: '加工工資（泰銖）',
    packagingResultTotal: '銷售包裝工資（泰銖）',
    total: '工資總額（泰銖）',
    rowVersion: '資料版本',
  },
  'th-TH': {
    eyebrow: 'P7 · ค่าจ้าง',
    title: 'ยืนยันค่าจ้างรายวันของพนักงาน',
    intro: 'หน้านี้ใช้ ConfirmEmployeeDailyWage จริง ระบบรวบรวม Processing outputs และ Sales Packaging work ที่ยังไม่ถูกคิดค่าจ้างตามพนักงานและวันที่ทำงาน โดยแก้ไขได้เฉพาะ Processing applied wage rate ตาม command contract ที่มีอยู่',
    employee: 'พนักงาน',
    employeeSearch: 'ค้นหาชื่อพนักงาน',
    employeeSelect: 'เลือกพนักงาน',
    workDate: 'วันที่ทำงาน',
    workspace: 'แหล่งค่าจ้างที่รอยืนยัน',
    loading: 'กำลังโหลด…',
    queryFailed: 'โหลดข้อมูลค่าจ้างรายวันไม่สำเร็จ',
    processing: 'แหล่งค่าจ้างงานแปรรูป',
    noProcessing: 'วันนี้ไม่มีแหล่งค่าจ้างงานแปรรูปที่ยังไม่ถูกคิดค่าจ้าง',
    configuredRate: 'อัตราค่าจ้างที่กำหนด',
    appliedRate: 'อัตราค่าจ้างที่ใช้',
    quantity: 'ปริมาณรวม',
    packaging: 'ค่าจ้างงานบรรจุขาย',
    packagingRecords: 'จำนวนบันทึกงาน',
    packagingTotal: 'ค่าจ้างงานบรรจุที่ยืนยันแล้วรวม (บาท)',
    noPackaging: 'วันนี้ไม่มีบันทึกค่าจ้างงานบรรจุขายที่ยังไม่ถูกคิดค่าจ้าง',
    alreadyConfirmed: 'ค่าจ้างรายวันของพนักงานในวันนี้ถูกยืนยันแล้ว ตาม LABOR-001 ไม่สามารถยืนยันซ้ำได้',
    invalidRate: 'อัตราค่าจ้างที่ใช้ต้องเป็น 0 หรือจำนวนบวก',
    employeeUnavailable: 'พนักงานที่เลือกไม่มีอยู่หรือไม่พร้อมใช้งาน โปรดโหลดใหม่และเลือกพนักงานที่ใช้งานอยู่',
    targetChanged: 'รายการค่าจ้างงานแปรรูปเปลี่ยนไป โปรดโหลดแหล่งค่าจ้างที่รอยืนยันใหม่ก่อนยืนยัน',
    wageInvalid: 'การคำนวณค่าจ้างไม่ถูกต้อง จึงไม่สามารถยืนยันได้',
    concurrent: 'แหล่งค่าจ้างถูกแก้ไขโดยการทำงานอื่น โปรดโหลดใหม่ก่อนยืนยัน',
    idempotency: 'รหัสการทำงานนี้ถูกใช้กับคำขออื่นแล้ว โปรดเริ่มการยืนยันใหม่',
    unexpected: 'เกิดข้อผิดพลาดที่ไม่คาดคิด',
    submit: 'ยืนยันค่าจ้างรายวัน',
    submitting: 'กำลังยืนยัน…',
    success: 'ยืนยันค่าจ้างรายวันแล้ว',
    dailyWageId: 'ค่าจ้างรายวัน',
    payableId: 'เจ้าหนี้',
    processingTotal: 'ค่าจ้างงานแปรรูป (บาท)',
    packagingResultTotal: 'ค่าจ้างงานบรรจุขาย (บาท)',
    total: 'ค่าจ้างรวม (บาท)',
    rowVersion: 'รุ่นข้อมูล',
  },
} as const;

export function LaborDailyWagePage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const [employeeSearch, setEmployeeSearch] = useState('');
  const [employeeId, setEmployeeId] = useState('');
  const [workDate, setWorkDate] = useState('');
  const [rateInputs, setRateInputs] = useState<Record<string, string>>({});
  const [localError, setLocalError] = useState<string | null>(null);
  const deferredEmployeeSearch = useDeferredValue(employeeSearch);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);

  const employeeQuery = useQuery({
    queryKey: ['labor-daily-wage-options', 'employees', locale, deferredEmployeeSearch],
    queryFn: ({ signal }) => listLaborDailyWageEmployeeOptions({ locale, search: deferredEmployeeSearch, limit: 50, signal }),
    staleTime: 30_000,
  });

  const workspaceQuery = useQuery({
    queryKey: ['labor-daily-wage-workspace', employeeId, workDate, locale],
    queryFn: ({ signal }) => getLaborDailyWageWorkspace(employeeId, workDate, locale, signal),
    enabled: employeeId !== '' && workDate !== '',
    staleTime: 5_000,
  });

  const workspace = workspaceQuery.data ?? null;

  const mutation = useMutation({
    mutationFn: ({ request, idempotencyKey }: { request: ConfirmEmployeeDailyWageRequest; idempotencyKey: string }) =>
      confirmEmployeeDailyWage(request, { idempotencyKey, locale }),
  });

  const apiProblem = mutation.error instanceof ApiProblemError ? mutation.error : null;
  const problemMessage = useMemo(() => {
    if (apiProblem === null) return mutation.error ? labels.unexpected : null;
    switch (apiProblem.code) {
      case 'labor.employee-not-found':
      case 'labor.employee-inactive':
        return labels.employeeUnavailable;
      case 'labor.daily-wage-already-confirmed':
        return labels.alreadyConfirmed;
      case 'labor.processing-rate-override-invalid':
      case 'labor.processing-rate-override-target-not-found':
        return labels.targetChanged;
      case 'labor.wage-amount-invalid':
        return labels.wageInvalid;
      case 'labor.concurrent-change':
        return labels.concurrent;
      case 'idempotency.key-reused':
        return labels.idempotency;
      default:
        return `${apiProblem.code}${apiProblem.problem.traceId ? ` · ${apiProblem.problem.traceId}` : ''}`;
    }
  }, [apiProblem, labels, mutation.error]);

  const queryError = employeeQuery.error ?? workspaceQuery.error;
  const queryErrorMessage = queryError
    ? queryError instanceof ApiProblemError
      ? `${labels.queryFailed}: ${queryError.code}`
      : labels.queryFailed
    : null;

  function handleConfirm() {
    if (!workspace || workspace.alreadyConfirmed) return;
    setLocalError(null);
    mutation.reset();

    const processingWageRateOverrides = [];
    for (const target of workspace.processingTargets) {
      const applied = Number(rateInputs[targetKey(target)] ?? target.configuredWageRateSnapshot);
      if (!Number.isFinite(applied) || applied < 0) {
        setLocalError(labels.invalidRate);
        return;
      }
      if (applied !== target.configuredWageRateSnapshot) {
        processingWageRateOverrides.push({
          processingModuleOutputId: target.processingModuleOutputId,
          configuredWageRateSnapshot: target.configuredWageRateSnapshot,
          appliedWageRate: applied,
        });
      }
    }

    const request: ConfirmEmployeeDailyWageRequest = {
      workDate: workspace.workDate,
      employeeId: workspace.employeeId,
      processingWageRateOverrides,
    };
    const fingerprint = JSON.stringify(request);
    if (submissionIdentity.current?.fingerprint !== fingerprint) {
      submissionIdentity.current = { fingerprint, idempotencyKey: crypto.randomUUID() };
    }
    mutation.mutate({ request, idempotencyKey: submissionIdentity.current.idempotencyKey });
  }

  return (
    <section className="procurement-page" aria-labelledby="labor-daily-wage-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="labor-daily-wage-title">{labels.title}</h1>
        </div>
      </header>

      <div className="procurement-grid">
        <div className="entry-form">
          <div className="field-grid">
            <div className="option-picker full-width">
              <span className="field-label">{labels.employee}</span>
              <input value={employeeSearch} onChange={(event) => setEmployeeSearch(event.target.value)} />
              <select value={employeeId} onChange={(event) => { setEmployeeId(event.target.value); setRateInputs({}); submissionIdentity.current = null; mutation.reset(); setLocalError(null); }}>
                <option value="">{employeeQuery.isPending ? labels.loading : labels.employeeSelect}</option>
                {(employeeQuery.data?.items ?? []).map((item) => <option key={item.id} value={item.id}>{item.displayName}</option>)}
              </select>
            </div>
            <label>
              <span>{labels.workDate}</span>
              <input type="date" value={workDate} onChange={(event) => { setWorkDate(event.target.value); setRateInputs({}); submissionIdentity.current = null; mutation.reset(); setLocalError(null); }} />
            </label>
          </div>

          {workspaceQuery.isPending && employeeId !== '' && workDate !== '' && <p>{labels.loading}</p>}

          {workspace && (
            <>
              <h2>{labels.workspace}</h2>
              {workspace.alreadyConfirmed && <div className="problem-banner" role="alert">{labels.alreadyConfirmed}</div>}

              <h2>{labels.processing}</h2>
              {workspace.processingTargets.length === 0 ? <p className="page-intro">{labels.noProcessing}</p> : (
                <div>
                  {workspace.processingTargets.map((target) => (
                    <div className="result-row" key={targetKey(target)}>
                      <div>
                        <strong>{target.displayName}</strong>
                        <div>{labels.quantity}: {target.aggregatedQuantity} · {labels.configuredRate}: {target.configuredWageRateSnapshot}</div>
                      </div>
                      <label>
                        <span>{labels.appliedRate}</span>
                        <input
                          type="number"
                          min="0"
                          step="any"
                          inputMode="decimal"
                          value={rateInputs[targetKey(target)] ?? String(target.configuredWageRateSnapshot)}
                          disabled={workspace.alreadyConfirmed}
                          onChange={(event) => setRateInputs((current) => ({ ...current, [targetKey(target)]: event.target.value }))}
                        />
                      </label>
                    </div>
                  ))}
                </div>
              )}

              <h2>{labels.packaging}</h2>
              {workspace.salesPackagingWorkRecordCount === 0 ? <p className="page-intro">{labels.noPackaging}</p> : (
                <dl>
                  <ResultRow label={labels.packagingRecords} value={String(workspace.salesPackagingWorkRecordCount)} />
                  <ResultRow label={labels.packagingTotal} value={new Intl.NumberFormat(locale).format(workspace.salesPackagingWageTotalThb)} />
                </dl>
              )}

              {(localError ?? problemMessage ?? queryErrorMessage) && <div className="problem-banner" role="alert">{localError ?? problemMessage ?? queryErrorMessage}</div>}
              <button className="primary-action" type="button" onClick={handleConfirm} disabled={mutation.isPending || workspace.alreadyConfirmed}>
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
                <ResultRow label={labels.dailyWageId} value={mutation.data.employeeDailyWageId} />
                <ResultRow label={labels.payableId} value={mutation.data.payableId} />
                <ResultRow label={labels.processingTotal} value={new Intl.NumberFormat(locale).format(mutation.data.processingWageTotalThb)} />
                <ResultRow label={labels.packagingResultTotal} value={new Intl.NumberFormat(locale).format(mutation.data.salesPackagingWageTotalThb)} />
                <ResultRow label={labels.total} value={new Intl.NumberFormat(locale).format(mutation.data.totalWageThb)} />
                <ResultRow label={labels.rowVersion} value={String(mutation.data.rowVersion)} />
              </dl>
            </>
          ) : <div className="result-placeholder" aria-hidden="true"><span>YowThi ERP V2</span></div>}
        </aside>
      </div>
    </section>
  );
}

function targetKey(target: LaborProcessingWageTarget): string {
  return `${target.processingModuleOutputId}|${target.configuredWageRateSnapshot}`;
}

function ResultRow({ label, value }: { label: string; value: string }) {
  return <div className="result-row"><dt>{label}</dt><dd>{value}</dd></div>;
}
