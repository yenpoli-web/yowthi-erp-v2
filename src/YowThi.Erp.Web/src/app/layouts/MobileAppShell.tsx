import { useState } from 'react';
import { NavLink, Outlet } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { LocaleControl } from '../i18n/LocaleControl';
import { useOperationalLocale } from '../i18n/locale';
import { ModuleIcon } from '../modules/ModuleIcon';
import { applicationShellCopy, mobileQuickNavigation, navigationGroups } from '../navigation';

export function MobileAppShell() {
  const { locale } = useOperationalLocale();
  const shellCopy = applicationShellCopy[locale];
  const [menuOpen, setMenuOpen] = useState(false);
  const homeLabel = locale === 'zh-TW' ? '首頁' : 'หน้าแรก';

  return (
    <div className="application-frame mobile-application-frame" data-ui-experience="mobile">
      <header className="mobile-shell-header nature-mobile-shell-header">
        <div className="mobile-brand-row nature-mobile-brand-row">
          <div className="nature-mobile-brand">
            <span className="nature-brand-emblem" aria-hidden="true">
              <svg viewBox="0 0 24 24">
                <path d="M20 4c-7 .2-12.2 2.1-15.4 5.8C2.5 12.2 2.9 16 5.8 18c2.8 1.9 6.8.8 8.7-2.3 1.7-2.8 1.8-6.3 5.5-11.7Z" />
                <path d="M5.5 18.2c2.1-3.9 5.4-6.8 9.8-8.7" />
              </svg>
            </span>
            <span className="nature-mobile-brand-copy">
              <strong>YowThi ERP</strong>
              <small>{shellCopy.mobileSuffix}</small>
            </span>
          </div>
          <LocaleControl />
        </div>

        <div className="mobile-toolbar nature-mobile-toolbar">
          <strong>{shellCopy.workspace}</strong>
          <button
            className="mobile-menu-trigger nature-mobile-menu-trigger"
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
          className="mobile-module-menu nature-mobile-module-menu"
          aria-label={shellCopy.primaryNavigation}
        >
          <NavLink
            to={withDeviceExperienceOverride('/modules')}
            end
            className={({ isActive }) => `nature-mobile-menu-home${isActive ? ' active' : ''}`}
            onClick={() => setMenuOpen(false)}
          >
            <ModuleIcon icon="home" className="nature-mobile-menu-icon" />
            <span>{homeLabel}</span>
          </NavLink>

          {navigationGroups.map((group) => (
            <section className="mobile-navigation-group nature-mobile-navigation-group" key={group.area}>
              <p className="navigation-group-label">
                {group.area === 'operations' ? shellCopy.operations : shellCopy.controls}
              </p>
              <div className="mobile-navigation-links nature-mobile-navigation-links">
                {group.modules.map((module) => (
                  <NavLink
                    key={module.key}
                    to={withDeviceExperienceOverride(module.route)}
                    className={({ isActive }) => (isActive ? 'active' : undefined)}
                    onClick={() => setMenuOpen(false)}
                  >
                    <ModuleIcon icon={module.key} className="nature-mobile-menu-icon" />
                    <span>{module.label[locale]}</span>
                  </NavLink>
                ))}
              </div>
            </section>
          ))}
        </nav>
      )}

      <main className="app-shell mobile-app-shell nature-mobile-app-shell">
        <Outlet />
      </main>

      <nav className="mobile-bottom-navigation nature-mobile-bottom-navigation" aria-label={shellCopy.primaryNavigation}>
        {mobileQuickNavigation.map((item) => (
          <NavLink
            key={item.to}
            to={withDeviceExperienceOverride(item.to)}
            className={({ isActive }) => (isActive ? 'active' : undefined)}
          >
            <ModuleIcon icon={item.icon} className="nature-mobile-bottom-icon" />
            <span>{item.label[locale]}</span>
          </NavLink>
        ))}
      </nav>
    </div>
  );
}
