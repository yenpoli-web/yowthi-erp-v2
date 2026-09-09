import { spawn } from 'node:child_process';
import fs from 'node:fs';
import { mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:http';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = fileURLToPath(new URL('..', import.meta.url));
const viteBin = path.join(webRoot, 'node_modules', 'vite', 'bin', 'vite.js');
const chromeCandidates = [
  path.join(process.env.PROGRAMFILES ?? 'C:\\Program Files', 'Google', 'Chrome', 'Application', 'chrome.exe'),
  path.join(process.env['PROGRAMFILES(X86)'] ?? 'C:\\Program Files (x86)', 'Google', 'Chrome', 'Application', 'chrome.exe'),
];
const chromePath = chromeCandidates.find((candidate) => fs.existsSync(candidate));

const cases = [
  { name: 'desktop-home', experience: 'desktop', width: 1440, height: 900, path: '/modules' },
  { name: 'desktop-procurement', experience: 'desktop', width: 1440, height: 900, path: '/procurement' },
  { name: 'desktop-outsourced', experience: 'desktop', width: 1440, height: 900, path: '/outsourced' },
  { name: 'desktop-processing', experience: 'desktop', width: 1440, height: 900, path: '/processing/executions/new' },
  { name: 'desktop-sales', experience: 'desktop', width: 1440, height: 900, path: '/sales' },
  { name: 'desktop-sales-handling', experience: 'desktop', width: 1440, height: 900, path: '/sales-handling' },
  { name: 'desktop-sales-handling-packaging-items', experience: 'desktop', width: 1440, height: 900, path: '/sales-handling/packaging-items' },
  { name: 'desktop-labor', experience: 'desktop', width: 1440, height: 900, path: '/labor' },
  { name: 'desktop-finance', experience: 'desktop', width: 1440, height: 900, path: '/finance' },
  { name: 'desktop-inventory', experience: 'desktop', width: 1440, height: 900, path: '/inventory' },
  { name: 'desktop-inventory-operations', experience: 'desktop', width: 1440, height: 900, path: '/inventory/operations' },
  { name: 'desktop-inventory-warehouses', experience: 'desktop', width: 1440, height: 900, path: '/inventory/warehouses' },
  { name: 'desktop-inventory-storage-locations', experience: 'desktop', width: 1440, height: 900, path: '/inventory/storage-locations' },
  { name: 'desktop-party', experience: 'desktop', width: 1440, height: 900, path: '/party' },
  { name: 'desktop-party-customer', experience: 'desktop', width: 1440, height: 900, path: '/party/customers' },
  { name: 'desktop-party-outsourced-vendor', experience: 'desktop', width: 1440, height: 900, path: '/party/outsourced-vendors' },
  { name: 'desktop-party-farmer', experience: 'desktop', width: 1440, height: 900, path: '/party/farmers' },
  { name: 'desktop-party-employee', experience: 'desktop', width: 1440, height: 900, path: '/party/employees' },
  { name: 'desktop-infrastructure', experience: 'desktop', width: 1440, height: 900, path: '/infrastructure' },
  { name: 'desktop-product', experience: 'desktop', width: 1440, height: 900, path: '/product' },
  { name: 'desktop-product-sales-products', experience: 'desktop', width: 1440, height: 900, path: '/product/sales-products' },
  { name: 'desktop-product-sales-product-groups', experience: 'desktop', width: 1440, height: 900, path: '/product/sales-product-groups' },
  { name: 'desktop-product-lifecycle', experience: 'desktop', width: 1440, height: 900, path: '/product/lifecycle' },
  { name: 'desktop-security', experience: 'desktop', width: 1440, height: 900, path: '/security/accounts' },
  { name: 'desktop-data-protection', experience: 'desktop', width: 1440, height: 900, path: '/data-protection' },
  { name: 'tablet-home', experience: 'tablet', width: 1024, height: 768, path: '/modules' },
  { name: 'tablet-procurement', experience: 'tablet', width: 1024, height: 768, path: '/procurement' },
  { name: 'tablet-outsourced', experience: 'tablet', width: 1024, height: 768, path: '/outsourced' },
  { name: 'tablet-processing', experience: 'tablet', width: 1024, height: 768, path: '/processing/executions/new' },
  { name: 'tablet-sales', experience: 'tablet', width: 1024, height: 768, path: '/sales' },
  { name: 'tablet-sales-handling', experience: 'tablet', width: 1024, height: 768, path: '/sales-handling' },
  { name: 'tablet-sales-handling-packaging-items', experience: 'tablet', width: 1024, height: 768, path: '/sales-handling/packaging-items' },
  { name: 'tablet-labor', experience: 'tablet', width: 1024, height: 768, path: '/labor' },
  { name: 'tablet-finance', experience: 'tablet', width: 1024, height: 768, path: '/finance' },
  { name: 'tablet-inventory', experience: 'tablet', width: 1024, height: 768, path: '/inventory' },
  { name: 'tablet-inventory-operations', experience: 'tablet', width: 1024, height: 768, path: '/inventory/operations' },
  { name: 'tablet-inventory-warehouses', experience: 'tablet', width: 1024, height: 768, path: '/inventory/warehouses' },
  { name: 'tablet-inventory-storage-locations', experience: 'tablet', width: 1024, height: 768, path: '/inventory/storage-locations' },
  { name: 'tablet-party', experience: 'tablet', width: 1024, height: 768, path: '/party' },
  { name: 'tablet-party-customer', experience: 'tablet', width: 1024, height: 768, path: '/party/customers' },
  { name: 'tablet-party-outsourced-vendor', experience: 'tablet', width: 1024, height: 768, path: '/party/outsourced-vendors' },
  { name: 'tablet-party-farmer', experience: 'tablet', width: 1024, height: 768, path: '/party/farmers' },
  { name: 'tablet-party-employee', experience: 'tablet', width: 1024, height: 768, path: '/party/employees' },
  { name: 'tablet-infrastructure', experience: 'tablet', width: 1024, height: 768, path: '/infrastructure' },
  { name: 'tablet-product', experience: 'tablet', width: 1024, height: 768, path: '/product' },
  { name: 'tablet-product-sales-products', experience: 'tablet', width: 1024, height: 768, path: '/product/sales-products' },
  { name: 'tablet-product-sales-product-groups', experience: 'tablet', width: 1024, height: 768, path: '/product/sales-product-groups' },
  { name: 'tablet-product-lifecycle', experience: 'tablet', width: 1024, height: 768, path: '/product/lifecycle' },
  { name: 'tablet-security', experience: 'tablet', width: 1024, height: 768, path: '/security/accounts' },
  { name: 'tablet-data-protection', experience: 'tablet', width: 1024, height: 768, path: '/data-protection' },
  { name: 'mobile-home', experience: 'mobile', width: 390, height: 844, path: '/modules' },
  { name: 'mobile-procurement', experience: 'mobile', width: 390, height: 844, path: '/procurement' },
  { name: 'mobile-outsourced', experience: 'mobile', width: 390, height: 844, path: '/outsourced' },
  { name: 'mobile-processing', experience: 'mobile', width: 390, height: 844, path: '/processing/executions/new' },
  { name: 'mobile-sales', experience: 'mobile', width: 390, height: 844, path: '/sales' },
  { name: 'mobile-sales-handling', experience: 'mobile', width: 390, height: 844, path: '/sales-handling' },
  { name: 'mobile-sales-handling-packaging-items', experience: 'mobile', width: 390, height: 844, path: '/sales-handling/packaging-items' },
  { name: 'mobile-labor', experience: 'mobile', width: 390, height: 844, path: '/labor' },
  { name: 'mobile-finance', experience: 'mobile', width: 390, height: 844, path: '/finance' },
  { name: 'mobile-inventory', experience: 'mobile', width: 390, height: 844, path: '/inventory' },
  { name: 'mobile-inventory-operations', experience: 'mobile', width: 390, height: 844, path: '/inventory/operations' },
  { name: 'mobile-inventory-warehouses', experience: 'mobile', width: 390, height: 844, path: '/inventory/warehouses' },
  { name: 'mobile-inventory-storage-locations', experience: 'mobile', width: 390, height: 844, path: '/inventory/storage-locations' },
  { name: 'mobile-party', experience: 'mobile', width: 390, height: 844, path: '/party' },
  { name: 'mobile-party-customer', experience: 'mobile', width: 390, height: 844, path: '/party/customers' },
  { name: 'mobile-party-outsourced-vendor', experience: 'mobile', width: 390, height: 844, path: '/party/outsourced-vendors' },
  { name: 'mobile-party-farmer', experience: 'mobile', width: 390, height: 844, path: '/party/farmers' },
  { name: 'mobile-party-employee', experience: 'mobile', width: 390, height: 844, path: '/party/employees' },
  { name: 'mobile-infrastructure', experience: 'mobile', width: 390, height: 844, path: '/infrastructure' },
  { name: 'mobile-product', experience: 'mobile', width: 390, height: 844, path: '/product' },
  { name: 'mobile-product-sales-products', experience: 'mobile', width: 390, height: 844, path: '/product/sales-products' },
  { name: 'mobile-product-sales-product-groups', experience: 'mobile', width: 390, height: 844, path: '/product/sales-product-groups' },
  { name: 'mobile-product-lifecycle', experience: 'mobile', width: 390, height: 844, path: '/product/lifecycle' },
  { name: 'mobile-security', experience: 'mobile', width: 390, height: 844, path: '/security/accounts' },
  { name: 'mobile-data-protection', experience: 'mobile', width: 390, height: 844, path: '/data-protection' },
  { name: 'mobile-narrow-home', experience: 'mobile', width: 360, height: 800, path: '/modules' },
];

class CdpClient {
  static async connect(url) {
    const client = new CdpClient(url);
    await client.open();
    return client;
  }

  constructor(url) {
    this.url = url;
    this.sequence = 0;
    this.pending = new Map();
    this.eventWaiters = new Map();
  }

  open() {
    return new Promise((resolve, reject) => {
      this.socket = new WebSocket(this.url);
      const timeout = setTimeout(() => reject(new Error('Timed out connecting to Chrome DevTools Protocol.')), 8_000);
      this.socket.addEventListener('open', () => {
        clearTimeout(timeout);
        resolve();
      }, { once: true });
      this.socket.addEventListener('error', () => {
        clearTimeout(timeout);
        reject(new Error('Failed to connect to Chrome DevTools Protocol.'));
      }, { once: true });
      this.socket.addEventListener('message', (event) => this.onMessage(event.data));
      this.socket.addEventListener('close', () => {
        for (const pending of this.pending.values()) pending.reject(new Error('Chrome DevTools Protocol connection closed.'));
        this.pending.clear();
      });
    });
  }

  send(method, params = {}) {
    const id = ++this.sequence;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.socket.send(JSON.stringify({ id, method, params }));
    });
  }

  waitForEvent(method, timeoutMs) {
    return new Promise((resolve, reject) => {
      const waiter = { resolve, reject };
      const entries = this.eventWaiters.get(method) ?? [];
      entries.push(waiter);
      this.eventWaiters.set(method, entries);
      waiter.timeout = setTimeout(() => {
        const current = this.eventWaiters.get(method) ?? [];
        this.eventWaiters.set(method, current.filter((candidate) => candidate !== waiter));
        reject(new Error(`Timed out waiting for CDP event ${method}.`));
      }, timeoutMs);
    });
  }

  onMessage(data) {
    const message = JSON.parse(String(data));
    if (message.id !== undefined) {
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      if (message.error) pending.reject(new Error(`${message.error.message} (${message.error.code})`));
      else pending.resolve(message.result ?? {});
      return;
    }
    if (message.method) {
      const waiters = this.eventWaiters.get(message.method) ?? [];
      this.eventWaiters.delete(message.method);
      for (const waiter of waiters) {
        clearTimeout(waiter.timeout);
        waiter.resolve(message.params ?? {});
      }
    }
  }

  close() {
    if (this.socket?.readyState === WebSocket.OPEN) this.socket.close();
  }
}

