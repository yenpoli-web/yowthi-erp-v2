import { spawn } from 'node:child_process';
import fs from 'node:fs';
import { mkdtemp, rm } from 'node:fs/promises';
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
  { name: 'desktop-procurement', experience: 'desktop', width: 1440, height: 900, path: '/procurement/entries/new' },
  { name: 'desktop-outsourced', experience: 'desktop', width: 1440, height: 900, path: '/outsourced/supply-details/new' },
  { name: 'desktop-processing', experience: 'desktop', width: 1440, height: 900, path: '/processing/executions/new' },
  { name: 'desktop-sales', experience: 'desktop', width: 1440, height: 900, path: '/sales' },
  { name: 'tablet-home', experience: 'tablet', width: 1024, height: 768, path: '/modules' },
  { name: 'tablet-procurement', experience: 'tablet', width: 1024, height: 768, path: '/procurement/entries/new' },
  { name: 'tablet-outsourced', experience: 'tablet', width: 1024, height: 768, path: '/outsourced/supply-details/new' },
  { name: 'tablet-processing', experience: 'tablet', width: 1024, height: 768, path: '/processing/executions/new' },
  { name: 'tablet-sales', experience: 'tablet', width: 1024, height: 768, path: '/sales' },
  { name: 'mobile-home', experience: 'mobile', width: 390, height: 844, path: '/modules' },
  { name: 'mobile-procurement', experience: 'mobile', width: 390, height: 844, path: '/procurement/entries/new' },
  { name: 'mobile-outsourced', experience: 'mobile', width: 390, height: 844, path: '/outsourced/supply-details/new' },
  { name: 'mobile-processing', experience: 'mobile', width: 390, height: 844, path: '/processing/executions/new' },
  { name: 'mobile-sales', experience: 'mobile', width: 390, height: 844, path: '/sales' },
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

  const previewPort = await reservePort();
  const cdpPort = await reservePort();
  const baseUrl = `http://127.0.0.1:${previewPort}`;
  const chromeProfile = await mkdtemp(path.join(os.tmpdir(), 'yowthi-erp-visual-'));
  let previewProcess;
  let chromeProcess;
  let cdp;

  try {
    previewProcess = spawn(process.execPath, [
      viteBin,
      'preview',
      '--host', '127.0.0.1',
      '--port', String(previewPort),
      '--strictPort',
    ], {
      cwd: webRoot,
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

    console.log('visual acceptance: PASS');
    console.log(`- cases: ${results.length}/${cases.length}`);
    console.log('- experiences: desktop / tablet / mobile');
    console.log('- routes: home / procurement / outsourced supply / processing / sales');
    console.log('- viewport overflow: none');
    console.log('- zh-TW / th-TH static interface mixing: none');
    console.log('- mobile module menu bounds: accepted');
    console.log('- mobile bottom navigation bounds: accepted');
  } catch (error) {
    fail(error instanceof Error ? error.message : String(error));
  } finally {
    cdp?.close();
    await terminateAndWait(chromeProcess);
    await terminateAndWait(previewProcess);
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
    if (!metrics.mobileBottomNavigation || metrics.mobileBottomNavigation.left < -1 || metrics.mobileBottomNavigation.right > width + 1 || metrics.mobileBottomNavigation.bottom > height + 1) {
      throw new Error(`${name}: mobile bottom navigation escapes viewport: ${JSON.stringify(metrics.mobileBottomNavigation)}.`);
    }
    if (metrics.mobileBottomItemCount !== 4) {
      throw new Error(`${name}: mobile bottom navigation must expose exactly 4 entries; found ${metrics.mobileBottomItemCount}.`);
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
      'h1', 'h2', 'h3', 'button', 'label:not(.locale-control)',
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
    const mobileBottomNavigation = document.querySelector('.nature-mobile-bottom-navigation');
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
      mobileBottomNavigation: rect(mobileBottomNavigation),
      mobileBottomItemCount: mobileBottomNavigation ? mobileBottomNavigation.querySelectorAll(':scope > a').length : 0,
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