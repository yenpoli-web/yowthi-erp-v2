import { NavLink, Outlet } from 'react-router';

export function App() {
  return (
    <div className="application-frame">
      <header className="topbar">
        <div>
          <span className="brand-mark">YowThi</span>
          <span className="brand-subtitle">ERP V2</span>
        </div>
        <nav aria-label="Primary navigation">
          <NavLink to="/procurement/entries/new">Procurement</NavLink>
        </nav>
      </header>

      <main className="app-shell">
        <Outlet />
      </main>
    </div>
  );
}