await main();

async function main() {
  if (chromePath === undefined) {
    fail(`Google Chrome is required for visual acceptance. Checked: ${chromeCandidates.join(', ')}`);
    return;
  }
  if (!fs.existsSync(viteBin)) {
    fail(`Vite executable was not found at ${viteBin}. Run pnpm install first.`);
    return;
  }
  if (!fs.existsSync(path.join(webRoot, 'dist', 'index.html'))) {
    fail('Built frontend was not found. Run pnpm build before visual acceptance.');
    return;
  }

  assertPwaShellMetadata();

  const previewPort = await reservePort();
  const cdpPort = await reservePort();
  const authMockPort = await reservePort();
  const baseUrl = `http://127.0.0.1:${previewPort}`;
  const authMockUrl = `http://127.0.0.1:${authMockPort}`;
  const chromeProfile = await mkdtemp(path.join(os.tmpdir(), 'yowthi-erp-visual-'));
  let previewProcess;
  let chromeProcess;
  let authMockServer;
  let cdp;

  try {
    authMockServer = await startAuthMockServer(authMockPort);
    previewProcess = spawn(process.execPath, [
      viteBin,
      'preview',
      '--host', '127.0.0.1',
      '--port', String(previewPort),
      '--strictPort',
    ], {
      cwd: webRoot,
      env: { ...process.env, YOWTHI_ERP_API_PROXY_TARGET: authMockUrl },
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
    });
    captureStderr(previewProcess);
    await waitForHttp(baseUrl, previewProcess, 15_000);

    chromeProcess = spawn(chromePath, [
      '--headless=new',
      '--disable-background-networking',
      '--disable-component-update',
      '--disable-extensions',
      '--disable-sync',
      '--no-first-run',
      '--no-default-browser-check',
      '--remote-debugging-address=127.0.0.1',
      `--remote-debugging-port=${cdpPort}`,
      `--user-data-dir=${chromeProfile}`,
      '--window-size=1440,900',
      'about:blank',
    ], {
      cwd: webRoot,
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
    });
    captureStderr(chromeProcess);

    const target = await waitForPageTarget(cdpPort, chromeProcess, 15_000);
    cdp = await CdpClient.connect(target.webSocketDebuggerUrl);
    await cdp.send('Page.enable');
    await cdp.send('Runtime.enable');

    const results = [];
    for (const acceptanceCase of cases) {
      const result = await runCase(cdp, acceptanceCase, baseUrl);
      results.push(result);
      console.log(`visual acceptance: PASS ${acceptanceCase.name} (${acceptanceCase.width}x${acceptanceCase.height})`);
    }

    await runMobileRouteTransitionAcceptance(cdp, baseUrl);
    console.log('visual acceptance: PASS mobile-route-scroll-reset (390x844)');

    await runPartyLifecycleShellAcceptance(cdp, baseUrl);
    console.log('visual acceptance: PASS party-lifecycle-unified-shell (390x844)');

    console.log('visual acceptance: PASS');
    console.log(`- cases: ${results.length}/${cases.length}`);
    console.log('- experiences: desktop / tablet / mobile');
    console.log('- routes: home / procurement / outsourced supply / processing / sales / sales handling / labor / finance / inventory / party / infrastructure / product / security / data protection');
    console.log('- viewport overflow: none');
    console.log('- zh-TW / th-TH static interface mixing: none');
    console.log('- mobile module menu bounds: accepted');
    console.log('- mobile fixed top shell: accepted');
    console.log('- mobile docked bottom navigation: accepted');
    console.log('- PWA standalone metadata and root scope: accepted');
    console.log('- mobile form controls >= 16px to prevent iOS focus zoom: accepted');
    console.log('- party management master-module navigation: 5/5 visible');
    console.log('- party lifecycle routes use the same master-management shell: accepted');
  } catch (error) {
    fail(error instanceof Error ? error.message : String(error));
  } finally {
    cdp?.close();
    await terminateAndWait(chromeProcess);
    await terminateAndWait(previewProcess);
    await closeServer(authMockServer);
    await rm(chromeProfile, { recursive: true, force: true, maxRetries: 8, retryDelay: 100 });
  }
}

