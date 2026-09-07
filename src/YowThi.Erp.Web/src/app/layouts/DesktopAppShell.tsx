import { NavLink, Outlet } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { LocaleControl } from '../i18n/LocaleControl';
import { useOperationalLocale } from '../i18n/locale';
import { applicationShellCopy } from '../navigation';
import { systemModules, type ModuleKey } from '../modules/moduleRegistry';

type DesktopNavigationIconKey = ModuleKey | 'home';

const navigationIconPaths: Record<DesktopNavigationIconKey, readonly string[]> = {
  home: ['M3 10.5 12 3l9 7.5', 'M5 9.5V21h14V9.5', 'M9 21v-7h6v7'],
  procurement: ['M4 7h16l-1.5 8H7L5.5 4H3', 'M8 20h.01', 'M17 20h.01'],
  outsourced: ['M4 8h5l2 3h9v7H4z', 'M9 8l2-4h4l2 7', 'M7 18v2', 'M17 18v2'],
  processing: ['M4 20V8l5-4v4l5-4v4l6-3v15z', 'M8 15h2', 'M14 15h2'],
  sales: ['M4 5h16v14H4z', 'M8 9h8', 'M8 13h5', 'M8 17h3'],
  'sales-handling': ['M5 7h14v12H5z', 'M8 7V5h8v2', 'M9 11h6', 'M12 11v5'],
  labor: ['M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z', 'M4 21a8 8 0 0 1 16 0', 'M8 17h8'],
  finance: ['M4 7h16v12H4z', 'M7 11h10', 'M7 15h6', 'M16 15h1'],
  inventory: ['M4 7 12 3l8 4-8 4z', 'M4 7v10l8 4 8-4V7', 'M12 11v10'],
  party: ['M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z', 'M2 21a7 7 0 0 1 14 0', 'M17 11a3 3 0 1 0 0-6', 'M16 16a5 5 0 0 1 6 5'],
  infrastructure: ['M4 21V8h16v13', 'M8 21v-5h8v5', 'M8 11h2', 'M14 11h2', 'M8 7V3h8v4'],
  product: ['M3 8 12 3l9 5-9 5z', 'M3 8v8l9 5 9-5V8', 'M12 13v8'],
  'data-protection': ['M12 3 5 6v5c0 5 3 8 7 10 4-2 7-5 7-10V6z', 'm9 12 2 2 4-4'],
};

function DesktopNavigationIcon({ icon }: { icon: DesktopNavigationIconKey }) {
  return (
    <svg className="desktop-nav-icon" viewBox="0 0 24 24" aria-hidden="true">
      {navigationIconPaths[icon].map((path) => (
        <path key={path} d={path} />
      ))}
    </svg>
  );
}

export function DesktopAppShell() {
  const { locale } = useOperationalLocale();
  const shellCopy = applicationShellCopy[locale];
  const homeLabel = locale === 'zh-TW' ? '首頁' : 'หน้าแรก';

  return (
    <div className="application-frame desktop-application-frame" data-ui-experience="desktop">
      <aside className="desktop-sidebar">
        <div className="sidebar-brand desktop-sidebar-brand">
          <span className="desktop-brand-emblem" aria-hidden="true">
            <svg viewBox="0 0 24 24">
              <path d="M20 4c-7 .2-12.2 2.1-15.4 5.8C2.5 12.2 2.9 16 5.8 18c2.8 1.9 6.8.8 8.7-2.3 1.7-2.8 1.8-6.3 5.5-11.7Z" />
              <path d="M5.5 18.2c2.1-3.9 5.4-6.8 9.8-8.7" />
            </svg>
          </span>
          <strong>YowThi ERP</strong>
        </div>

        <nav className="desktop-navigation reference-desktop-navigation" aria-label={shellCopy.primaryNavigation}>
          <NavLink
            to={withDeviceExperienceOverride('/modules')}
            end
            className={({ isActive }) => (isActive ? 'active' : undefined)}
          >
            <DesktopNavigationIcon icon="home" />
            <span>{homeLabel}</span>
          </NavLink>

          {systemModules.map((module) => (
            <NavLink
              key={module.key}
              to={withDeviceExperienceOverride(module.route)}
              className={({ isActive }) => (isActive ? 'active' : undefined)}
            >
              <DesktopNavigationIcon icon={module.key} />
              <span>{module.label[locale]}</span>
            </NavLink>
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
