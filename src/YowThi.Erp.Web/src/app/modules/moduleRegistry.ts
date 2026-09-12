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
  | 'security'
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
    route: '/procurement',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '採購管理', 'th-TH': 'จัดซื้อ' },
  },
  {
    key: 'outsourced',
    route: '/outsourced',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '委外加工', 'th-TH': 'จัดหาภายนอก' },
  },
  {
    key: 'processing',
    route: '/processing',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '生產加工', 'th-TH': 'แปรรูป' },
  },
  {
    key: 'sales',
    route: '/sales',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '銷售管理', 'th-TH': 'การขาย' },
  },
  {
    key: 'sales-handling',
    route: '/sales-handling',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '銷售作業', 'th-TH': 'งานบรรจุขาย' },
  },
  {
    key: 'labor',
    route: '/labor',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '勞務工資', 'th-TH': 'ค่าจ้าง' },
  },
  {
    key: 'finance',
    route: '/finance',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '財務管理', 'th-TH': 'การเงิน' },
  },
  {
    key: 'inventory',
    route: '/inventory',
    area: 'operations',
    webState: 'operational',
    label: { 'zh-TW': '庫存管理', 'th-TH': 'สินค้าคงคลัง' },
  },
  {
    key: 'party',
    route: '/party',
    area: 'controls',
    webState: 'operational',
    label: { 'zh-TW': '夥伴管理', 'th-TH': 'บุคลากรและคู่ค้า' },
  },
  {
    key: 'infrastructure',
    route: '/infrastructure',
    area: 'controls',
    webState: 'operational',
    label: { 'zh-TW': '基礎資料', 'th-TH': 'ข้อมูลโครงสร้างพื้นฐาน' },
  },
  {
    key: 'product',
    route: '/product',
    area: 'controls',
    webState: 'operational',
    label: { 'zh-TW': '產品管理', 'th-TH': 'การตั้งค่าสินค้า' },
  },
  {
    key: 'security',
    route: '/security/accounts',
    area: 'controls',
    webState: 'operational',
    label: { 'zh-TW': '帳號與權限', 'th-TH': 'บัญชีและสิทธิ์' },
  },
  {
    key: 'data-protection',
    route: '/data-protection',
    area: 'controls',
    webState: 'operational',
    label: { 'zh-TW': '資料保護', 'th-TH': 'การควบคุมการคุ้มครองข้อมูล' },
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