async function runCase(client, acceptanceCase, origin) {
  const { experience, width, height } = acceptanceCase;
  await client.send('Emulation.setDeviceMetricsOverride', {
    width,
    height,
    deviceScaleFactor: 1,
    mobile: experience !== 'desktop',
    screenWidth: width,
    screenHeight: height,
  });

  const url = `${origin}${acceptanceCase.path}?ui=${experience}`;
  await navigateAndWait(client, url);
  await waitForSelector(client, `[data-ui-experience="${experience}"]`, 8_000);
  await delay(250);

  await setLocale(client, 'zh-TW');
  const zhMetrics = await inspectPage(client);
  assertMetrics(acceptanceCase, zhMetrics, 'zh-TW');
  if (/\p{Script=Thai}/u.test(zhMetrics.staticText)) {
    throw new Error(`${acceptanceCase.name}: Thai characters remain in visible zh-TW interface text.`);
  }

  await setLocale(client, 'th-TH');
  const thMetrics = await inspectPage(client);
  assertMetrics(acceptanceCase, thMetrics, 'th-TH');
  if (/\p{Script=Han}/u.test(thMetrics.staticText)) {
    throw new Error(`${acceptanceCase.name}: Han characters remain in visible th-TH interface text: ${compact(thMetrics.staticText)}`);
  }

  if (experience === 'mobile') {
    const menuMetrics = await openAndInspectMobileMenu(client);
    if (!menuMetrics.exists) {
      throw new Error(`${acceptanceCase.name}: mobile module menu did not open.`);
    }
    if (menuMetrics.left < -1 || menuMetrics.right > width + 1 || menuMetrics.top < -1 || menuMetrics.bottom > height + 1) {
      throw new Error(`${acceptanceCase.name}: mobile module menu escapes viewport: ${JSON.stringify(menuMetrics)}.`);
    }
  }

  await setLocale(client, 'zh-TW');
  return { zhMetrics, thMetrics };
}

