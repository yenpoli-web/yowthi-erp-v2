import { useMutation, useQuery } from '@tanstack/react-query';
import {
  type FormEvent,
  useDeferredValue,
  useMemo,
  useRef,
  useState,
} from 'react';

import { type OperationalLocale, useOperationalLocale } from '../../app/i18n/locale';
import {
  ApiProblemError,
  confirmProcurementEntry,
  type ConfirmProcurementEntryRequest,
  type ProcurementSourceType,
} from './confirmProcurementEntry';
import {
  listProcurementProductOptions,
  listProcurementReceiptStorageLocationOptions,
  listProcurementSourceOptions,
  type ProcurementProductOption,
  type ProcurementReceiptStorageLocationOption,
  type ProcurementSourceOption,
} from './procurementEntryOptions';

interface Submission {
  request: ConfirmProcurementEntryRequest;
  idempotencyKey: string;
  locale: OperationalLocale;
}

interface SubmissionIdentity {
  fingerprint: string;
  idempotencyKey: string;
}

const copy = {
  'zh-TW': {
    eyebrow: 'P6 · 採購',
    title: '確認採購登錄',
    intro: '此畫面直接使用正式 ConfirmProcurementEntry command。產品、來源與收貨儲位由 purpose-specific query 提供，不再要求操作人員手動貼 UUID。',
    locale: '介面語言',
    date: '採購日期',
    product: '採購產品',
    productSearch: '搜尋產品名稱',
    productSelect: '選擇採購產品',
    sourceType: '供應來源',
    source: '供應來源對象',
    sourceSearch: '搜尋供應商 / 農戶',
    sourceSelect: '選擇來源對象',
    supplierLabel: '供應商',
    farmerLabel: '農戶',
    quantity: '淨數量',
    unitPrice: '單價',
    companyPickup: '公司取貨',
    location: '收貨儲位',
    locationSearch: '搜尋儲位名稱／代碼',
    useDefault: '由伺服器使用產品預設收貨儲位',
    locationSelect: '選擇明確收貨儲位',
    explicitLocationRequired: '此產品目前沒有可用的預設收貨儲位；依 PROC-002，確認前必須明確選擇收貨儲位。',
    defaultLocation: '目前可用預設',
    loading: '載入選項中…',
    queryFailed: '選項查詢失敗',
    submit: '確認採購登錄',
    submitting: '確認中…',
    validation: '請檢查輸入資料。',
    quantityPositive: '依 PROC-003，淨數量必須大於 0。',
    priceNonNegative: '單價不可小於 0。',
    productRequired: '請選擇採購產品。',
    sourceRequired: '請選擇供應來源對象。',
    locationRequired: '請明確選擇收貨儲位。',
    batchCompleted: '此採購批次已完成；依 PROC-001，目前不允許一般補登。',
    idempotencyConflict: '此操作識別碼已被不同請求使用。請修改輸入或重新開始一次確認。',
    success: '採購登錄已確認',
    entryId: '採購登錄',
    batchId: '採購批次',
    payableId: '應付款',
    amount: '金額（泰銖）',
    receiptLocation: '收貨儲位',
    transportBasis: '公司取貨運輸依據',
    noTransportBasis: '未建立',
    unexpected: '發生未預期錯誤。',
  },
  'th-TH': {
    eyebrow: 'P6 · จัดซื้อ',
    title: 'ยืนยันรายการจัดซื้อ',
    intro: 'หน้าจอนี้ใช้ ConfirmProcurementEntry จริง และโหลดตัวเลือกสินค้า แหล่งซื้อ และตำแหน่งรับสินค้าจาก query เฉพาะงาน ไม่ต้องกรอก UUID ด้วยตนเอง',
    locale: 'ภาษา',
    date: 'วันที่จัดซื้อ',
    product: 'สินค้าจัดซื้อ',
    productSearch: 'ค้นหาชื่อสินค้า',
    productSelect: 'เลือกสินค้า',
    sourceType: 'ประเภทแหล่งที่มา',
    source: 'แหล่งที่มา',
    sourceSearch: 'ค้นหาผู้จำหน่าย / เกษตรกร',
    sourceSelect: 'เลือกแหล่งที่มา',
    supplierLabel: 'ผู้จำหน่าย',
    farmerLabel: 'เกษตรกร',
    quantity: 'ปริมาณสุทธิ',
    unitPrice: 'ราคาต่อหน่วย',
    companyPickup: 'บริษัทไปรับสินค้า',
    location: 'ตำแหน่งรับสินค้า',
    locationSearch: 'ค้นหาชื่อ / รหัสตำแหน่ง',
    useDefault: 'ให้เซิร์ฟเวอร์ใช้ตำแหน่งรับสินค้าหลักของสินค้า',
    locationSelect: 'เลือกตำแหน่งรับสินค้า',
    explicitLocationRequired: 'สินค้านี้ไม่มีตำแหน่งรับสินค้าหลักที่ใช้งานได้ ตาม PROC-002 ต้องเลือกตำแหน่งรับสินค้าให้ชัดเจนก่อนยืนยัน',
    defaultLocation: 'ตำแหน่งหลักที่ใช้งานได้',
    loading: 'กำลังโหลดตัวเลือก…',
    queryFailed: 'โหลดตัวเลือกไม่สำเร็จ',
    submit: 'ยืนยันรายการจัดซื้อ',
    submitting: 'กำลังยืนยัน…',
    validation: 'โปรดตรวจสอบข้อมูล',
    quantityPositive: 'ตาม PROC-003 ปริมาณสุทธิต้องมากกว่า 0',
    priceNonNegative: 'ราคาต่อหน่วยต้องไม่ติดลบ',
    productRequired: 'โปรดเลือกสินค้าจัดซื้อ',
    sourceRequired: 'โปรดเลือกแหล่งที่มา',
    locationRequired: 'โปรดเลือกตำแหน่งรับสินค้าให้ชัดเจน',
    batchCompleted: 'ล็อตจัดซื้อนี้เสร็จสมบูรณ์แล้ว ตาม PROC-001 ไม่อนุญาตให้เพิ่มรายการย้อนหลังแบบปกติ',
    idempotencyConflict: 'รหัสการทำงานนี้ถูกใช้กับคำขออื่นแล้ว โปรดแก้ข้อมูลหรือเริ่มการยืนยันครั้งใหม่',
    success: 'ยืนยันรายการจัดซื้อแล้ว',
    entryId: 'รายการจัดซื้อ',
    batchId: 'ล็อตจัดซื้อ',
    payableId: 'เจ้าหนี้',
    amount: 'จำนวนเงิน (บาท)',
    receiptLocation: 'ตำแหน่งรับสินค้า',
    transportBasis: 'เกณฑ์การขนส่งรับสินค้าโดยบริษัท',
    noTransportBasis: 'ไม่ได้สร้าง',
    unexpected: 'เกิดข้อผิดพลาดที่ไม่คาดคิด',
  },
} as const;

