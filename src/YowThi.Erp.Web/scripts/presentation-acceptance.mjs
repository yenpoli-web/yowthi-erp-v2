import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = fileURLToPath(new URL('..', import.meta.url));
const srcRoot = path.join(webRoot, 'src');

function fail(message) {
  console.error(`presentation acceptance: FAIL\n${message}`);
  process.exit(1);
}

function read(relativePath) {
  return fs.readFileSync(path.join(webRoot, relativePath), 'utf8');
}

function countMatches(text, regex) {
  return [...text.matchAll(regex)].length;
}

function collectFiles(directory, suffix, result = []) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      collectFiles(fullPath, suffix, result);
    } else if (entry.isFile() && entry.name.endsWith(suffix)) {
      result.push(fullPath);
    }
  }
  return result;
}

const shellExpectations = new Map([
  ['src/app/layouts/DesktopAppShell.tsx', 'desktop'],
  ['src/app/layouts/TabletAppShell.tsx', 'tablet'],
  ['src/app/layouts/MobileAppShell.tsx', 'mobile'],
]);

for (const [relativePath, experience] of shellExpectations) {
  const source = read(relativePath);
  const localeControls = countMatches(source, /<LocaleControl\s*\/>/g);
  if (localeControls !== 1) {
    fail(`${relativePath} must render exactly one LocaleControl; found ${localeControls}.`);
  }
  if (!source.includes(`data-ui-experience="${experience}"`)) {
    fail(`${relativePath} is missing data-ui-experience="${experience}".`);
  }
}

const adaptiveShellExpectations = [
  {
    relativePath: 'src/app/layouts/TabletAppShell.tsx',
    markers: ['nature-tablet-brand-row', 'nature-tablet-navigation', '<ModuleIcon icon="home"'],
  },
  {
    relativePath: 'src/app/layouts/MobileAppShell.tsx',
    markers: ['nature-mobile-brand-row', 'nature-mobile-module-menu', 'nature-mobile-bottom-navigation'],
  },
];

for (const expectation of adaptiveShellExpectations) {
  const source = read(expectation.relativePath);
  for (const marker of expectation.markers) {
    if (!source.includes(marker)) {
      fail(`${expectation.relativePath} is missing adaptive Nature Green marker: ${marker}.`);
    }
  }
}

const shellAbsolutePaths = new Set(
  [...shellExpectations.keys()].map((relativePath) => path.normalize(path.join(webRoot, relativePath))),
);
const tsxFiles = collectFiles(srcRoot, '.tsx');
let pageLocaleControls = 0;
let manualLocaleControlMarkupCount = 0;
let placeholderCount = 0;
let legacyOptionPickerCount = 0;
let persistentHintCount = 0;
const rawEnumPattern = />\s*(SUPPLIER|FARMER|DRAFT|CONFIRMED|WEIGHT_BASED_UNIT|UNIT_BASED|IN_HOUSE|OUTSOURCED|PROCUREMENT_PRODUCT|PROCESS_MATERIAL|SALES_PRODUCT)\s*</g;
const rawEnumHits = [];

for (const file of tsxFiles) {
  const source = fs.readFileSync(file, 'utf8');
  if (!shellAbsolutePaths.has(path.normalize(file))) {
    pageLocaleControls += countMatches(source, /<LocaleControl\s*\/>/g);
  }
  manualLocaleControlMarkupCount += countMatches(source, /<label\s+className="locale-control">/g);
  placeholderCount += countMatches(source, /\bplaceholder\s*=/g);
  legacyOptionPickerCount += countMatches(source, /className="option-picker(?:\s|")/g);
  persistentHintCount += countMatches(source, /className="(?:default-hint|required-hint)"/g);
  const enumMatches = [...source.matchAll(rawEnumPattern)];
  if (enumMatches.length > 0) {
    rawEnumHits.push(`${path.relative(webRoot, file)}: ${enumMatches.map((match) => match[1]).join(', ')}`);
  }
}

if (pageLocaleControls !== 0) {
  fail(`LocaleControl must be owned by the application shells only; found ${pageLocaleControls} page-level instance(s).`);
}
const manualPageLocaleControls = Math.max(0, manualLocaleControlMarkupCount - 1);
if (manualPageLocaleControls > 2) {
  fail(`Operational UI recovery allows at most 2 remaining page-level locale controls; found ${manualPageLocaleControls}.`);
}
if (legacyOptionPickerCount > 18) {
  fail(`Operational UI recovery allows at most 18 remaining stacked option-picker controls; found ${legacyOptionPickerCount}.`);
}
if (persistentHintCount > 9) {
  fail(`Operational UI recovery allows at most 9 remaining persistent hint controls; found ${persistentHintCount}.`);
}
if (placeholderCount !== 0) {
  fail(`User-requested hint-free UI requires zero placeholder attributes; found ${placeholderCount}.`);
}
if (rawEnumHits.length > 0) {
  fail(`Raw domain enum values are rendered directly in JSX:\n${rawEnumHits.join('\n')}`);
}