async function runMobileRouteTransitionAcceptance(client, origin) {
  const width = 390;
  const height = 844;
  await client.send('Emulation.setDeviceMetricsOverride', {
    width,
    height,
    deviceScaleFactor: 1,
    mobile: true,
    screenWidth: width,
    screenHeight: height,
  });

  await navigateAndWait(client, `${origin}/modules?ui=mobile`);
  await waitForSelector(client, '[data-ui-experience="mobile"]', 8_000);
  await setLocale(client, 'zh-TW');
  await delay(250);

  const scrollSetup = await evaluate(client, `(() => {
    const scroller = document.scrollingElement;
    if (!scroller) return { maxScroll: 0, target: 0 };
    const maxScroll = Math.max(0, scroller.scrollHeight - innerHeight);
    const target = Math.min(600, maxScroll);
    scroller.scrollTop = target;
    window.scrollTo(0, target);
    return { maxScroll, target };
  })()`);
  await delay(100);

  if (scrollSetup.maxScroll < 200) {
    throw new Error(`mobile-route-scroll-reset: test page is not tall enough (${scrollSetup.maxScroll}px scroll range).`);
  }

  const beforeScroll = await evaluate(client, 'window.scrollY');
  if (beforeScroll < 200) {
    throw new Error(`mobile-route-scroll-reset: test page did not reach the requested pre-navigation scroll (${beforeScroll}/${scrollSetup.target}).`);
  }

  const clicked = await evaluate(client, `(() => {
    const link = [...document.querySelectorAll('a')].find((anchor) => new URL(anchor.href).pathname === '/party');
    if (!link) return false;
    link.click();
    return true;
  })()`);
  if (!clicked) throw new Error('mobile-route-scroll-reset: party module link was not found.');

  const deadline = Date.now() + 4_000;
  while (Date.now() < deadline) {
    const pathname = await evaluate(client, 'location.pathname');
    if (pathname === '/party') break;
    await delay(50);
  }
  await waitForSelector(client, '.nature-mobile-shell-header', 4_000);
  await delay(150);

  const metrics = await evaluate(client, `(() => {
    const header = document.querySelector('.nature-mobile-shell-header');
    return {
      pathname: location.pathname,
      scrollY: window.scrollY,
      headerTop: header?.getBoundingClientRect().top ?? null,
    };
  })()`);

  if (metrics.pathname !== '/party') {
    throw new Error(`mobile-route-scroll-reset: navigation ended at ${metrics.pathname}.`);
  }
  if (metrics.scrollY > 1) {
    throw new Error(`mobile-route-scroll-reset: route retained scrollY=${metrics.scrollY}; expected top of page.`);
  }
  if (metrics.headerTop === null || Math.abs(metrics.headerTop) > 1) {
    throw new Error(`mobile-route-scroll-reset: shell header top is ${metrics.headerTop}; expected viewport top.`);
  }
}

