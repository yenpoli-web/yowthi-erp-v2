import { useOperationalLocale, type OperationalLocale } from './locale';

const localeControlCopy: Record<OperationalLocale, string> = {
  'zh-TW': '語言',
  'th-TH': 'ภาษา',
};

export function LocaleControl() {
  const { locale, setLocale } = useOperationalLocale();

  return (
    <label className="locale-control">
      <span>{localeControlCopy[locale]}</span>
      <select
        value={locale}
        onChange={(event) => setLocale(event.target.value as OperationalLocale)}
      >
        <option value="zh-TW">繁體中文</option>
        <option value="th-TH">ไทย</option>
      </select>
    </label>
  );
}
