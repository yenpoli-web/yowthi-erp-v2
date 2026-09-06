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
  confirmOutsourcedSupplyDetail,
  type ConfirmOutsourcedSupplyDetailRequest,
} from './confirmOutsourcedSupplyDetail';
import {
  listOutsourcedReceiptStorageLocationOptions,
  listOutsourcedSalesProductOptions,
  listOutsourcedVendorOptions,
  type OutsourcedReceiptStorageLocationOption,
  type OutsourcedSalesProductOption,
  type OutsourcedVendorOption,
} from './outsourcedSupplyDetailOptions';

interface Submission {
  request: ConfirmOutsourcedSupplyDetailRequest;
  idempotencyKey: string;
  locale: OperationalLocale;
}

interface SubmissionIdentity {
  fingerprint: string;
  idempotencyKey: string;
}

const copy = {
  'zh-TW': {
    eyebrow: 'P6 · 委外供應',
    title: '確認委外供應明細',
    intro: '此畫面直接使用正式 ConfirmOutsourcedSupplyDetail command。委外商、銷售產品與收貨儲位皆由 purpose-specific query 提供，不需要手動輸入 UUID。',
    locale: '介面語言',
    date: '供應日期',
    vendor: '委外商',
    vendorSearch: '搜尋委外商名稱',
    vendorSelect: '選擇委外商',
    product: '銷售產品',
    productSearch: '搜尋產品名稱',
    productSelect: '選擇銷售產品',
    pricingBasis: '計價基礎',
    quantity: '數量',
    unitPrice: '單價',
    location: '收貨儲位',
    locationSearch: '搜尋儲位名稱／代碼',
    useDefault: '由伺服器使用產品預設收貨儲位',
    locationSelect: '選擇明確收貨儲位',
    explicitLocationRequired: '此產品目前沒有可用的預設收貨儲位；確認前必須明確選擇收貨儲位。',
    defaultLocation: '目前可用預設',
    loading: '載入選項中…',
    queryFailed: '選項查詢失敗',
    submit: '確認委外供應明細',
    submitting: '確認中…',
    validation: '請檢查輸入資料。',
    quantityPositive: '依 OUT-002，數量必須大於 0。',
    priceNonNegative: '單價不可小於 0。',
    vendorRequired: '請選擇委外商。',
    productRequired: '請選擇銷售產品。',
    locationRequired: '請明確選擇收貨儲位。',
    batchClosed: '此委外供應批次已關閉；依目前未確認規則，一般補登仍被阻擋。',
    idempotencyConflict: '此操作識別碼已被不同請求使用。請修改輸入或重新開始一次確認。',
    success: '委外供應明細已確認',
    detailId: '委外供應明細',
    batchId: '委外供應批次',
    inventoryOperationId: '庫存操作',
    payableId: '應付款',
    amount: '金額（泰銖）',
    receiptLocation: '收貨儲位',
    rowVersion: '資料版本',
    unexpected: '發生未預期錯誤。',
  },
  'th-TH': {
    eyebrow: 'P6 · จัดหาจากผู้รับจ้างภายนอก',
    title: 'ยืนยันรายละเอียดการรับสินค้าจากผู้รับจ้างภายนอก',
    intro: 'หน้าจอนี้ใช้ ConfirmOutsourcedSupplyDetail จริง และโหลดผู้รับจ้าง สินค้าขาย และตำแหน่งรับสินค้าจาก query เฉพาะงาน ไม่ต้องกรอก UUID ด้วยตนเอง',
    locale: 'ภาษา',
    date: 'วันที่รับสินค้า',
    vendor: 'ผู้รับจ้างภายนอก',
    vendorSearch: 'ค้นหาชื่อผู้รับจ้าง',
    vendorSelect: 'เลือกผู้รับจ้าง',
    product: 'สินค้าขาย',
    productSearch: 'ค้นหาชื่อสินค้า',
    productSelect: 'เลือกสินค้า',
    pricingBasis: 'เกณฑ์ราคา',
    quantity: 'ปริมาณ',
    unitPrice: 'ราคาต่อหน่วย',
    location: 'ตำแหน่งรับสินค้า',
    locationSearch: 'ค้นหาชื่อ / รหัสตำแหน่ง',
    useDefault: 'ให้เซิร์ฟเวอร์ใช้ตำแหน่งรับสินค้าหลักของสินค้า',
    locationSelect: 'เลือกตำแหน่งรับสินค้า',
    explicitLocationRequired: 'สินค้านี้ไม่มีตำแหน่งรับสินค้าหลักที่ใช้งานได้ ต้องเลือกตำแหน่งรับสินค้าให้ชัดเจนก่อนยืนยัน',
    defaultLocation: 'ตำแหน่งหลักที่ใช้งานได้',
    loading: 'กำลังโหลดตัวเลือก…',
    queryFailed: 'โหลดตัวเลือกไม่สำเร็จ',
    submit: 'ยืนยันรายละเอียด',
    submitting: 'กำลังยืนยัน…',
    validation: 'โปรดตรวจสอบข้อมูล',
    quantityPositive: 'ตาม OUT-002 ปริมาณต้องมากกว่า 0',
    priceNonNegative: 'ราคาต่อหน่วยต้องไม่ติดลบ',
    vendorRequired: 'โปรดเลือกผู้รับจ้าง',
    productRequired: 'โปรดเลือกสินค้าขาย',
    locationRequired: 'โปรดเลือกตำแหน่งรับสินค้าให้ชัดเจน',
    batchClosed: 'ล็อตจัดหาภายนอกนี้ปิดแล้ว และตามกฎที่ยังรอยืนยันยังไม่อนุญาตให้เพิ่มรายการย้อนหลังแบบปกติ',
    idempotencyConflict: 'รหัสการทำงานนี้ถูกใช้กับคำขออื่นแล้ว โปรดแก้ข้อมูลหรือเริ่มการยืนยันครั้งใหม่',
    success: 'ยืนยันรายละเอียดแล้ว',
    detailId: 'รายละเอียดการจัดหาภายนอก',
    batchId: 'ล็อตจัดหาภายนอก',
    inventoryOperationId: 'รายการสินค้าคงคลัง',
    payableId: 'เจ้าหนี้',
    amount: 'จำนวนเงิน (บาท)',
    receiptLocation: 'ตำแหน่งรับสินค้า',
    rowVersion: 'รุ่นข้อมูล',
    unexpected: 'เกิดข้อผิดพลาดที่ไม่คาดคิด',
  },
} as const;

