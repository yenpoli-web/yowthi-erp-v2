import { NavLink } from 'react-router';
import type { OperationalLocale } from '../i18n/locale';
import './ModuleSubnav.css';

export interface ModuleSubnavItem {
  to: string;
  label: Record<OperationalLocale, string>;
  end?: boolean;
}

export function ModuleSubnav({ locale, items, ariaLabel }: { locale: OperationalLocale; items: readonly ModuleSubnavItem[]; ariaLabel: string }) {
  return <nav className="module-subnav" aria-label={ariaLabel}>
    {items.map((item) => <NavLink key={item.to} to={item.to} end={item.end} className={({ isActive }) => isActive ? 'is-active' : undefined}>{item.label[locale]}</NavLink>)}
  </nav>;
}
