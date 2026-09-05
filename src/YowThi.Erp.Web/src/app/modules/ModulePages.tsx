import { Link } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
import { LocaleControl } from '../i18n/LocaleControl';
import { useOperationalLocale, type OperationalLocale } from '../i18n/locale';
import {
  getSystemModule,
  systemModules,
  type ModuleArea,
  type ModuleKey,
  type SystemModuleDefinition,
} from './moduleRegistry';

const modulePageCopy: Record<
  OperationalLocale,
  {
    eyebrow: string;
    title: string;
    intro: string;
    operations: string;
    controls: string;
    operational: string;
    skeleton: string;
    skeletonIntro: string;
    backToModules: string;
  }
> = {
  'zh-TW': {
    eyebrow: 'ERP V2',
    title: '系統模組',
    intro: '此頁只列出目前已存在正式 P6 後端能力的模組。已有操作畫面的模組可直接使用；其餘模組先建立穩定 Web route，後續依 P7 vertical slice 逐一接入。',
    operations: '作業模組',
    controls: '控制與基礎模組',
    operational: '已有操作畫面',
    skeleton: 'Web 骨架',
    skeletonIntro: '此模組的後端能力已存在；目前先納入 Web 系統骨架。後續 P7 vertical slice 會依正式 command/query contract 接入操作畫面，不在此處新增 Business Rule。',
    backToModules: '返回系統模組',
  },
  'th-TH': {
    eyebrow: 'ERP V2',
    title: 'โมดูลระบบ',
    intro: 'หน้านี้แสดงเฉพาะโมดูลที่มีความสามารถฝั่ง backend ของ P6 อยู่แล้ว โมดูลที่มีหน้าปฏิบัติงานสามารถใช้งานได้ทันที ส่วนโมดูลอื่นจะสร้าง Web route ที่คงที่ก่อน แล้วจึงเชื่อมต่อทีละ vertical slice ใน P7',
    operations: 'โมดูลปฏิบัติงาน',
    controls: 'โมดูลควบคุมและข้อมูลพื้นฐาน',
    operational: 'มีหน้าปฏิบัติงานแล้ว',
    skeleton: 'โครง Web',
    skeletonIntro: 'โมดูลนี้มีความสามารถฝั่ง backend อยู่แล้ว และถูกนำเข้าโครงระบบ Web ในขั้นนี้ P7 vertical slice ถัดไปจะเชื่อมต่อหน้าปฏิบัติงานตาม command/query contract ที่ยืนยันแล้ว โดยไม่สร้าง Business Rule เพิ่มในหน้านี้',
    backToModules: 'กลับไปโมดูลระบบ',
  },
};

export function ModuleIndexPage() {
  const { locale } = useOperationalLocale();
  const copy = modulePageCopy[locale];

  return (
    <section className="module-page">
      <header className="page-header">
        <div>
          <p className="eyebrow">{copy.eyebrow}</p>
          <h1>{copy.title}</h1>
          <p className="page-intro">{copy.intro}</p>
        </div>
        <LocaleControl />
      </header>

      <ModuleAreaSection area="operations" title={copy.operations} locale={locale} />
      <ModuleAreaSection area="controls" title={copy.controls} locale={locale} />
    </section>
  );
}

export function ModuleSkeletonPage({ moduleKey }: { moduleKey: ModuleKey }) {
  const { locale } = useOperationalLocale();
  const copy = modulePageCopy[locale];
  const module = getSystemModule(moduleKey);

  return (
    <section className="module-page">
      <header className="page-header">
        <div>
          <p className="eyebrow">{copy.eyebrow}</p>
          <h1>{module.label[locale]}</h1>
          <p className="page-intro">{copy.skeletonIntro}</p>
        </div>
        <LocaleControl />
      </header>

      <div className="entry-form">
        <p className="eyebrow">{copy.skeleton}</p>
        <p>{module.route}</p>
        <Link to={withDeviceExperienceOverride('/modules')}>{copy.backToModules}</Link>
      </div>
    </section>
  );
}

function ModuleAreaSection({
  area,
  title,
  locale,
}: {
  area: ModuleArea;
  title: string;
  locale: OperationalLocale;
}) {
  const copy = modulePageCopy[locale];
  const modules = systemModules.filter((module) => module.area === area);

  return (
    <section className="entry-form" aria-labelledby={`module-area-${area}`}>
      <h2 id={`module-area-${area}`}>{title}</h2>
      <ul className="module-list">
        {modules.map((module) => (
          <ModuleListItem key={module.key} module={module} locale={locale} copy={copy} />
        ))}
      </ul>
    </section>
  );
}

function ModuleListItem({
  module,
  locale,
  copy,
}: {
  module: SystemModuleDefinition;
  locale: OperationalLocale;
  copy: (typeof modulePageCopy)[OperationalLocale];
}) {
  return (
    <li>
      <Link to={withDeviceExperienceOverride(module.route)}>{module.label[locale]}</Link>
      <span>{module.webState === 'operational' ? copy.operational : copy.skeleton}</span>
    </li>
  );
}