export function OutsourcedSupplyDetailPage() {
  const { locale, setLocale } = useOperationalLocale();
  const [vendorSearch, setVendorSearch] = useState('');
  const [productSearch, setProductSearch] = useState('');
  const [locationSearch, setLocationSearch] = useState('');
  const [selectedVendor, setSelectedVendor] = useState<OutsourcedVendorOption | null>(null);
  const [selectedProduct, setSelectedProduct] = useState<OutsourcedSalesProductOption | null>(null);
  const [selectedLocation, setSelectedLocation] = useState<OutsourcedReceiptStorageLocationOption | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const submissionIdentity = useRef<SubmissionIdentity | null>(null);
  const labels = copy[locale];

  const deferredVendorSearch = useDeferredValue(vendorSearch);
  const deferredProductSearch = useDeferredValue(productSearch);
  const deferredLocationSearch = useDeferredValue(locationSearch);

  const vendorQuery = useQuery({
    queryKey: ['outsourced-supply-detail-options', 'vendors', locale, deferredVendorSearch],
    queryFn: ({ signal }) =>
      listOutsourcedVendorOptions({
        locale,
        search: deferredVendorSearch,
        limit: 50,
        signal,
      }),
    staleTime: 30_000,
  });

  const productQuery = useQuery({
    queryKey: ['outsourced-supply-detail-options', 'products', locale, deferredProductSearch],
    queryFn: ({ signal }) =>
      listOutsourcedSalesProductOptions({
        locale,
        search: deferredProductSearch,
        limit: 50,
        signal,
      }),
    staleTime: 30_000,
  });

  const locationQuery = useQuery({
    queryKey: [
      'outsourced-supply-detail-options',
      'storage-locations',
      selectedProduct?.id ?? null,
      locale,
      deferredLocationSearch,
    ],
    queryFn: ({ signal }) =>
      listOutsourcedReceiptStorageLocationOptions(selectedProduct!.id, {
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
      confirmOutsourcedSupplyDetail(request, {
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
      case 'outsourced.receipt-location-required':
        return labels.locationRequired;
      case 'outsourced.batch-closed-late-detail-unverified':
        return labels.batchClosed;
      case 'outsourced.quantity-zero-unverified':
        return labels.quantityPositive;
      case 'idempotency.key-reused':
        return labels.idempotencyConflict;
      default:
        return `${apiProblem.code}${apiProblem.problem.traceId ? ` · ${apiProblem.problem.traceId}` : ''}`;
    }
  }, [apiProblem, labels, mutation.error]);

  const optionError = firstOptionError(vendorQuery.error, productQuery.error, locationQuery.error);
  const optionErrorMessage = optionError
    ? optionError instanceof ApiProblemError
      ? `${labels.queryFailed}: ${optionError.code}`
      : labels.queryFailed
    : null;

  const vendorItems = includeSelected(vendorQuery.data?.items ?? [], selectedVendor);
  const productItems = includeSelected(productQuery.data?.items ?? [], selectedProduct);
  const locationItems = includeSelected(locationQuery.data?.items ?? [], selectedLocation);
  const applicableDefault = locationQuery.data?.items.find((item) => item.isProductDefault) ?? null;
  const requiresExplicitLocation =
    selectedProduct !== null
    && locationQuery.isSuccess
    && locationQuery.data.defaultStorageLocationId === null;

  function handleLocaleChange(next: OperationalLocale) {
    setLocale(next);
    setVendorSearch('');
    setProductSearch('');
    setLocationSearch('');
    setSelectedVendor(null);
    setSelectedProduct(null);
    setSelectedLocation(null);
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
      if (selectedVendor === null) {
        throw new Error(labels.vendorRequired);
      }

      if (selectedProduct === null) {
        throw new Error(labels.productRequired);
      }

      if (requiresExplicitLocation && selectedLocation === null) {
        throw new Error(labels.locationRequired);
      }

      const quantity = requiredNumber(formData, 'quantity');
      const unitPrice = requiredNumber(formData, 'unitPrice');

      if (quantity <= 0) {
        throw new Error(labels.quantityPositive);
      }

      if (unitPrice < 0) {
        throw new Error(labels.priceNonNegative);
      }

      const request: ConfirmOutsourcedSupplyDetailRequest = {
        supplyDate: requiredText(formData, 'supplyDate'),
        outsourcedVendorId: selectedVendor.id,
        salesProductId: selectedProduct.id,
        quantity,
        unitPrice,
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
    || selectedVendor === null
    || selectedProduct === null
    || locationQuery.isPending
    || locationQuery.isError;

  return (
    <section className="procurement-page" aria-labelledby="outsourced-supply-detail-title">
      <header className="page-header">
        <div>
          <p className="eyebrow">{labels.eyebrow}</p>
          <h1 id="outsourced-supply-detail-title">{labels.title}</h1>
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
              <input type="date" name="supplyDate" required />
            </label>

            <div className="option-picker full-width">
              <span className="field-label">{labels.vendor}</span>
              <input
                value={vendorSearch}
                onChange={(event) => setVendorSearch(event.target.value)}
                aria-label={labels.vendorSearch}
              />
              <select
                value={selectedVendor?.id ?? ''}
                onChange={(event) =>
                  setSelectedVendor(vendorItems.find((item) => item.id === event.target.value) ?? null)
                }
                required
              >
                <option value="">{vendorQuery.isPending ? labels.loading : labels.vendorSelect}</option>
                {vendorItems.map((item) => (
                  <option key={item.id} value={item.id}>{item.displayName}</option>
                ))}
              </select>
            </div>

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
                    {item.displayName} · {labels.pricingBasis}: {pricingBasisLabel(item.pricingBasis, locale)}
                  </option>
                ))}
              </select>
            </div>

            <label>
              <span>{labels.quantity}</span>
              <input type="number" name="quantity" min="0" step="any" inputMode="decimal" required />
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
                <ResultRow label={labels.detailId} value={mutation.data.outsourcedSupplyDetailId} />
                <ResultRow label={labels.batchId} value={mutation.data.outsourcedSupplyBatchId} />
                <ResultRow label={labels.inventoryOperationId} value={mutation.data.inventoryOperationId} />
                <ResultRow label={labels.payableId} value={mutation.data.payableId} />
                <ResultRow label={labels.amount} value={new Intl.NumberFormat(locale).format(mutation.data.amountThb)} />
                <ResultRow label={labels.receiptLocation} value={mutation.data.receiptStorageLocationId} />
                <ResultRow label={labels.rowVersion} value={String(mutation.data.outsourcedSupplyDetailRowVersion)} />
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

function pricingBasisLabel(basis: string, locale: 'zh-TW' | 'th-TH'): string {
  if (basis === 'WEIGHT_BASED_UNIT') return locale === 'zh-TW' ? '依重量計價' : 'คิดราคาตามน้ำหนัก';
  if (basis === 'UNIT_BASED') return locale === 'zh-TW' ? '依件數計價' : 'คิดราคาต่อหน่วย';
  return basis;
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
