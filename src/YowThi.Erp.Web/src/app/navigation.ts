import type { OperationalLocale } from './i18n/locale';

type LocalizedLabel = Record<OperationalLocale, string>;

export type PrimaryNavigationItem = {
  to: string;
  label: LocalizedLabel;
  compactLabel: LocalizedLabel;
};

export const applicationShellCopy: Record<
  OperationalLocale,
  {
    primaryNavigation: string;
    tabletSuffix: string;
    mobileSuffix: string;
  }
> = {
  'zh-TW': {
    primaryNavigation: '主要導覽',
    tabletSuffix: '平板',
    mobileSuffix: '手機',
  },
  'th-TH': {
    primaryNavigation: 'เมนูหลัก',
    tabletSuffix: 'แท็บเล็ต',
    mobileSuffix: 'มือถือ',
  },
};

export const primaryNavigation: readonly PrimaryNavigationItem[] = [
  {
    to: '/procurement/entries/new',
    label: {
      'zh-TW': '採購',
      'th-TH': 'จัดซื้อ',
    },
    compactLabel: {
      'zh-TW': '採購',
      'th-TH': 'จัดซื้อ',
    },
  },
  {
    to: '/outsourced/supply-details/new',
    label: {
      'zh-TW': '委外供應',
      'th-TH': 'จัดหาภายนอก',
    },
    compactLabel: {
      'zh-TW': '委外',
      'th-TH': 'ภายนอก',
    },
  },
  {
    to: '/processing/executions/new',
    label: {
      'zh-TW': '加工',
      'th-TH': 'แปรรูป',
    },
    compactLabel: {
      'zh-TW': '加工',
      'th-TH': 'แปรรูป',
    },
  },
];
