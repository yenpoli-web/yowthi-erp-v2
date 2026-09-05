import {
  createContext,
  type PropsWithChildren,
  useContext,
  useEffect,
  useMemo,
  useState,
} from 'react';

export type OperationalLocale = 'zh-TW' | 'th-TH';

interface LocaleContextValue {
  locale: OperationalLocale;
  setLocale: (locale: OperationalLocale) => void;
}

const localeStorageKey = 'yowthi.erp.locale';
const LocaleContext = createContext<LocaleContextValue | null>(null);

export function LocaleProvider({ children }: PropsWithChildren) {
  const [locale, setLocale] = useState<OperationalLocale>(resolveInitialLocale);

  useEffect(() => {
    document.documentElement.lang = locale;
    writePersistedLocale(locale);
  }, [locale]);

  const value = useMemo<LocaleContextValue>(
    () => ({ locale, setLocale }),
    [locale],
  );

  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>;
}

export function useOperationalLocale(): LocaleContextValue {
  const value = useContext(LocaleContext);
  if (value === null) {
    throw new Error('useOperationalLocale must be used inside LocaleProvider.');
  }

  return value;
}

export function normalizeOperationalLocale(value: string | null | undefined): OperationalLocale | null {
  switch (value?.trim().toLowerCase()) {
    case 'zh-tw':
      return 'zh-TW';
    case 'th-th':
      return 'th-TH';
    default:
      return null;
  }
}

function resolveInitialLocale(): OperationalLocale {
  const persisted = readPersistedLocale();
  if (persisted !== null) {
    return persisted;
  }

  const configured = normalizeOperationalLocale(import.meta.env.VITE_DEFAULT_LOCALE);
  if (configured !== null) {
    return configured;
  }

  for (const candidate of navigator.languages) {
    const normalized = normalizeOperationalLocale(candidate);
    if (normalized !== null) {
      return normalized;
    }
  }

  const browserLocale = normalizeOperationalLocale(navigator.language);
  return browserLocale ?? 'zh-TW';
}

function readPersistedLocale(): OperationalLocale | null {
  try {
    return normalizeOperationalLocale(window.localStorage.getItem(localeStorageKey));
  } catch {
    return null;
  }
}

function writePersistedLocale(locale: OperationalLocale): void {
  try {
    window.localStorage.setItem(localeStorageKey, locale);
  } catch {
    // Locale persistence is optional; the in-memory preference remains authoritative.
  }
}
