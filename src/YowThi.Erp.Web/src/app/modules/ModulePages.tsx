import { Link } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { useOperationalLocale, type OperationalLocale } from '../i18n/locale';
import { ModuleIcon } from './ModuleIcon';
import {
  getSystemModule,
  systemModules,
  type ModuleArea,
  type ModuleKey,
  type SystemModuleDefinition,
} from './moduleRegistry';

const homePageCopy: Record<
  OperationalLocale,
  {
    welcome: string;
    subtitle: string;
    operations: string;
    controls: string;
    openModule: string;
  }
> = {
  'zh-TW': {
    welcome: '歡迎使用 YowThi ERP V2',
    subtitle: '日常作業與資料控制集中在同一個工作台。',
    operations: '日常作業',
    controls: '資料與控制',
    openModule: '開啟模組',
  },
  'th-TH': {
    welcome: 'ยินดีต้อนรับสู่ YowThi ERP V2',
    subtitle: 'รวมงานประจำวันและการควบคุมข้อมูลไว้ในพื้นที่ทำงานเดียว',
    operations: 'งานประจำวัน',
    controls: 'ข้อมูลและการควบคุม',
    openModule: 'เปิดโมดูล',
  },
};

const skeletonPageCopy: Record<
  OperationalLocale,
  {
    eyebrow: string;
    skeleton: string;
    backToModules: string;
  }
> = {
  'zh-TW': {
    eyebrow: 'ERP V2',
    skeleton: '尚未接入',
    backToModules: '返回首頁',
  },
  'th-TH': {
    eyebrow: 'ERP V2',
    skeleton: 'ยังไม่เชื่อมต่อ',
    backToModules: 'กลับหน้าแรก',
  },
};

export function ModuleIndexPage() {
  const { locale } = useOperationalLocale();
  const copy = homePageCopy[locale];

  return (
    <section className="home-page">
      <section className="home-hero" aria-labelledby="home-welcome-title">
        <div className="home-hero-copy">
          <span className="home-hero-kicker">YowThi ERP V2</span>
          <h1 id="home-welcome-title">{copy.welcome}</h1>
          <p>{copy.subtitle}</p>
        </div>
        <div className="home-hero-landscape" aria-hidden="true">
          <span className="home-sun" />
          <span className="home-field home-field-one" />
          <span className="home-field home-field-two" />
          <span className="home-field home-field-three" />
        </div>
      </section>

      <HomeModuleSection area="operations" title={copy.operations} locale={locale} />
      <HomeModuleSection area="controls" title={copy.controls} locale={locale} />
    </section>
  );
}

export function ModuleSkeletonPage({ moduleKey }: { moduleKey: ModuleKey }) {
  const { locale } = useOperationalLocale();
  const copy = skeletonPageCopy[locale];
  const module = getSystemModule(moduleKey);

  return (
    <section className="module-page">
      <header className="page-header">
        <div>
          <p className="eyebrow">{copy.eyebrow}</p>
          <h1>{module.label[locale]}</h1>
        </div>
      </header>

      <div className="entry-form">
        <p className="eyebrow">{copy.skeleton}</p>
        <Link to={withDeviceExperienceOverride('/modules')}>{copy.backToModules}</Link>
      </div>
    </section>
  );
}

function HomeModuleSection({
  area,
  title,
  locale,
}: {
  area: ModuleArea;
  title: string;
  locale: OperationalLocale;
}) {
  const copy = homePageCopy[locale];
  const modules = systemModules.filter((module) => module.area === area);

  return (
    <section className="home-module-section" aria-labelledby={`home-module-area-${area}`}>
      <header className="home-section-header">
        <h2 id={`home-module-area-${area}`}>{title}</h2>
      </header>
      <div className="home-module-grid">
        {modules.map((module) => (
          <HomeModuleCard key={module.key} module={module} locale={locale} openModule={copy.openModule} />
        ))}
      </div>
    </section>
  );
}

function HomeModuleCard({
  module,
  locale,
  openModule,
}: {
  module: SystemModuleDefinition;
  locale: OperationalLocale;
  openModule: string;
}) {
  return (
    <Link
      className="home-module-card"
      to={withDeviceExperienceOverride(module.route)}
      aria-label={`${openModule}: ${module.label[locale]}`}
    >
      <span className="home-module-icon-wrap" aria-hidden="true">
        <ModuleIcon icon={module.key} className="home-module-icon" />
      </span>
      <strong>{module.label[locale]}</strong>
      <span className="home-module-arrow" aria-hidden="true">→</span>
    </Link>
  );
}
