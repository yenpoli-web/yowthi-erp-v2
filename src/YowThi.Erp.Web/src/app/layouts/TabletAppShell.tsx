import { NavLink, Outlet } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { primaryNavigation } from '../navigation';

export function TabletAppShell() {
  return (
    <div className="application-frame tablet-application-frame" data-ui-experience="tablet">
      <header className="tablet-topbar">
        <div className="tablet-brand-row">
          <div className="brand-lockup">
            <span className="brand-mark">YowThi</span>
            <span className="brand-subtitle">ERP V2 · Tablet</span>
          </div>
        </div>
        <nav className="tablet-navigation" aria-label="Primary navigation">
          {primaryNavigation.map((item) => (
            <NavLink
              key={item.to}
              to={withDeviceExperienceOverride(item.to)}
              className={({ isActive }) => (isActive ? 'active' : undefined)}
            >
              {item.label}
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
