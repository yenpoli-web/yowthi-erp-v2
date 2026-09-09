import { useDeferredValue, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useOperationalLocale } from '../../app/i18n/locale';
import { ModuleSubnav, type ModuleSubnavItem } from '../../app/modules/ModuleSubnav';
import { listInventoryPositions, type InventoryObjectKind, type InventoryOrigin } from './inventoryManagement';
import './InventoryPositionPage.css';

const navItems: readonly ModuleSubnavItem[] = [
  { to: '/inventory', end: true, label: { 'zh-TW': '庫存現況', 'th-TH': 'ยอดคงเหลือ' } },
  { to: '/inventory/operations', label: { 'zh-TW': '調撥與調整', 'th-TH': 'โอนและปรับปรุง' } },
  { to: '/inventory/warehouses', label: { 'zh-TW': '倉庫', 'th-TH': 'คลัง' } },
  { to: '/inventory/storage-locations', label: { 'zh-TW': '儲位', 'th-TH': 'ตำแหน่งจัดเก็บ' } },
];

const copy = {
  'zh-TW': {
    nav: '庫存管理', eyebrow: '庫存管理', title: '庫存現況', search: '搜尋產品、倉庫、儲位或供應商',
    allOrigins: '全部來源', inHouse: '自有', outsourced: '委外', allObjects: '全部品項', procurementProduct: '採購產品', processMaterial: '加工品', salesProduct: '銷售產品',
    includeZero: '顯示零庫存', object: '品項', origin: '來源', warehouse: '倉庫', location: '儲位', source: '來源細分', balance: '庫存數量', records: '筆庫存',
    farmersCombined: '農戶合併', noSource: '—', noResult: '目前沒有符合條件的庫存位置。', loadFailed: '無法載入庫存現況。',
  },
  'th-TH': {
    nav: 'สินค้าคงคลัง', eyebrow: 'สินค้าคงคลัง', title: 'ยอดคงเหลือปัจจุบัน', search: 'ค้นหาสินค้า คลัง ตำแหน่ง หรือผู้ขาย',
    allOrigins: 'ทุกแหล่ง', inHouse: 'ภายใน', outsourced: 'ภายนอก', allObjects: 'ทุกรายการ', procurementProduct: 'สินค้าจัดซื้อ', processMaterial: 'วัสดุระหว่างผลิต', salesProduct: 'สินค้าขาย',
    includeZero: 'แสดงยอดศูนย์', object: 'รายการ', origin: 'แหล่ง', warehouse: 'คลัง', location: 'ตำแหน่ง', source: 'แหล่งย่อย', balance: 'จำนวนคงเหลือ', records: 'ตำแหน่ง',
    farmersCombined: 'รวมเกษตรกร', noSource: '—', noResult: 'ไม่พบยอดคงเหลือตามเงื่อนไข', loadFailed: 'ไม่สามารถโหลดยอดคงเหลือได้',
  },
} as const;

export function InventoryPositionPage() {
  const { locale } = useOperationalLocale();
  const labels = copy[locale];
  const [search, setSearch] = useState('');
  const deferredSearch = useDeferredValue(search);
  const [origin, setOrigin] = useState<InventoryOrigin | ''>('');
  const [objectKind, setObjectKind] = useState<InventoryObjectKind | ''>('');
  const [includeZeroBalance, setIncludeZeroBalance] = useState(false);

  const query = useQuery({
    queryKey: ['inventory-positions', locale, deferredSearch, origin, objectKind, includeZeroBalance],
    queryFn: ({ signal }) => listInventoryPositions({ locale, search: deferredSearch, origin, objectKind, includeZeroBalance, limit: 200, signal }),
    staleTime: 2_000,
  });
  const items = query.data?.items ?? [];
  const numberFormat = new Intl.NumberFormat(locale, { maximumFractionDigits: 6 });

  function objectLabel(kind: InventoryObjectKind) {
    return kind === 'PROCUREMENT_PRODUCT' ? labels.procurementProduct : kind === 'PROCESS_MATERIAL' ? labels.processMaterial : labels.salesProduct;
  }
  function originLabel(value: InventoryOrigin) { return value === 'IN_HOUSE' ? labels.inHouse : labels.outsourced; }
  function sourceLabel(rawSourceKind: string | null, supplierDisplayName: string | null) {
    if (rawSourceKind === 'SUPPLIER') return supplierDisplayName || labels.noSource;
    if (rawSourceKind === 'FARMERS_COMBINED') return labels.farmersCombined;
    return labels.noSource;
  }

  return <section className="inventory-position-page" aria-labelledby="inventory-position-title">
    <ModuleSubnav locale={locale} items={navItems} ariaLabel={labels.nav} />
    <header className="inventory-position-header"><div><p className="eyebrow">{labels.eyebrow}</p><h1 id="inventory-position-title">{labels.title}</h1></div><strong>{items.length} {labels.records}</strong></header>
    <div className="inventory-position-toolbar">
      <label className="inventory-position-search"><input type="search" value={search} aria-label={labels.search} onChange={(event) => setSearch(event.target.value)} /></label>
      <select value={origin} onChange={(event) => setOrigin(event.target.value as InventoryOrigin | '')} aria-label={labels.origin}><option value="">{labels.allOrigins}</option><option value="IN_HOUSE">{labels.inHouse}</option><option value="OUTSOURCED">{labels.outsourced}</option></select>
      <select value={objectKind} onChange={(event) => setObjectKind(event.target.value as InventoryObjectKind | '')} aria-label={labels.object}><option value="">{labels.allObjects}</option><option value="PROCUREMENT_PRODUCT">{labels.procurementProduct}</option><option value="PROCESS_MATERIAL">{labels.processMaterial}</option><option value="SALES_PRODUCT">{labels.salesProduct}</option></select>
      <label className="inventory-zero-toggle"><input type="checkbox" checked={includeZeroBalance} onChange={(event) => setIncludeZeroBalance(event.target.checked)} />{labels.includeZero}</label>
    </div>
    {query.error && <div className="problem-banner" role="alert">{labels.loadFailed}</div>}
    <div className="inventory-position-table-wrap">
      <table className="inventory-position-table"><thead><tr><th>{labels.object}</th><th>{labels.origin}</th><th>{labels.warehouse}</th><th>{labels.location}</th><th>{labels.source}</th><th className="is-number">{labels.balance}</th></tr></thead>
        <tbody>{items.map((item) => <tr key={item.id}><td data-label={labels.object}><strong>{item.objectDisplayName}</strong><small>{objectLabel(item.objectKind)}</small></td><td data-label={labels.origin}>{originLabel(item.origin)}</td><td data-label={labels.warehouse}>{item.warehouseDisplayName}</td><td data-label={labels.location}>{item.storageLocationDisplayName}</td><td data-label={labels.source}>{sourceLabel(item.rawSourceKind, item.supplierDisplayName)}</td><td data-label={labels.balance} className="is-number"><strong>{numberFormat.format(item.balanceQuantity)}</strong></td></tr>)}</tbody>
      </table>
      {!query.isPending && items.length === 0 && <p className="inventory-position-empty">{labels.noResult}</p>}
    </div>
  </section>;
}
