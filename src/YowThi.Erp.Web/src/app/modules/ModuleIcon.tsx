import type { ModuleKey } from './moduleRegistry';

export type ModuleIconKey = ModuleKey | 'home';

const moduleIconPaths: Record<ModuleIconKey, readonly string[]> = {
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

export function ModuleIcon({ icon, className }: { icon: ModuleIconKey; className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" aria-hidden="true">
      {moduleIconPaths[icon].map((path) => (
        <path key={path} d={path} />
      ))}
    </svg>
  );
}