async function runPartyLifecycleShellAcceptance(client, origin) {
  const width = 390;
  const height = 844;
  await client.send('Emulation.setDeviceMetricsOverride', {
    width,
    height,
    deviceScaleFactor: 1,
    mobile: true,
    screenWidth: width,
    screenHeight: height,
  });

  await navigateAndWait(client, `${origin}/party/lifecycle?kind=farmers&ui=mobile`);
  await waitForSelector(client, '[data-ui-experience="mobile"]', 8_000);
  await waitForSelector(client, '.supplier-master-prototype', 8_000);
  await setLocale(client, 'zh-TW');
  await delay(150);

  const metrics = await evaluate(client, `(() => {
    const nav = document.querySelector('.party-master-kind-switcher');
    const active = nav?.querySelector('.is-active');
    return {
      pathname: location.pathname,
      kind: new URLSearchParams(location.search).get('kind'),
      heading: document.querySelector('.supplier-master-header h1')?.textContent?.trim() ?? null,
      navCount: nav?.querySelectorAll(':scope > a, :scope > span').length ?? 0,
      activeText: active?.textContent?.trim() ?? null,
      unifiedShell: Boolean(document.querySelector('.supplier-master-header') && document.querySelector('.supplier-master-grid')),
      legacyShellPresent: Boolean(document.querySelector('.procurement-page')),
    };
  })()`);

  if (metrics.pathname !== '/party/lifecycle' || metrics.kind !== 'farmers') {
    throw new Error(`party-lifecycle-unified-shell: route identity mismatch ${metrics.pathname}?kind=${metrics.kind}.`);
  }
  if (metrics.heading !== '農戶' || metrics.activeText !== '農戶') {
    throw new Error(`party-lifecycle-unified-shell: requested farmers but heading/active are ${metrics.heading}/${metrics.activeText}.`);
  }
  if (metrics.navCount !== 5) {
    throw new Error(`party-lifecycle-unified-shell: expected 5 partner master entries, found ${metrics.navCount}.`);
  }
  if (!metrics.unifiedShell || metrics.legacyShellPresent) {
    throw new Error(`party-lifecycle-unified-shell: lifecycle route is not using the unified master shell: ${JSON.stringify(metrics)}.`);
  }
}

