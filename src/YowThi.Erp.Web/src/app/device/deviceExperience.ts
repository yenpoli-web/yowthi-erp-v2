import { useEffect, useState } from 'react';

export type DeviceExperience = 'desktop' | 'tablet' | 'mobile';

const mobileViewportQuery = '(max-width: 699px)';
const desktopViewportQuery = '(min-width: 1100px)';
const finePointerQuery = '(any-pointer: fine)';
const hoverQuery = '(any-hover: hover)';

function readExperienceOverride(): DeviceExperience | null {
  const value = new URLSearchParams(window.location.search).get('ui');

  return value === 'desktop' || value === 'tablet' || value === 'mobile'
    ? value
    : null;
}

export function resolveDeviceExperience(): DeviceExperience {
  const override = readExperienceOverride();

  if (override !== null) {
    return override;
  }

  if (window.matchMedia(mobileViewportQuery).matches) {
    return 'mobile';
  }

  const isDesktopWidth = window.matchMedia(desktopViewportQuery).matches;
  const hasDesktopInput =
    window.matchMedia(finePointerQuery).matches ||
    window.matchMedia(hoverQuery).matches;

  return isDesktopWidth && hasDesktopInput ? 'desktop' : 'tablet';
}

export function withDeviceExperienceOverride(path: string): string {
  const override = readExperienceOverride();
  return override === null ? path : `${path}?ui=${override}`;
}

export function useDeviceExperience(): DeviceExperience {
  const [experience, setExperience] = useState<DeviceExperience>(resolveDeviceExperience);

  useEffect(() => {
    const mediaQueries = [
      window.matchMedia(mobileViewportQuery),
      window.matchMedia(desktopViewportQuery),
      window.matchMedia(finePointerQuery),
      window.matchMedia(hoverQuery),
    ];

    const updateExperience = () => {
      setExperience(resolveDeviceExperience());
    };

    for (const mediaQuery of mediaQueries) {
      mediaQuery.addEventListener('change', updateExperience);
    }

    window.addEventListener('popstate', updateExperience);

    return () => {
      for (const mediaQuery of mediaQueries) {
        mediaQuery.removeEventListener('change', updateExperience);
      }

      window.removeEventListener('popstate', updateExperience);
    };
  }, []);

  return experience;
}
