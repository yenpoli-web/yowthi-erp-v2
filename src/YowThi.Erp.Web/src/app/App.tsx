import { useDeviceExperience } from './device/deviceExperience';
import { DesktopAppShell } from './layouts/DesktopAppShell';
import { MobileAppShell } from './layouts/MobileAppShell';
import { TabletAppShell } from './layouts/TabletAppShell';

export function App() {
  const experience = useDeviceExperience();

  if (experience === 'desktop') {
    return <DesktopAppShell />;
  }

  if (experience === 'mobile') {
    return <MobileAppShell />;
  }

  return <TabletAppShell />;
}
