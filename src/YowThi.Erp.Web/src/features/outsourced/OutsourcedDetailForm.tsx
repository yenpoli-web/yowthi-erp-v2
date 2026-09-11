import { useMutation, useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import { SearchableSelect } from '../../app/forms/SearchableSelect';
import { useOperationalLocale } from '../../app/i18n/locale';
import {
  ApiProblemError,
  confirmOutsourcedSupplyDetail,
  type ConfirmOutsourcedSupplyDetailResult,
} from './confirmOutsourcedSupplyDetail';
import {
  listOutsourcedReceiptStorageLocationOptions,
  listOutsourcedSalesProductOptions,
  type OutsourcedReceiptStorageLocationOption,
  type OutsourcedSalesProductOption,
} from './outsourcedSupplyDetailOptions';
import './OutsourcedDetailForm.css';

interface OutsourcedDetailHeader {
  supplyDate: string;
  outsourcedVendorId: string;
  outsourcedVendorDisplayName: string;
}

interface OutsourcedDetailFormProps {
  header: OutsourcedDetailHeader;
  onSaved: (result: ConfirmOutsourcedSupplyDetailResult) => void | Promise<void>;
  onCancel: () => void;
}

const copy = {
  'zh-TW': {
    title: '新增明細', header: '主單', date: '日期', vendor: '委外商', product: '產品', productSearch: '搜尋銷售產品', productSelect: '選擇銷售產品',
    pricing: '計價', quantity: '數量', unitPrice: '單價', location: '收貨儲位', locationSearch: '搜尋儲位', locationSelect: '選擇收貨儲位',
    loading: '載入中', save: '儲存並新增下一筆', cancel: '結束新增', defaultLocation: '產品預設', invalid: '請完整輸入產品、數量、單價與必要的收貨儲位。', failed: '委外明細儲存失敗。',
    weightBased: '依重量計價', unitBased: '依件數計價',
  },
  'th-TH': {
    title: 'เพิ่มรายละเอียด', header: 'เอกสารหลัก', date: 'วันที่', vendor: 'ผู้รับจ้างภายนอก', product: 'สินค้า', productSearch: 'ค้นหาสินค้าขาย', productSelect: 'เลือกสินค้าขาย',
    pricing: 'เกณฑ์ราคา', quantity: 'ปริมาณ', unitPrice: 'ราคาต่อหน่วย', location: 'ตำแหน่งรับสินค้า', locationSearch: 'ค้นหาตำแหน่ง', locationSelect: 'เลือกตำแหน่งรับสินค้า',
    loading: 'กำลังโหลด', save: 'บันทึกและเพิ่มรายการถัดไป', cancel: 'จบการเพิ่ม', defaultLocation: 'ค่าเริ่มต้นสินค้า', invalid: 'กรุณากรอกสินค้า ปริมาณ ราคา และตำแหน่งรับสินค้าที่จำเป็นให้ครบถ้วน', failed: 'บันทึกรายละเอียดงานภายนอกไม่สำเร็จ',
    weightBased: 'คิดราคาตามน้ำหนัก', unitBased: 'คิดราคาต่อหน่วย',
  },
} as const;

export function OutsourcedDetailForm({ header, onSaved, onCancel }: OutsourcedDetailFormProps) {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const [productSearch, setProductSearch] = useState('');
  const [selectedProduct, setSelectedProduct] = useState<OutsourcedSalesProductOption | null>(null);
  const [locationSearch, setLocationSearch] = useState('');
  const [selectedLocation, setSelectedLocation] = useState<OutsourcedReceiptStorageLocationOption | null>(null);
  const [quantity, setQuantity] = useState('');
  const [unitPrice, setUnitPrice] = useState('');
  const [localError, setLocalError] = useState<string | null>(null);

  const productQuery = useQuery({
    queryKey: ['outsourced-detail', 'products', locale, productSearch],
    queryFn: ({ signal }) => listOutsourcedSalesProductOptions({ locale, search: productSearch, limit: 50, signal }),
    staleTime: 10_000,
  });

  const locationQuery = useQuery({
    queryKey: ['outsourced-detail', 'locations', locale, selectedProduct?.id ?? null, locationSearch],
    queryFn: ({ signal }) => listOutsourcedReceiptStorageLocationOptions(selectedProduct!.id, { locale, search: locationSearch, limit: 50, signal }),
    enabled: selectedProduct !== null,
    staleTime: 10_000,
  });

  const productItems = includeSelected(productQuery.data?.items ?? [], selectedProduct);
  const locationItems = includeSelected(locationQuery.data?.items ?? [], selectedLocation);
  const defaultLocation = locationQuery.data?.items.find((item) => item.isProductDefault) ?? null;
  const requiresExplicitLocation = selectedProduct !== null && locationQuery.isSuccess && locationQuery.data.defaultStorageLocationId === null;

  const mutation = useMutation({
    mutationFn: async () => {
      const parsedQuantity = Number(quantity);
      const parsedUnitPrice = Number(unitPrice);
      if (
        selectedProduct === null
        || !Number.isFinite(parsedQuantity)
        || parsedQuantity <= 0
        || !Number.isFinite(parsedUnitPrice)
        || parsedUnitPrice < 0
        || (requiresExplicitLocation && selectedLocation === null)
      ) {
        throw new LocalValidationError();
      }

      return confirmOutsourcedSupplyDetail({
        supplyDate: header.supplyDate,
        outsourcedVendorId: header.outsourcedVendorId,
        salesProductId: selectedProduct.id,
        quantity: parsedQuantity,
        unitPrice: parsedUnitPrice,
        receiptStorageLocationId: selectedLocation?.id ?? null,
      }, {
        idempotencyKey: crypto.randomUUID(),
        locale,
      });
    },
    onSuccess: async (result) => {
      setLocalError(null);
      await onSaved(result);
      resetForNextRow();
    },
    onError: (error) => {
      if (error instanceof LocalValidationError) {
        setLocalError(labels.invalid);
        return;
      }
      if (error instanceof ApiProblemError) {
        setLocalError(error.problem.title || labels.failed);
        return;
      }
      setLocalError(labels.failed);
    },
  });

  function changeProduct(id: string) {
    setSelectedProduct(productItems.find((item) => item.id === id) ?? null);
    setSelectedLocation(null);
    setLocationSearch('');
    setLocalError(null);
  }

  function resetForNextRow() {
    setSelectedProduct(null);
    setProductSearch('');
    setSelectedLocation(null);
    setLocationSearch('');
    setQuantity('');
    setUnitPrice('');
  }

  return (
    <section className="outsourced-detail-form" aria-labelledby="outsourced-detail-form-title">
      <header className="outsourced-detail-form-header">
        <div><span>{labels.header}</span><h3 id="outsourced-detail-form-title">{labels.title}</h3></div>
        <div className="outsourced-detail-header-context">
          <span>{labels.date}: <strong>{formatDate(header.supplyDate, locale)}</strong></span>
          <span>{labels.vendor}: <strong>{header.outsourcedVendorDisplayName}</strong></span>
        </div>
      </header>

      <div className="outsourced-detail-primary-row">
        <SearchableSelect
          label={labels.product}
          value={selectedProduct?.id ?? ''}
          options={productItems.map((item) => ({ id: item.id, label: item.displayName, secondaryLabel: pricingLabel(item.pricingBasis, labels) }))}
          onChange={changeProduct}
          searchValue={productSearch}
          onSearchChange={setProductSearch}
          searchLabel={labels.productSearch}
          chooseLabel={labels.productSelect}
          loadingLabel={labels.loading}
          loading={productQuery.isPending}
        />

        <div className="outsourced-readonly-field outsourced-pricing-field">
          <span>{labels.pricing}</span>
          <strong>{selectedProduct ? pricingLabel(selectedProduct.pricingBasis, labels) : '—'}</strong>
        </div>

        <label className="outsourced-quantity-field">
          <span>{labels.quantity}</span>
          <input type="number" min="0" step="any" inputMode="decimal" value={quantity} onChange={(event) => { setQuantity(event.target.value); setLocalError(null); }} />
        </label>

        <label className="outsourced-unit-price-field">
          <span>{labels.unitPrice}</span>
          <input type="number" min="0" step="any" inputMode="decimal" value={unitPrice} onChange={(event) => { setUnitPrice(event.target.value); setLocalError(null); }} />
        </label>

        <SearchableSelect
          label={labels.location}
          value={selectedLocation?.id ?? ''}
          options={locationItems.map((item) => ({ id: item.id, label: item.displayName, secondaryLabel: item.code }))}
          onChange={(id) => { setSelectedLocation(locationItems.find((item) => item.id === id) ?? null); setLocalError(null); }}
          searchValue={locationSearch}
          onSearchChange={setLocationSearch}
          searchLabel={labels.locationSearch}
          chooseLabel={labels.locationSelect}
          loadingLabel={labels.loading}
          emptyOptionLabel={defaultLocation ? `${labels.defaultLocation} · ${defaultLocation.displayName}` : undefined}
          disabled={selectedProduct === null}
          loading={locationQuery.isPending}
        />
      </div>

      <div className="outsourced-detail-form-actions">
        <button type="button" className="outsourced-secondary-action" onClick={onCancel} disabled={mutation.isPending}>{labels.cancel}</button>
        <button type="button" className="outsourced-primary-action" onClick={() => mutation.mutate()} disabled={mutation.isPending || selectedProduct === null || locationQuery.isPending || locationQuery.isError}>{mutation.isPending ? labels.loading : labels.save}</button>
      </div>

      {localError && <div className="problem-banner" role="alert">{localError}</div>}
      {(productQuery.isError || locationQuery.isError) && <div className="problem-banner" role="alert">{labels.failed}</div>}
    </section>
  );
}

function includeSelected<T extends { id: string }>(items: readonly T[], selected: T | null): T[] {
  return selected === null || items.some((item) => item.id === selected.id) ? [...items] : [selected, ...items];
}

function pricingLabel(value: string, labels: (typeof copy)[keyof typeof copy]): string {
  if (value === 'WEIGHT_BASED_UNIT') return labels.weightBased;
  if (value === 'UNIT_BASED') return labels.unitBased;
  return value;
}

function formatDate(value: string, locale: 'zh-TW' | 'th-TH'): string {
  return new Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(`${value}T00:00:00`));
}

class LocalValidationError extends Error {}