const localeControl = read('src/app/i18n/LocaleControl.tsx');
if (!localeControl.includes('<option value="zh-TW">繁體中文</option>')) {
  fail('LocaleControl is missing the Traditional Chinese option.');
}
if (!localeControl.includes('<option value="th-TH">ไทย</option>')) {
  fail('LocaleControl is missing the Thai option.');
}

const moduleRegistry = read('src/app/modules/moduleRegistry.ts');
const operationalModules = countMatches(moduleRegistry, /^\s+webState:\s*'operational',/gm);
const skeletonModules = countMatches(moduleRegistry, /^\s+webState:\s*'skeleton',/gm);
const zhLabels = countMatches(moduleRegistry, /label:\s*\{\s*'zh-TW':/g);
const thLabels = countMatches(moduleRegistry, /'th-TH':/g);

if (operationalModules !== 13 || skeletonModules !== 0) {
  fail(`Module registry must contain 13 operational modules and 0 skeleton modules; found ${operationalModules}/${skeletonModules}.`);
}
if (zhLabels !== 13 || thLabels < 13) {
  fail(`All 13 modules must carry zh-TW and th-TH labels; found zh-TW=${zhLabels}, th-TH=${thLabels}.`);
}

const hardDeleteOptions = read('src/features/data-protection/hardDeleteOptions.ts');
for (const target of ['suppliers', 'customers', 'outsourced-vendors', 'farmers']) {
  if (!hardDeleteOptions.includes(`'${target}'`)) {
    fail(`Canonical Data Protection target list is missing ${target}.`);
  }
}
const dataProtectionPage = read('src/features/data-protection/DataProtectionPage.tsx');
if (!dataProtectionPage.includes('hardDeleteTargetKinds.map')) {
  fail('Data Protection main page must render the canonical Hard Delete target list.');
}
if (!dataProtectionPage.includes('listHardDeleteOptions')) {
  fail('Data Protection main page must read through the target-specific Hard Delete options API.');
}
if (dataProtectionPage.includes('listSuppliers') || dataProtectionPage.includes('listCustomers')) {
  fail('Data Protection main page must not regress to Supplier/Customer-only master queries.');
}
const routerSource = read('src/app/router.tsx');
if (!routerSource.includes(`path: 'data-protection/hard-delete', element: <Navigate to=\"/data-protection\" replace />`)) {
  fail('Legacy Data Protection Hard Delete route must converge on the primary Data Protection workspace.');
}

const navigation = read('src/app/navigation.ts');
if (!navigation.includes("to: '/modules',\n    icon: 'home'")) {
  fail('Mobile quick navigation must begin with the localized home workspace entry.');
}
if (countMatches(navigation, /^\s+icon:\s*'/gm) < 4) {
  fail('Mobile quick navigation must expose icons for all four primary entries.');
}

const presentationShell = read('src/app/layouts/presentationShell.css');
for (const selector of [
  '.nature-tablet-brand-row',
  '.nature-tablet-navigation',
  '.nature-mobile-brand-row',
  '.nature-mobile-module-menu',
  '.nature-mobile-bottom-navigation',
]) {
  if (!presentationShell.includes(selector)) {
    fail(`Presentation shell is missing adaptive Nature Green selector: ${selector}.`);
  }
}

const deviceExperience = read('src/app/device/deviceExperience.ts');
if (!deviceExperience.includes("export type DeviceExperience = 'desktop' | 'tablet' | 'mobile';")) {
  fail('DeviceExperience must remain exactly desktop | tablet | mobile.');
}
if (!deviceExperience.includes("new URLSearchParams(window.location.search).get('ui')")) {
  fail('Deterministic ?ui= acceptance override is missing.');
}

console.log('presentation acceptance: PASS');
console.log('- application shell locale ownership: 3/3');
console.log('- page-level LocaleControl component instances: 0');
console.log(`- recovery debt: ${manualPageLocaleControls}/2 page locale controls, ${legacyOptionPickerCount}/18 legacy option-pickers, ${persistentHintCount}/9 persistent hints`);
console.log('- placeholder hints: 0');
console.log('- raw JSX domain enums: 0');
console.log('- module registry: 13 operational / 13 localized');
console.log('- Data Protection correspondence: 4/4 target-specific Hard Delete controls');
console.log('- Nature Green tablet/mobile shell markers: present');
console.log('- mobile primary navigation: localized home + 3 core operations');
console.log('- deterministic desktop/tablet/mobile override: present');
