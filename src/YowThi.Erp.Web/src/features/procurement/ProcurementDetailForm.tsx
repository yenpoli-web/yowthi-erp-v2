import { useMutation, useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';

import { SearchableSelect } from '../../app/forms/SearchableSelect';
import { useOperationalLocale } from '../../app/i18n/locale';
import {
  ApiProblemError,
  confirmProcurementEntry,
  type ConfirmProcurementEntryResult,
  type ProcurementSourceType,
} from './confirmProcurementEntry';
import {
  listProcurementSourceOptions,
  type ProcurementSourceOption,
} from './procurementEntryOptions';
import './ProcurementDetailForm.css';

interface ProcurementDetailHeader {
  procurementDate: string;
  procurementProductId: string;
  procurementProductDisplayName: string;
  unitCode: string;
}

interface ProcurementDetailFormProps {
  header: ProcurementDetailHeader;
  onSaved: (result: ConfirmProcurementEntryResult) => void | Promise<void>;
  onCancel: () => void;
}

const copy = {
  'zh-TW': {
    title: '新增明細',
    header: '主單',
    date: '日期',
    product: '產品',
    code: '編號',
    sourceType: '供應類型',
    supplier: '供應商',
    farmer: '農戶',
    source: '供應商／來源',
    sourceSearch: '搜尋編號或供應來源',
    sourceSelect: '選擇供應來源',
    quantity: '數量',
    unitPrice: '單價',
    amount: '金額',
    companyPickup: '公司取貨',
    loading: '載入中',
    save: '儲存並新增下一筆',
    cancel: '結束新增',
    invalid: '請完整輸入供應來源、數量與單價。',
    failed: '採購明細儲存失敗。',
  },
  'th-TH': {
    title: 'เพิ่มรายการ',
    header: 'เอกสารหลัก',
    date: 'วันที่',
    product: 'สินค้า',
    code: 'รหัส',
    sourceType: 'ประเภทแหล่งที่มา',
    supplier: 'ผู้จำหน่าย',
    farmer: 'เกษตรกร',
    source: 'ผู้จำหน่าย / แหล่งที่มา',
    sourceSearch: 'ค้นหารหัสหรือแหล่งจัดซื้อ',
    sourceSelect: 'เลือกแหล่งจัดซื้อ',
    quantity: 'ปริมาณ',
    unitPrice: 'ราคาต่อหน่วย',
    amount: 'จำนวนเงิน',
    companyPickup: 'บริษัทรับสินค้า',
    loading: 'กำลังโหลด',
    save: 'บันทึกและเพิ่มรายการถัดไป',
    cancel: 'จบการเพิ่มรายการ',
    invalid: 'กรุณากรอกแหล่งจัดซื้อ ปริมาณ และราคาให้ครบถ้วน',
    failed: 'บันทึกรายการจัดซื้อไม่สำเร็จ',
  },
} as const;

export function ProcurementDetailForm({ header, onSaved, onCancel }: ProcurementDetailFormProps) {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const [sourceType, setSourceType] = useState<ProcurementSourceType>('SUPPLIER');
  const [sourceSearch, setSourceSearch] = useState('');
  const [selectedSource, setSelectedSource] = useState<ProcurementSourceOption | null>(null);
  const [quantity, setQuantity] = useState('');
  const [unitPrice, setUnitPrice] = useState('');
  const [companyPickup, setCompanyPickup] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);

  const sourceQuery = useQuery({
    queryKey: ['procurement-detail', 'sources', locale, sourceType, sourceSearch],
    queryFn: ({ signal }) => listProcurementSourceOptions(sourceType, {
      locale,
      search: sourceSearch,
      limit: 50,
      signal,
    }),
    staleTime: 10_000,
  });

  const sourceItems = includeSelected(sourceQuery.data?.items ?? [], selectedSource);
  const amountThb = useMemo(() => calculateAmount(quantity, unitPrice), [quantity, unitPrice]);

  const mutation = useMutation({
    mutationFn: async () => {
      const netQuantity = Number(quantity);
      const price = Number(unitPrice);
      if (
        selectedSource === null
        || !Number.isFinite(netQuantity)
        || netQuantity <= 0
        || !Number.isFinite(price)
        || price < 0
      ) {
        throw new LocalValidationError();
      }

      return confirmProcurementEntry({
        procurementDate: header.procurementDate,
        procurementProductId: header.procurementProductId,
        sourceType,
        supplierId: sourceType === 'SUPPLIER' ? selectedSource.id : null,
        farmerId: sourceType === 'FARMER' ? selectedSource.id : null,
        netQuantity,
        unitPrice: price,
        companyPickup,
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

  function changeSourceType(next: ProcurementSourceType) {
    setSourceType(next);
    setSelectedSource(null);
    setSourceSearch('');
    setLocalError(null);
  }

  function resetForNextRow() {
    setSelectedSource(null);
    setSourceSearch('');
    setQuantity('');
    setUnitPrice('');
    setCompanyPickup(false);
  }

  return (
    <section className="procurement-detail-form" aria-labelledby="procurement-detail-form-title">
      <header className="procurement-detail-form-header">
        <div>
          <span>{labels.header}</span>
          <h3 id="procurement-detail-form-title">{labels.title}</h3>
        </div>
        <div className="procurement-detail-header-context">
          <span>{labels.date}: <strong>{formatDate(header.procurementDate, locale)}</strong></span>
          <span>{labels.product}: <strong>{header.procurementProductDisplayName}</strong></span>
        </div>
      </header>

      <div className="procurement-detail-primary-row">
        <div className="procurement-readonly-field procurement-source-code-field">
          <span>{labels.code}</span>
          <strong>{selectedSource?.code ?? '—'}</strong>
        </div>

        <SearchableSelect
          label={labels.source}
          value={selectedSource?.id ?? ''}
          options={sourceItems.map((item) => ({
            id: item.id,
            label: item.displayName,
            secondaryLabel: item.code ?? null,
          }))}
          onChange={(id) => {
            setSelectedSource(sourceItems.find((item) => item.id === id) ?? null);
            setLocalError(null);
          }}
          searchValue={sourceSearch}
          onSearchChange={setSourceSearch}
          searchLabel={labels.sourceSearch}
          chooseLabel={labels.sourceSelect}
          loadingLabel={labels.loading}
          loading={sourceQuery.isPending}
        />

        <label className="procurement-quantity-field">
          <span>{labels.quantity}</span>
          <div className="procurement-number-with-unit">
            <input
              type="number"
              min="0"
              step="any"
              inputMode="decimal"
              value={quantity}
              onChange={(event) => {
                setQuantity(event.target.value);
                setLocalError(null);
              }}
            />
            <span>{header.unitCode}</span>
          </div>
        </label>

        <label className="procurement-unit-price-field">
          <span>{labels.unitPrice}</span>
          <input
            type="number"
            min="0"
            step="any"
            inputMode="decimal"
            value={unitPrice}
            onChange={(event) => {
              setUnitPrice(event.target.value);
              setLocalError(null);
            }}
          />
        </label>

        <div className="procurement-readonly-field procurement-amount-field">
          <span>{labels.amount}</span>
          <strong>{amountThb === null ? '—' : formatMoney(amountThb, locale)}</strong>
        </div>
      </div>

      <div className="procurement-detail-secondary-row">
        <label>
          <span>{labels.sourceType}</span>
          <select value={sourceType} onChange={(event) => changeSourceType(event.target.value as ProcurementSourceType)}>
            <option value="SUPPLIER">{labels.supplier}</option>
            <option value="FARMER">{labels.farmer}</option>
          </select>
        </label>

        <label className="procurement-detail-pickup">
          <input type="checkbox" checked={companyPickup} onChange={(event) => setCompanyPickup(event.target.checked)} />
          <span>{labels.companyPickup}</span>
        </label>

        <div className="procurement-detail-form-actions">
          <button type="button" className="secondary-action" onClick={onCancel} disabled={mutation.isPending}>
            {labels.cancel}
          </button>
          <button type="button" className="primary-action" onClick={() => mutation.mutate()} disabled={mutation.isPending}>
            {mutation.isPending ? labels.loading : labels.save}
          </button>
        </div>
      </div>

      {localError && <div className="problem-banner" role="alert">{localError}</div>}
      {sourceQuery.isError && <div className="problem-banner" role="alert">{labels.failed}</div>}
    </section>
  );
}

function includeSelected<T extends { id: string }>(items: readonly T[], selected: T | null): T[] {
  if (selected === null || items.some((item) => item.id === selected.id)) return [...items];
  return [selected, ...items];
}

function calculateAmount(quantity: string, unitPrice: string): number | null {
  if (quantity.trim() === '' || unitPrice.trim() === '') return null;
  const q = Number(quantity);
  const p = Number(unitPrice);
  if (!Number.isFinite(q) || !Number.isFinite(p) || q < 0 || p < 0) return null;
  return Math.floor(q * p);
}

function formatMoney(value: number, locale: 'zh-TW' | 'th-TH'): string {
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency: 'THB',
    maximumFractionDigits: 0,
  }).format(value);
}

function formatDate(value: string, locale: 'zh-TW' | 'th-TH'): string {
  return new Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit' }).format(
    new Date(`${value}T00:00:00`),
  );
}

class LocalValidationError extends Error {}
