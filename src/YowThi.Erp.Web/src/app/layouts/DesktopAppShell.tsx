import { NavLink, Outlet } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { useOperationalLocale } from '../i18n/locale';
import { applicationShellCopy, primaryNavigation } from '../navigation';

export function DesktopAppShell() {
  const { locale } = useOperationalLocale();
  const shellCopy = applicationShellCopy[locale];

  return (
    <div className="application-frame desktop-application-frame" data-ui-experience="desktop">
      <header className="topbar desktop-topbar">
        <div className="brand-lockup">
          <span className="brand-mark">YowThi</span>
          <span className="brand-subtitle">ERP V2</span>
        </div>
        <nav className="desktop-navigation" aria-label={shellCopy.primaryNavigation}>
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

      <main className="app-shell desktop-app-shell">
        <Outlet />
      </main>
    </div>
  );
}
