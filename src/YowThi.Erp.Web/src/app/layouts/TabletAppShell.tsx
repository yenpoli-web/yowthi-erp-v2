import { NavLink, Outlet } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { LocaleControl } from '../i18n/LocaleControl';
import { useOperationalLocale } from '../i18n/locale';
import { ModuleIcon } from '../modules/ModuleIcon';
import { systemModules } from '../modules/moduleRegistry';
import { applicationShellCopy } from '../navigation';

export function TabletAppShell() {
  const { locale } = useOperationalLocale();
  const shellCopy = applicationShellCopy[locale];
  const homeLabel = locale === 'zh-TW' ? '首頁' : 'หน้าแรก';

  return (
    <div className="application-frame tablet-application-frame" data-ui-experience="tablet">
      <header className="tablet-shell-header nature-tablet-shell-header">
        <div className="tablet-brand-row nature-tablet-brand-row">
          <div className="nature-tablet-brand">
            <span className="nature-brand-emblem" aria-hidden="true">
              <svg viewBox="0 0 24 24">
                <path d="M20 4c-7 .2-12.2 2.1-15.4 5.8C2.5 12.2 2.9 16 5.8 18c2.8 1.9 6.8.8 8.7-2.3 1.7-2.8 1.8-6.3 5.5-11.7Z" />
                <path d="M5.5 18.2c2.1-3.9 5.4-6.8 9.8-8.7" />
              </svg>
            </span>
            <span className="nature-tablet-brand-copy">
              <strong>YowThi ERP</strong>
              <small>{shellCopy.tabletSuffix}</small>
            </span>
          </div>
          <LocaleControl />
        </div>

        <nav className="nature-tablet-navigation" aria-label={shellCopy.primaryNavigation}>
          <NavLink
            to={withDeviceExperienceOverride('/modules')}
            end
            className={({ isActive }) => (isActive ? 'active' : undefined)}
          >
            <ModuleIcon icon="home" className="nature-tablet-nav-icon" />
            <span>{homeLabel}</span>
          </NavLink>

          {systemModules.map((module) => (
            <NavLink
              key={module.key}
              to={withDeviceExperienceOverride(module.route)}
              className={({ isActive }) => (isActive ? 'active' : undefined)}
            >
              <ModuleIcon icon={module.key} className="nature-tablet-nav-icon" />
              <span>{module.label[locale]}</span>
            </NavLink>
          ))}
        </nav>
      </header>

      <main className="app-shell tablet-app-shell nature-tablet-app-shell">
        <Outlet />
      </main>
    </div>
  );
}
