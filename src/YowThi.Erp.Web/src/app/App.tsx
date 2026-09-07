import { useLayoutEffect } from 'react';
import { useLocation } from 'react-router';

import { useDeviceExperience } from './device/deviceExperience';
import { DesktopAppShell } from './layouts/DesktopAppShell';
import { MobileAppShell } from './layouts/MobileAppShell';
import { TabletAppShell } from './layouts/TabletAppShell';

export function App() {
  const experience = useDeviceExperience();
  const location = useLocation();

  useLayoutEffect(() => {
    const resetViewport = () => {
      window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
      document.documentElement.scrollTop = 0;
      document.body.scrollTop = 0;
    };

    resetViewport();
    const animationFrame = window.requestAnimationFrame(resetViewport);
    return () => window.cancelAnimationFrame(animationFrame);
  }, [location.pathname]);

  if (experience === 'desktop') {
    return <DesktopAppShell />;
  }

  if (experience === 'mobile') {
    return <MobileAppShell />;
  }

  return <TabletAppShell />;
}
