import { NavLink, Outlet } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { primaryNavigation } from '../navigation';

export function DesktopAppShell() {
  return (
    <div className="application-frame desktop-application-frame" data-ui-experience="desktop">
      <header className="topbar desktop-topbar">
        <div className="brand-lockup">
          <span className="brand-mark">YowThi</span>
          <span className="brand-subtitle">ERP V2</span>
        </div>
        <nav className="desktop-navigation" aria-label="Primary navigation">
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

      <main className="app-shell desktop-app-shell">
        <Outlet />
      </main>
    </div>
  );
}
