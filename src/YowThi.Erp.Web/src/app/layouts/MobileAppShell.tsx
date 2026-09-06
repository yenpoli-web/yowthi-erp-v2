import { useState } from 'react';
import { NavLink, Outlet } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { LocaleControl } from '../i18n/LocaleControl';
import { useOperationalLocale } from '../i18n/locale';
import { applicationShellCopy, mobileQuickNavigation, navigationGroups } from '../navigation';

export function MobileAppShell() {
  const { locale } = useOperationalLocale();
  const shellCopy = applicationShellCopy[locale];
  const [menuOpen, setMenuOpen] = useState(false);

  return (
    <div className="application-frame mobile-application-frame" data-ui-experience="mobile">
      <header className="mobile-shell-header">
        <div className="mobile-brand-row">
          <div className="sidebar-brand mobile-brand">
            <span className="brand-emblem" aria-hidden="true">YT</span>
            <span className="brand-lockup-text">
              <strong>YowThi</strong>
              <small>ERP V2</small>
            </span>
          </div>
          <LocaleControl />
        </div>

        <div className="mobile-toolbar">
          <span>{shellCopy.mobileSuffix}</span>
          <button
            className="mobile-menu-trigger"
            type="button"
            aria-expanded={menuOpen}
            aria-controls="mobile-module-menu"
            onClick={() => setMenuOpen((current) => !current)}
          >
            {menuOpen ? shellCopy.closeMenu : shellCopy.moduleMenu}
          </button>
        </div>
      </header>

      {menuOpen && (
        <nav
          id="mobile-module-menu"
          className="mobile-module-menu"
          aria-label={shellCopy.primaryNavigation}
        >
          {navigationGroups.map((group) => (
            <section className="mobile-navigation-group" key={group.area}>
              <p className="navigation-group-label">
                {group.area === 'operations' ? shellCopy.operations : shellCopy.controls}
              </p>
              <div className="mobile-navigation-links">
                {group.modules.map((module) => (
                  <NavLink
                    key={module.key}
                    to={withDeviceExperienceOverride(module.route)}
                    className={({ isActive }) => (isActive ? 'active' : undefined)}
                    onClick={() => setMenuOpen(false)}
                  >
                    {module.label[locale]}
                  </NavLink>
                ))}
              </div>
            </section>
          ))}
        </nav>
      )}

      <main className="app-shell mobile-app-shell">
        <Outlet />
      </main>

      <nav className="mobile-bottom-navigation" aria-label={shellCopy.primaryNavigation}>
        {mobileQuickNavigation.map((item) => (
          <NavLink
            key={item.to}
            to={withDeviceExperienceOverride(item.to)}
            className={({ isActive }) => (isActive ? 'active' : undefined)}
          >
            {item.label[locale]}
          </NavLink>
        ))}
      </nav>
    </div>
  );
}