export function ProcurementEntryPage() {
  const { locale, setLocale } = useOperationalLocale();
  const [sourceType, setSourceType] = useState<ProcurementSourceType>('SUPPLIER');
  const [productSearch, setProductSearch] = useState('');
  const [sourceSearch, setSourceSearch] = useState('');
  const [locationSearch, setLocationSearch] = useState('');
  const [selectedProduct, setSelectedProduct] = useState<ProcurementProductOption | null>(null);
  const [selectedSource, setSelectedSource] = useState<ProcurementSourceOption | null>(null);
  const [selectedLocation, setSelectedLocation] = useState<ProcurementReceiptStorageLocationOption | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);
  const labels = copy[locale];

  const deferredProductSearch = useDeferredValue(productSearch);
  const deferredSourceSearch = useDeferredValue(sourceSearch);
  const deferredLocationSearch = useDeferredValue(locationSearch);

  const productQuery = useQuery({
    queryKey: ['procurement-entry-options', 'products', locale, deferredProductSearch],
    queryFn: ({ signal }) =>
      listProcurementProductOptions({
        locale,
        search: deferredProductSearch,
        limit: 50,
        signal,
      }),
    staleTime: 30_000,
  });

  const sourceQuery = useQuery({
    queryKey: ['procurement-entry-options', 'sources', sourceType, locale, deferredSourceSearch],
    queryFn: ({ signal }) =>
      listProcurementSourceOptions(sourceType, {
        locale,
        search: deferredSourceSearch,
        limit: 50,
        signal,
      }),
    staleTime: 30_000,
  });

  const locationQuery = useQuery({
    queryKey: [
      'procurement-entry-options',
      'storage-locations',
      selectedProduct?.id ?? null,
      locale,
      deferredLocationSearch,
    ],
    queryFn: ({ signal }) =>
      listProcurementReceiptStorageLocationOptions(selectedProduct!.id, {
        locale,
        search: deferredLocationSearch,
        limit: 50,
        signal,
      }),
    enabled: selectedProduct !== null,
    staleTime: 30_000,
  });

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

  const optionError = firstOptionError(productQuery.error, sourceQuery.error, locationQuery.error);
  const optionErrorMessage = optionError
    ? optionError instanceof ApiProblemError
      ? `${labels.queryFailed}: ${optionError.code}`
      : labels.queryFailed
    : null;

  const productItems = includeSelected(productQuery.data?.items ?? [], selectedProduct);
  const sourceItems = includeSelected(sourceQuery.data?.items ?? [], selectedSource);
  const locationItems = includeSelected(locationQuery.data?.items ?? [], selectedLocation);
  const applicableDefault = locationQuery.data?.items.find((item) => item.isProductDefault) ?? null;
  const requiresExplicitLocation =
    selectedProduct !== null
    && locationQuery.isSuccess
    && locationQuery.data.defaultStorageLocationId === null;

  function handleLocaleChange(next: OperationalLocale) {
    setLocale(next);
    setProductSearch('');
    setSourceSearch('');
    setLocationSearch('');
    setSelectedProduct(null);
    setSelectedSource(null);
    setSelectedLocation(null);
    mutation.reset();
    setLocalError(null);
  }

  function handleSourceTypeChange(next: ProcurementSourceType) {
    setSourceType(next);
    setSelectedSource(null);
    setSourceSearch('');
    mutation.reset();
    setLocalError(null);
  }

  function handleProductChange(productId: string) {
    const option = productItems.find((item) => item.id === productId) ?? null;
    setSelectedProduct(option);
    setSelectedLocation(null);
    setLocationSearch('');
    mutation.reset();
    setLocalError(null);
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLocalError(null);
    mutation.reset();

    const formData = new FormData(event.currentTarget);

    try {
      if (selectedProduct === null) {
        throw new Error(labels.productRequired);
      }

      if (selectedSource === null) {
        throw new Error(labels.sourceRequired);
      }

      if (requiresExplicitLocation && selectedLocation === null) {
        throw new Error(labels.locationRequired);
      }

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
        procurementProductId: selectedProduct.id,
        sourceType,
        supplierId: sourceType === 'SUPPLIER' ? selectedSource.id : null,
        farmerId: sourceType === 'FARMER' ? selectedSource.id : null,
        netQuantity,
        unitPrice,
        companyPickup: formData.get('companyPickup') === 'on',
        receiptStorageLocationId: selectedLocation?.id ?? null,
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

  const submitDisabled =
    mutation.isPending
    || selectedProduct === null
    || selectedSource === null
    || locationQuery.isPending
    || locationQuery.isError;

  return (
    <section className="procurement-page" aria-labelledby="procurement-entry-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="procurement-entry-title">{labels.title}</h1>
        </div>

        <label className="locale-control">
          <span>{labels.locale}</span>
          <select value={locale} onChange={(event) => handleLocaleChange(event.target.value as OperationalLocale)}>
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
              <span>{labels.sourceType}</span>
              <select
                name="sourceType"
                value={sourceType}
                onChange={(event) => handleSourceTypeChange(event.target.value as ProcurementSourceType)}
              >
                <option value="SUPPLIER">{labels.supplierLabel}</option>
                <option value="FARMER">{labels.farmerLabel}</option>
              </select>
            </label>

            <div className="option-picker full-width">
              <span className="field-label">{labels.product}</span>
              <input
                value={productSearch}
                onChange={(event) => setProductSearch(event.target.value)}
                aria-label={labels.productSearch}
              />
              <select
                value={selectedProduct?.id ?? ''}
                onChange={(event) => handleProductChange(event.target.value)}
                required
              >
                <option value="">{productQuery.isPending ? labels.loading : labels.productSelect}</option>
                {productItems.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.displayName} · {item.unitCode}
                  </option>
                ))}
              </select>
            </div>

            <div className="option-picker full-width">
              <span className="field-label">{labels.source}</span>
              <input
                value={sourceSearch}
                onChange={(event) => setSourceSearch(event.target.value)}
                aria-label={labels.sourceSearch}
              />
              <select
                value={selectedSource?.id ?? ''}
                onChange={(event) =>
                  setSelectedSource(sourceItems.find((item) => item.id === event.target.value) ?? null)
                }
                required
              >
                <option value="">{sourceQuery.isPending ? labels.loading : labels.sourceSelect}</option>
                {sourceItems.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.displayName}
                  </option>
                ))}
              </select>
            </div>

            <label>
              <span>
                {labels.quantity}
                {selectedProduct ? ` · ${selectedProduct.unitCode}` : ''}
              </span>
              <input type="number" name="netQuantity" min="0" step="any" inputMode="decimal" required />
            </label>

            <label>
              <span>{labels.unitPrice}</span>
              <input type="number" name="unitPrice" min="0" step="any" inputMode="decimal" required />
            </label>

            <div className="option-picker full-width">
              <span className="field-label">{labels.location}</span>
              <input
                value={locationSearch}
                onChange={(event) => setLocationSearch(event.target.value)}
                aria-label={labels.locationSearch}
                disabled={selectedProduct === null}
              />
              <select
                value={selectedLocation?.id ?? ''}
                onChange={(event) =>
                  setSelectedLocation(locationItems.find((item) => item.id === event.target.value) ?? null)
                }
                disabled={selectedProduct === null || locationQuery.isPending}
                required={requiresExplicitLocation}
              >
                <option value="">
                  {locationQuery.isPending
                    ? labels.loading
                    : locationQuery.data?.defaultStorageLocationId
                      ? labels.useDefault
                      : labels.locationSelect}
                </option>
                {locationItems.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.displayName}
                    {item.code ? ` · ${item.code}` : ''}
                    {item.isProductDefault ? ` · ${labels.defaultLocation}` : ''}
                  </option>
                ))}
              </select>
              {applicableDefault && selectedLocation === null && (
                <small className="default-hint">
                  {labels.defaultLocation}: {applicableDefault.displayName}
                </small>
              )}
              {requiresExplicitLocation && selectedLocation === null && (
                <small className="required-hint">{labels.explicitLocationRequired}</small>
              )}
            </div>
          </div>

          <label className="checkbox-field">
            <input type="checkbox" name="companyPickup" />
            <span>{labels.companyPickup}</span>
          </label>

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
                <ResultRow label={labels.entryId} value={mutation.data.procurementEntryId} />
                <ResultRow label={labels.batchId} value={mutation.data.procurementBatchId} />
                <ResultRow label={labels.payableId} value={mutation.data.payableId} />
                <ResultRow label={labels.amount} value={new Intl.NumberFormat(locale).format(mutation.data.amountThb)} />
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

function includeSelected<T extends { id: string }>(items: T[], selected: T | null): T[] {
  if (selected === null || items.some((item) => item.id === selected.id)) {
    return items;
  }

  return [selected, ...items];
}

function firstOptionError(...errors: Array<Error | null>): Error | null {
  return errors.find((error) => error !== null) ?? null;
}

function requiredText(formData: FormData, name: string): string {
  const value = formData.get(name);
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error(`${name} is required.`);
  }

  return value.trim();
}

function requiredNumber(formData: FormData, name: string): number {
  const raw = requiredText(formData, name);
  const value = Number(raw);
  if (!Number.isFinite(value)) {
    throw new Error(`${name} must be a finite number.`);
  }

  return value;
}
