import type { OperationalLocale } from '../i18n/locale';

export type ModuleKey =
  | 'procurement'
  | 'outsourced'
  | 'processing'
  | 'sales'
  | 'sales-handling'
  | 'labor'
  | 'finance'
  | 'inventory'
  | 'party'
  | 'infrastructure'
  | 'product'
  | 'data-protection';

export type ModuleArea = 'operations' | 'controls';
export type ModuleWebState = 'operational' | 'skeleton';

export interface SystemModuleDefinition {
  key: ModuleKey;
  route: string;
  area: ModuleArea;
  webState: ModuleWebState;
  label: Record<OperationalLocale, string>;
}

export const systemModules: readonly SystemModuleDefinition[] = [
  {
    key: 'procurement',
    route: '/procurement/entries/new',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '採購', 'th-TH': 'จัดซื้อ' },
  },
  {
    key: 'outsourced',
    route: '/outsourced/supply-details/new',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '委外供應', 'th-TH': 'จัดหาภายนอก' },
  },
  {
    key: 'processing',
    route: '/processing/executions/new',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '加工', 'th-TH': 'แปรรูป' },
  },
  {
    key: 'sales',
    route: '/sales',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '銷售', 'th-TH': 'การขาย' },
  },
  {
    key: 'sales-handling',
    route: '/sales-handling',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '銷售包裝作業', 'th-TH': 'งานบรรจุขาย' },
  },
  {
    key: 'labor',
    route: '/labor',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '工資', 'th-TH': 'ค่าจ้าง' },
  },
  {
    key: 'finance',
    route: '/finance',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '財務', 'th-TH': 'การเงิน' },
  },
  {
    key: 'inventory',
    route: '/inventory',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '庫存', 'th-TH': 'สินค้าคงคลัง' },
  },
  {
    key: 'party',
    route: '/party',
    area: 'controls',
    webState: 'operational',
    label: { 'zh-TW': '人員與往來對象', 'th-TH': 'บุคลากรและคู่ค้า' },
  },
  {
    key: 'infrastructure',
    route: '/infrastructure',
    area: 'controls',
    webState: 'operational',
    label: { 'zh-TW': '基礎設施資料', 'th-TH': 'ข้อมูลโครงสร้างพื้นฐาน' },
  },
  {
    key: 'product',
    route: '/product',
    area: 'controls',
    webState: 'operational',
    label: { 'zh-TW': '產品設定', 'th-TH': 'การตั้งค่าสินค้า' },
  },
  {
    key: 'data-protection',
    route: '/data-protection',
    area: 'controls',
    webState: 'skeleton',
    label: { 'zh-TW': '資料保護控制', 'th-TH': 'การควบคุมการคุ้มครองข้อมูล' },
  },
];

export const skeletonModules = systemModules.filter(
  (module): module is SystemModuleDefinition & { webState: 'skeleton' } => module.webState === 'skeleton',
);

export function getSystemModule(moduleKey: ModuleKey): SystemModuleDefinition {
  const module = systemModules.find((candidate) => candidate.key === moduleKey);

  if (module === undefined) {
    throw new Error(`Unknown ERP module: ${moduleKey}`);
  }

  return module;
}
