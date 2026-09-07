import { Link } from 'react-router';

import { withDeviceExperienceOverride } from '../device/deviceExperience';
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
    intro: '12 個 ERP 模組已全部接入正式操作畫面。本頁是 Desktop、Tablet、Mobile 共用的模組索引；各模組仍使用同一套已驗證的 command、query、權限與 Business Rule 邊界。',
    operations: '日常作業',
    controls: '資料與控制',
    operational: '可使用',
    skeleton: '尚未接入',
    skeletonIntro: '此模組目前尚未接入操作畫面；若未來重新出現 skeleton 狀態，仍必須依正式 command/query contract 實作，不得在 presentation layer 新增 Business Rule。',
    backToModules: '返回系統模組',
  },
  'th-TH': {
    eyebrow: 'ERP V2',
    title: 'โมดูลระบบ',
    intro: 'โมดูล ERP ทั้ง 12 โมดูลมีหน้าปฏิบัติงานแล้ว หน้านี้เป็นดัชนีโมดูลร่วมสำหรับ Desktop, Tablet และ Mobile โดยทุกโมดูลยังใช้ command, query, สิทธิ์ และขอบเขต Business Rule ชุดเดียวกันที่ผ่านการตรวจสอบแล้ว',
    operations: 'งานประจำวัน',
    controls: 'ข้อมูลและการควบคุม',
    operational: 'พร้อมใช้งาน',
    skeleton: 'ยังไม่เชื่อมต่อ',
    skeletonIntro: 'โมดูลนี้ยังไม่มีหน้าปฏิบัติงาน หากในอนาคตมีสถานะ skeleton อีก ต้องเชื่อมต่อตาม command/query contract ที่ยืนยันแล้ว และห้ามสร้าง Business Rule ใหม่ใน presentation layer',
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
        </div>
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
        </div>
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
    <section className="entry-form module-area-card" aria-labelledby={`module-area-${area}`}>
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