function assertMetrics(acceptanceCase, metrics, locale) {
  const { width, height, experience, name } = acceptanceCase;
  if (metrics.experience !== experience) {
    throw new Error(`${name}: expected ${experience} shell, received ${metrics.experience ?? 'none'}.`);
  }
  if (metrics.lang !== locale) {
    throw new Error(`${name}: document language is ${metrics.lang}, expected ${locale}.`);
  }
  if (metrics.viewportWidth !== width || metrics.viewportHeight !== height) {
    throw new Error(`${name}: viewport mismatch ${metrics.viewportWidth}x${metrics.viewportHeight}, expected ${width}x${height}.`);
  }
  if (metrics.documentScrollWidth > width + 1 || metrics.bodyScrollWidth > width + 1) {
    throw new Error(`${name}: horizontal document overflow (${metrics.documentScrollWidth}/${metrics.bodyScrollWidth} > ${width}).`);
  }
  if (metrics.main.left < -1 || metrics.main.right > width + 1 || metrics.main.width <= 0) {
    throw new Error(`${name}: main workspace escapes viewport: ${JSON.stringify(metrics.main)}.`);
  }
  if (metrics.overflowingInteractives.length > 0) {
    throw new Error(`${name}: interactive controls escape viewport: ${JSON.stringify(metrics.overflowingInteractives.slice(0, 5))}.`);
  }
  if (acceptanceCase.path === '/party' && metrics.partyMasterKindCount !== 5) {
    throw new Error(`${name}: party management must expose all 5 master modules; found ${metrics.partyMasterKindCount}.`);
  }
  if (experience === 'desktop') {
    if (!metrics.desktopSidebar || metrics.desktopSidebar.left < -1 || metrics.desktopSidebar.right > 280) {
      throw new Error(`${name}: desktop sidebar geometry is invalid: ${JSON.stringify(metrics.desktopSidebar)}.`);
    }
    if (metrics.desktopSidebarBackground === metrics.desktopBrandBackground) {
      throw new Error(`${name}: desktop sidebar body still uses the same solid green background as the brand block.`);
    }
  }
  if (experience === 'tablet') {
    if (!metrics.tabletNavigation || metrics.tabletNavigation.left < -1 || metrics.tabletNavigation.right > width + 1) {
      throw new Error(`${name}: tablet navigation escapes viewport: ${JSON.stringify(metrics.tabletNavigation)}.`);
    }
  }
  if (experience === 'mobile') {
    if (!metrics.mobileHeader || metrics.mobileHeader.left < -1 || metrics.mobileHeader.right > width + 1) {
      throw new Error(`${name}: mobile header escapes viewport: ${JSON.stringify(metrics.mobileHeader)}.`);
    }
    if (metrics.mobileHeaderPosition !== 'fixed' || Math.abs(metrics.mobileHeader.top) > 1) {
      throw new Error(`${name}: mobile header must remain fixed at viewport top; position=${metrics.mobileHeaderPosition ?? 'unset'}, rect=${JSON.stringify(metrics.mobileHeader)}.`);
    }
    if (!metrics.mobileBottomNavigation || metrics.mobileBottomNavigation.left < -1 || metrics.mobileBottomNavigation.right > width + 1 || metrics.mobileBottomNavigation.bottom > height + 1) {
      throw new Error(`${name}: mobile bottom navigation escapes viewport: ${JSON.stringify(metrics.mobileBottomNavigation)}.`);
    }
    if (metrics.mobileBottomPosition !== 'fixed') {
      throw new Error(`${name}: mobile bottom navigation position is ${metrics.mobileBottomPosition ?? 'unset'}, expected fixed.`);
    }
    if (Math.abs(metrics.mobileBottomNavigation.left) > 1 || Math.abs(metrics.mobileBottomNavigation.right - width) > 1 || Math.abs(metrics.mobileBottomNavigation.bottom - height) > 1) {
      throw new Error(`${name}: mobile bottom navigation must be docked edge-to-edge at the viewport bottom: ${JSON.stringify(metrics.mobileBottomNavigation)}.`);
    }
    if (metrics.mobileBottomItemCount !== 4) {
      throw new Error(`${name}: mobile bottom navigation must expose exactly 4 entries; found ${metrics.mobileBottomItemCount}.`);
    }
    if (metrics.mobileUndersizedFormControls.length > 0) {
      throw new Error(`${name}: mobile form controls below 16px can trigger iOS focus zoom: ${JSON.stringify(metrics.mobileUndersizedFormControls.slice(0, 5))}.`);
    }
  }
}

