import { NavLink, Outlet } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { useOperationalLocale } from '../i18n/locale';
import { applicationShellCopy, primaryNavigation } from '../navigation';

export function MobileAppShell() {
  const { locale } = useOperationalLocale();
  const shellCopy = applicationShellCopy[locale];

  return (
    <div className="application-frame mobile-application-frame" data-ui-experience="mobile">
      <header className="mobile-topbar">
        <div className="brand-lockup">
          <span className="brand-mark">YowThi</span>
          <span className="brand-subtitle">ERP V2 · {shellCopy.mobileSuffix}</span>
        </div>
      </header>

      <main className="app-shell mobile-app-shell">
        <Outlet />
      </main>

      <nav className="mobile-bottom-navigation" aria-label={shellCopy.primaryNavigation}>
        {primaryNavigation.map((item) => (
          <NavLink
            key={item.to}
            to={withDeviceExperienceOverride(item.to)}
            className={({ isActive }) => (isActive ? 'active' : undefined)}
          >
            {item.compactLabel[locale]}
          </NavLink>
        ))}
      </nav>
    </div>
  );
}
