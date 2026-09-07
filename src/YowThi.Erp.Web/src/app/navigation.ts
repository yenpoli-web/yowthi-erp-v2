import type { OperationalLocale } from './i18n/locale';
import {
  systemModules,
  type ModuleArea,
  type ModuleKey,
  type SystemModuleDefinition,
} from './modules/moduleRegistry';

type LocalizedLabel = Record<OperationalLocale, string>;

export type NavigationGroup = {
  area: ModuleArea;
  modules: readonly SystemModuleDefinition[];
};

export type QuickNavigationItem = {
  to: string;
  icon: ModuleKey | 'home';
  label: LocalizedLabel;
};

export const applicationShellCopy: Record<
  OperationalLocale,
  {
    primaryNavigation: string;
    operations: string;
    controls: string;
    workspace: string;
    moduleMenu: string;
    closeMenu: string;
    systemReady: string;
    tabletSuffix: string;
    mobileSuffix: string;
  }
> = {
  'zh-TW': {
    primaryNavigation: '主要導覽',
    operations: '日常作業',
    controls: '資料與控制',
    workspace: '工作台',
    moduleMenu: '全部模組',
    closeMenu: '關閉模組選單',
    systemReady: '12 個模組可使用',
    tabletSuffix: '平板工作台',
    mobileSuffix: '行動工作台',
  },
  'th-TH': {
    primaryNavigation: 'เมนูหลัก',
    operations: 'งานประจำวัน',
    controls: 'ข้อมูลและการควบคุม',
    workspace: 'พื้นที่ทำงาน',
    moduleMenu: 'โมดูลทั้งหมด',
    closeMenu: 'ปิดเมนูโมดูล',
    systemReady: 'พร้อมใช้งาน 12 โมดูล',
    tabletSuffix: 'พื้นที่ทำงานแท็บเล็ต',
    mobileSuffix: 'พื้นที่ทำงานมือถือ',
  },
};

export const navigationGroups: readonly NavigationGroup[] = (
  ['operations', 'controls'] as const
).map((area) => ({
  area,
  modules: systemModules.filter((module) => module.area === area),
}));

export const mobileQuickNavigation: readonly QuickNavigationItem[] = [
  {
    to: '/modules',
    icon: 'home',
    label: { 'zh-TW': '首頁', 'th-TH': 'หน้าแรก' },
  },
  {
    to: '/procurement/entries/new',
    icon: 'procurement',
    label: { 'zh-TW': '採購', 'th-TH': 'จัดซื้อ' },
  },
  {
    to: '/processing/executions/new',
    icon: 'processing',
    label: { 'zh-TW': '加工', 'th-TH': 'แปรรูป' },
  },
  {
    to: '/sales',
    icon: 'sales',
    label: { 'zh-TW': '銷售', 'th-TH': 'การขาย' },
  },
];