async function inspectPage(client) {
  return evaluate(client, `(() => {
    const visible = (element) => {
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
    };
    const rect = (element) => element ? (() => {
      const value = element.getBoundingClientRect();
      return { left: value.left, top: value.top, right: value.right, bottom: value.bottom, width: value.width, height: value.height };
    })() : null;
    const staticSelectors = [
      'h1', 'h2', 'h3', 'button', 'label:not(.locale-control)', '.field-label',
      '.reference-desktop-navigation a', '.nature-tablet-navigation a',
      '.nature-mobile-bottom-navigation a', '.nature-mobile-navigation-links a',
      '.nature-mobile-menu-home', '.workspace-topbar strong', '.nature-mobile-toolbar strong'
    ];
    const staticText = [...document.querySelectorAll(staticSelectors.join(','))]
      .filter(visible)
      .map((element) => element.innerText.trim())
      .filter(Boolean)
      .join('\\n');
    const overflowingInteractives = [...document.querySelectorAll('input, select, button, a')]
      .filter(visible)
      .filter((element) => !element.closest('.nature-tablet-navigation'))
      .map((element) => ({ element, rect: element.getBoundingClientRect() }))
      .filter(({ rect }) => rect.left < -1 || rect.right > innerWidth + 1)
      .map(({ element, rect }) => ({
        tag: element.tagName,
        text: (element.innerText || element.getAttribute('aria-label') || '').trim().slice(0, 80),
        left: rect.left,
        right: rect.right,
        width: rect.width,
      }));
    const shell = document.querySelector('[data-ui-experience]');
    const sidebar = document.querySelector('.desktop-sidebar');
    const brand = document.querySelector('.desktop-sidebar-brand');
    const tabletNavigation = document.querySelector('.nature-tablet-navigation');
    const mobileHeader = document.querySelector('.nature-mobile-shell-header');
    const mobileBottomNavigation = document.querySelector('.nature-mobile-bottom-navigation');
    const mobileUndersizedFormControls = [...document.querySelectorAll('input, select, textarea')]
      .filter(visible)
      .map((element) => ({
        tag: element.tagName,
        type: element.getAttribute('type') ?? '',
        fontSize: Number.parseFloat(getComputedStyle(element).fontSize),
      }))
      .filter((item) => Number.isFinite(item.fontSize) && item.fontSize < 16);
    return {
      experience: shell?.getAttribute('data-ui-experience') ?? null,
      lang: document.documentElement.lang,
      viewportWidth: innerWidth,
      viewportHeight: innerHeight,
      documentScrollWidth: document.documentElement.scrollWidth,
      bodyScrollWidth: document.body.scrollWidth,
      main: rect(document.querySelector('main')),
      staticText,
      overflowingInteractives,
      desktopSidebar: rect(sidebar),
      desktopSidebarBackground: sidebar ? getComputedStyle(sidebar).backgroundColor : null,
      desktopBrandBackground: brand ? getComputedStyle(brand).backgroundColor : null,
      tabletNavigation: rect(tabletNavigation),
      mobileHeader: rect(mobileHeader),
      mobileHeaderPosition: mobileHeader ? getComputedStyle(mobileHeader).position : null,
      mobileBottomNavigation: rect(mobileBottomNavigation),
      mobileBottomPosition: mobileBottomNavigation ? getComputedStyle(mobileBottomNavigation).position : null,
      mobileBottomItemCount: mobileBottomNavigation ? mobileBottomNavigation.querySelectorAll(':scope > a').length : 0,
      mobileUndersizedFormControls,
      partyMasterKindCount: document.querySelectorAll('.party-master-kind-switcher > a, .party-master-kind-switcher > span').length,
    };
  })()`);
}

async function openAndInspectMobileMenu(client) {
  const opened = await evaluate(client, `(() => {
    const button = document.querySelector('.nature-mobile-menu-trigger');
    if (!button) return false;
    if (button.getAttribute('aria-expanded') !== 'true') button.click();
    return true;
  })()`);
  if (!opened) return { exists: false };
  await delay(100);
  return evaluate(client, `(() => {
    const menu = document.querySelector('.nature-mobile-module-menu');
    if (!menu) return { exists: false };
    const rect = menu.getBoundingClientRect();
    return { exists: true, left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height };
  })()`);
}

async function setLocale(client, locale) {
  const changed = await evaluate(client, `(() => {
    const select = document.querySelector('.locale-control select');
    if (!select) return false;
    select.value = ${JSON.stringify(locale)};
    select.dispatchEvent(new Event('change', { bubbles: true }));
    return true;
  })()`);
  if (!changed) throw new Error('LocaleControl select was not found.');
  const deadline = Date.now() + 3_000;
  while (Date.now() < deadline) {
    const lang = await evaluate(client, 'document.documentElement.lang');
    if (lang === locale) return;
    await delay(50);
  }
  throw new Error(`Timed out switching interface locale to ${locale}.`);
}

async function navigateAndWait(client, url) {
  const loaded = client.waitForEvent('Page.loadEventFired', 8_000);
  const response = await client.send('Page.navigate', { url });
  if (response.errorText) throw new Error(`Chrome navigation failed: ${response.errorText}`);
  await loaded;
}

async function waitForSelector(client, selector, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const exists = await evaluate(client, `Boolean(document.querySelector(${JSON.stringify(selector)}))`);
    if (exists) return;
    await delay(75);
  }
  throw new Error(`Timed out waiting for ${selector}.`);
}

async function evaluate(client, expression) {
  const response = await client.send('Runtime.evaluate', {
    expression,
    returnByValue: true,
    awaitPromise: true,
  });
  if (response.exceptionDetails) {
    const description = response.exceptionDetails.exception?.description ?? response.exceptionDetails.text;
    throw new Error(`Browser evaluation failed: ${description}`);
  }
  return response.result?.value;
}

