import { Link } from 'react-router';

import { useOperationalLocale } from '../../app/i18n/locale';
import { partyLifecycleCopy } from './partyLifecycleCopy';
import type { PartyLifecycleKind } from './partyLifecycleOptions';

type PartyMasterNavigationProps = {
  activeKind: PartyLifecycleKind;
};

const targets: readonly { kind: PartyLifecycleKind; route: string }[] = [
  { kind: 'suppliers', route: '/party' },
  { kind: 'customers', route: '/party/customers' },
  { kind: 'outsourced-vendors', route: '/party/lifecycle?kind=outsourced-vendors' },
  { kind: 'farmers', route: '/party/lifecycle?kind=farmers' },
  { kind: 'employees', route: '/party/lifecycle?kind=employees' },
];

export function PartyMasterNavigation({ activeKind }: PartyMasterNavigationProps) {
  const { locale } = useOperationalLocale();
  const labels = partyLifecycleCopy[locale];

  const labelFor = (kind: PartyLifecycleKind) => {
    switch (kind) {
      case 'suppliers': return labels.suppliers;
      case 'customers': return labels.customers;
      case 'outsourced-vendors': return labels.outsourcedVendors;
      case 'farmers': return labels.farmers;
      case 'employees': return labels.employees;
    }
  };

  return (
    <nav className="party-master-kind-switcher" aria-label={labels.title}>
      {targets.map((target) => target.kind === activeKind
        ? <span key={target.kind} className="is-active">{labelFor(target.kind)}</span>
        : <Link key={target.kind} to={target.route}>{labelFor(target.kind)}</Link>)}
    </nav>
  );
}
