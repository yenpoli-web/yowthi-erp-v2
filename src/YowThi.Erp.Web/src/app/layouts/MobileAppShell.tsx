import { NavLink, Outlet } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { primaryNavigation } from '../navigation';

export function MobileAppShell() {
  return (
    <div className="application-frame mobile-application-frame" data-ui-experience="mobile">
      <header className="mobile-topbar">
        <div className="brand-lockup">
          <span className="brand-mark">YowThi</span>
          <span className="brand-subtitle">ERP V2 · Mobile</span>
        </div>
      </header>

      <main className="app-shell mobile-app-shell">
        <Outlet />
      </main>

      <nav className="mobile-bottom-navigation" aria-label="Primary navigation">
        {primaryNavigation.map((item) => (
          <NavLink
            key={item.to}
            to={withDeviceExperienceOverride(item.to)}
            className={({ isActive }) => (isActive ? 'active' : undefined)}
          >
            {item.compactLabel}
          </NavLink>
        ))}
      </nav>
    </div>
  );
}