function assertPwaShellMetadata() {
  const distRoot = path.join(webRoot, 'dist');
  const indexPath = path.join(distRoot, 'index.html');
  const manifestPath = path.join(distRoot, 'manifest.webmanifest');
  const indexHtml = fs.readFileSync(indexPath, 'utf8');

  if (!indexHtml.includes('name="apple-mobile-web-app-capable" content="yes"')) {
    throw new Error('PWA acceptance: apple-mobile-web-app-capable=yes is missing.');
  }
  if (!indexHtml.includes('name="mobile-web-app-capable" content="yes"')) {
    throw new Error('PWA acceptance: mobile-web-app-capable=yes is missing.');
  }
  if (!indexHtml.includes('viewport-fit=cover')) {
    throw new Error('PWA acceptance: viewport-fit=cover is missing.');
  }
  if (!indexHtml.includes('rel="manifest" href="/manifest.webmanifest"')) {
    throw new Error('PWA acceptance: manifest link is missing.');
  }
  if (!fs.existsSync(manifestPath)) {
    throw new Error('PWA acceptance: dist/manifest.webmanifest is missing.');
  }

  const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  if (manifest.display !== 'standalone') {
    throw new Error(`PWA acceptance: display is ${manifest.display ?? 'unset'}, expected standalone.`);
  }
  if (manifest.scope !== '/') {
    throw new Error(`PWA acceptance: scope is ${manifest.scope ?? 'unset'}, expected root scope.`);
  }
  if (manifest.start_url !== '/modules') {
    throw new Error(`PWA acceptance: start_url is ${manifest.start_url ?? 'unset'}, expected /modules.`);
  }
}

async function startAuthMockServer(port) {
  const server = createServer((request, response) => {
    response.setHeader('Content-Type', 'application/json; charset=utf-8');

    if (request.method === 'GET' && request.url?.startsWith('/auth/session')) {
      response.statusCode = 200;
      response.end(JSON.stringify({
        authenticated: true,
        accountId: '01900000-0000-7000-8000-000000000001',
        displayName: 'Visual Acceptance Admin',
        capabilities: ['security.account.manage'],
        developmentLoginAvailable: false,
      }));
      return;
    }

    if (request.method === 'GET' && request.url?.startsWith('/api/v1/security/accounts')) {
      response.statusCode = 200;
      response.end(JSON.stringify({
        items: [],
        availableCapabilities: ['security.account.manage'],
      }));
      return;
    }

    response.statusCode = 404;
    response.end(JSON.stringify({ status: 404 }));
  });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, '127.0.0.1', resolve);
  });
  return server;
}

async function closeServer(server) {
  if (!server?.listening) return;
  await new Promise((resolve) => server.close(resolve));
}

async function reservePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.unref();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      if (typeof address !== 'object' || address === null) {
        server.close();
        reject(new Error('Failed to allocate a loopback port.'));
        return;
      }
      const port = address.port;
      server.close((error) => error ? reject(error) : resolve(port));
    });
  });
}

async function waitForHttp(url, processHandle, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (processHandle.exitCode !== null) throw new Error(`Process exited before ${url} became available: ${processHandle.__stderr ?? ''}`);
    try {
      const response = await fetch(url);
      if (response.ok) return;
    } catch {
      // Retry until the bounded deadline.
    }
    await delay(100);
  }
  throw new Error(`Timed out waiting for ${url}.`);
}

async function waitForPageTarget(port, processHandle, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (processHandle.exitCode !== null) throw new Error(`Chrome exited before a DevTools page target became available: ${processHandle.__stderr ?? ''}`);
    try {
      const response = await fetch(`http://127.0.0.1:${port}/json/list`);
      if (response.ok) {
        const targets = await response.json();
        const page = targets.find((target) => target.type === 'page' && target.webSocketDebuggerUrl);
        if (page) return page;
      }
    } catch {
      // Retry until the bounded deadline.
    }
    await delay(100);
  }
  throw new Error('Timed out waiting for Chrome DevTools page target.');
}

function captureStderr(processHandle) {
  processHandle.__stderr = '';
  processHandle.stderr?.setEncoding('utf8');
  processHandle.stderr?.on('data', (chunk) => {
    processHandle.__stderr += chunk;
    if (processHandle.__stderr.length > 16_000) processHandle.__stderr = processHandle.__stderr.slice(-16_000);
  });
}

async function terminateAndWait(processHandle) {
  if (!processHandle || processHandle.exitCode !== null) return;
  const exited = new Promise((resolve) => processHandle.once('exit', resolve));
  try {
    processHandle.kill();
  } catch {
    return;
  }
  await Promise.race([exited, delay(2_000)]);
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function compact(text) {
  return text.replace(/\s+/g, ' ').trim().slice(0, 240);
}

function fail(message) {
  console.error(`visual acceptance: FAIL\n${message}`);
  process.exitCode = 1;
}