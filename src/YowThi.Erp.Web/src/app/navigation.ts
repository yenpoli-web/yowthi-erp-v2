export type PrimaryNavigationItem = {
  to: string;
  label: string;
  compactLabel: string;
};

export const primaryNavigation: readonly PrimaryNavigationItem[] = [
  {
    to: '/procurement/entries/new',
    label: 'Procurement',
    compactLabel: 'Procure',
  },
  {
    to: '/outsourced/supply-details/new',
    label: 'Outsourced',
    compactLabel: 'Outsource',
  },
  {
    to: '/processing/executions/new',
    label: 'Processing',
    compactLabel: 'Process',
  },
];
