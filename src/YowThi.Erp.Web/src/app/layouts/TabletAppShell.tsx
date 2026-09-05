import { NavLink, Outlet } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { useOperationalLocale } from '../i18n/locale';
import { applicationShellCopy, primaryNavigation } from '../navigation';

export function TabletAppShell() {
  const { locale } = useOperationalLocale();
  const shellCopy = applicationShellCopy[locale];

  return (
    <div className="application-frame tablet-application-frame" data-ui-experience="tablet">
      <header className="tablet-topbar">
        <div className="tablet-brand-row">
          <div className="brand-lockup">
            <span className="brand-mark">YowThi</span>
            <span className="brand-subtitle">ERP V2 · {shellCopy.tabletSuffix}</span>
          </div>
        </div>
        <nav className="tablet-navigation" aria-label={shellCopy.primaryNavigation}>
          {primaryNavigation.map((item) => (
            <NavLink
              key={item.to}
              to={withDeviceExperienceOverride(item.to)}
              className={({ isActive }) => (isActive ? 'active' : undefined)}
            >
              {item.label[locale]}
            </NavLink>
          ))}
        </nav>
      </header>

      <main className="app-shell tablet-app-shell">
        <Outlet />
      </main>
    </div>
  );
}
