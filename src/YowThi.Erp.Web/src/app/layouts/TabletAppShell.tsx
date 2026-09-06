import { NavLink, Outlet } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { LocaleControl } from '../i18n/LocaleControl';
import { useOperationalLocale } from '../i18n/locale';
import { applicationShellCopy, navigationGroups } from '../navigation';

export function TabletAppShell() {
  const { locale } = useOperationalLocale();
  const shellCopy = applicationShellCopy[locale];

  return (
    <div className="application-frame tablet-application-frame" data-ui-experience="tablet">
      <header className="tablet-shell-header">
        <div className="tablet-brand-row">
          <div className="sidebar-brand tablet-brand">
            <span className="brand-emblem" aria-hidden="true">YT</span>
            <span className="brand-lockup-text">
              <strong>YowThi</strong>
              <small>ERP V2 · {shellCopy.tabletSuffix}</small>
            </span>
          </div>
          <LocaleControl />
        </div>

        <div className="tablet-navigation-groups">
          {navigationGroups.map((group) => (
            <section className="tablet-navigation-group" key={group.area}>
              <span className="navigation-group-label">
                {group.area === 'operations' ? shellCopy.operations : shellCopy.controls}
              </span>
              <nav className="tablet-navigation" aria-label={shellCopy.primaryNavigation}>
                {group.modules.map((module) => (
                  <NavLink
                    key={module.key}
                    to={withDeviceExperienceOverride(module.route)}
                    className={({ isActive }) => (isActive ? 'active' : undefined)}
                  >
                    {module.label[locale]}
                  </NavLink>
                ))}
              </nav>
            </section>
          ))}
        </div>
      </header>

      <main className="app-shell tablet-app-shell">
        <Outlet />
      </main>
    </div>
  );
}
