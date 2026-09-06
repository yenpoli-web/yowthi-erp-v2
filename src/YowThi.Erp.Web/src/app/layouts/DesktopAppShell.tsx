import { NavLink, Outlet } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { LocaleControl } from '../i18n/LocaleControl';
import { useOperationalLocale } from '../i18n/locale';
import { applicationShellCopy, navigationGroups } from '../navigation';

export function DesktopAppShell() {
  const { locale } = useOperationalLocale();
  const shellCopy = applicationShellCopy[locale];

  return (
    <div className="application-frame desktop-application-frame" data-ui-experience="desktop">
      <aside className="desktop-sidebar">
        <div className="sidebar-brand">
          <span className="brand-emblem" aria-hidden="true">YT</span>
          <span className="brand-lockup-text">
            <strong>YowThi</strong>
            <small>ERP V2</small>
          </span>
        </div>

        <nav className="desktop-navigation" aria-label={shellCopy.primaryNavigation}>
          {navigationGroups.map((group) => (
            <section className="navigation-group" key={group.area}>
              <p className="navigation-group-label">
                {group.area === 'operations' ? shellCopy.operations : shellCopy.controls}
              </p>
              <div className="navigation-group-links">
                {group.modules.map((module) => (
                  <NavLink
                    key={module.key}
                    to={withDeviceExperienceOverride(module.route)}
                    className={({ isActive }) => (isActive ? 'active' : undefined)}
                  >
                    <span>{module.label[locale]}</span>
                  </NavLink>
                ))}
              </div>
            </section>
          ))}
        </nav>

      </aside>

      <div className="desktop-workspace">
        <header className="workspace-topbar">
          <div>
            <span className="workspace-kicker">YowThi ERP V2</span>
            <strong>{shellCopy.workspace}</strong>
          </div>
          <LocaleControl />
        </header>

        <main className="app-shell desktop-app-shell">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
