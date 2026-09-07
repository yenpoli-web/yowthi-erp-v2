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
const prototypeStorageKey = 'yowthi.erp.prototype.suppliers.v1';
const testName = '供應商操作測試';

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
        reject(new Error(`Timed out waiting for ${method}.`));
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
  if (!chromePath) throw new Error('Google Chrome is required.');
  const previewPort = await reservePort();
  const cdpPort = await reservePort();
  const baseUrl = `http://127.0.0.1:${previewPort}`;
  const chromeProfile = await mkdtemp(path.join(os.tmpdir(), 'yowthi-supplier-prototype-'));
  let previewProcess;
  let chromeProcess;
  let cdp;

  try {
    previewProcess = spawn(process.execPath, [viteBin, 'preview', '--host', '127.0.0.1', '--port', String(previewPort), '--strictPort'], {
      cwd: webRoot,
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
    });
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
    ], { cwd: webRoot, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });

    const target = await waitForPageTarget(cdpPort, chromeProcess, 15_000);
    cdp = await CdpClient.connect(target.webSocketDebuggerUrl);
    await cdp.send('Page.enable');
    await cdp.send('Runtime.enable');

    await navigateAndWait(cdp, `${baseUrl}/modules?ui=desktop`);
    await evaluate(cdp, `sessionStorage.removeItem(${JSON.stringify(prototypeStorageKey)})`);
    await navigateAndWait(cdp, `${baseUrl}/party?ui=desktop`);
    await waitFor(cdp, `Boolean(document.querySelector('.supplier-master-prototype'))`, 8_000);

    await setLocale(cdp, 'zh-TW');
    await evaluate(cdp, `Object.defineProperty(globalThis.crypto, 'randomUUID', { configurable: true, value: undefined })`);
    await click(cdp, '.supplier-master-header .supplier-primary-action');
    await waitFor(cdp, `Boolean(document.querySelector('.supplier-editor-footer .supplier-primary-action'))`, 4_000);

    const footerPlacement = await evaluate(cdp, `(() => {
      const footer = document.querySelector('.supplier-editor-footer');
      const inputs = [...document.querySelectorAll('.supplier-editor input, .supplier-editor textarea')];
      const footerRect = footer.getBoundingClientRect();
      const lastBottom = Math.max(...inputs.map((element) => element.getBoundingClientRect().bottom));
      return { footerTop: footerRect.top, lastBottom };
    })()`);
    if (!(footerPlacement.footerTop > footerPlacement.lastBottom)) {
      throw new Error(`Save footer is not below the form fields: ${JSON.stringify(footerPlacement)}`);
    }

    await setInput(cdp, '.supplier-form-grid input', testName);
    await click(cdp, '.supplier-editor-footer .supplier-primary-action');
    await waitFor(cdp, `document.querySelector('.supplier-detail-header h2')?.textContent?.includes(${JSON.stringify(testName)}) === true`, 4_000);
    await waitFor(cdp, `[...document.querySelectorAll('.supplier-list-name strong')].some((element) => element.textContent?.includes(${JSON.stringify(testName)}))`, 4_000);

    await navigateAndWait(cdp, `${baseUrl}/party?ui=desktop`);
    await waitFor(cdp, `[...document.querySelectorAll('.supplier-list-name strong')].some((element) => element.textContent?.includes(${JSON.stringify(testName)}))`, 4_000);
    await clickContaining(cdp, '.supplier-list-item', testName);
    await click(cdp, '.supplier-detail-actions .supplier-danger-ghost-action');
    await waitFor(cdp, `Boolean(document.querySelector('.supplier-dialog .supplier-danger-action'))`, 2_000);
    await click(cdp, '.supplier-dialog .supplier-danger-action');
    await waitFor(cdp, `[...document.querySelectorAll('.supplier-status-pill')].some((element) => element.textContent?.includes('已刪除'))`, 4_000);

    await navigateAndWait(cdp, `${baseUrl}/data-protection?ui=desktop`);
    await waitFor(cdp, `Boolean(document.querySelector('.protection-prototype'))`, 4_000);
    await waitFor(cdp, `[...document.querySelectorAll('.protection-list-name strong')].some((element) => element.textContent?.includes(${JSON.stringify(testName)}))`, 4_000);
    await clickContaining(cdp, '.protection-list-item', testName);
    await click(cdp, '.protection-detail-footer .protection-danger-action');
    await waitFor(cdp, `Boolean(document.querySelector('.protection-dialog .protection-danger-action'))`, 2_000);
    await click(cdp, '.protection-dialog .protection-danger-action');
    await waitFor(cdp, `![...document.querySelectorAll('.protection-list-name strong')].some((element) => element.textContent?.includes(${JSON.stringify(testName)}))`, 4_000);

    await navigateAndWait(cdp, `${baseUrl}/data-protection?ui=desktop`);
    await waitFor(cdp, `Boolean(document.querySelector('.protection-prototype'))`, 4_000);
    const remainsAfterReload = await evaluate(cdp, `[...document.querySelectorAll('.protection-list-name strong')].some((element) => element.textContent?.includes(${JSON.stringify(testName)}))`);
    if (remainsAfterReload) throw new Error('Hard-deleted prototype supplier returned after reload.');

    console.log('supplier prototype interaction acceptance: PASS');
    console.log('- create + bottom save: PASS');
    console.log('- create works without crypto.randomUUID: PASS');
    console.log('- save survives full page reload within browser session: PASS');
    console.log('- soft delete appears in data protection: PASS');
    console.log('- hard delete removes protected record: PASS');
  } finally {
    cdp?.close();
    await terminateAndWait(chromeProcess);
    await terminateAndWait(previewProcess);
    await rm(chromeProfile, { recursive: true, force: true, maxRetries: 8, retryDelay: 100 });
  }
}

async function click(client, selector) {
  const result = await evaluate(client, `(() => { const element = document.querySelector(${JSON.stringify(selector)}); if (!element) return false; element.click(); return true; })()`);
  if (!result) throw new Error(`Element not found: ${selector}`);
}

async function clickContaining(client, selector, text) {
  const result = await evaluate(client, `(() => {
    const element = [...document.querySelectorAll(${JSON.stringify(selector)})].find((candidate) => candidate.textContent?.includes(${JSON.stringify(text)}));
    if (!element) return false;
    element.click();
    return true;
  })()`);
  if (!result) throw new Error(`Element containing text was not found: ${selector} / ${text}`);
}

async function setInput(client, selector, value) {
  const result = await evaluate(client, `(() => {
    const element = document.querySelector(${JSON.stringify(selector)});
    if (!(element instanceof HTMLInputElement)) return false;
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
    setter.call(element, ${JSON.stringify(value)});
    element.dispatchEvent(new Event('input', { bubbles: true }));
    element.dispatchEvent(new Event('change', { bubbles: true }));
    return true;
  })()`);
  if (!result) throw new Error(`Input not found: ${selector}`);
}

async function setLocale(client, locale) {
  const changed = await evaluate(client, `(() => {
    const select = document.querySelector('.locale-control select');
    if (!select) return false;
    select.value = ${JSON.stringify(locale)};
    select.dispatchEvent(new Event('change', { bubbles: true }));
    return true;
  })()`);
  if (!changed) throw new Error('Locale control not found.');
  await waitFor(client, `document.documentElement.lang === ${JSON.stringify(locale)}`, 3_000);
}

async function waitFor(client, expression, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (await evaluate(client, expression)) return;
    await delay(60);
  }
  throw new Error(`Timed out waiting for expression: ${expression}`);
}

async function navigateAndWait(client, url) {
  const loaded = client.waitForEvent('Page.loadEventFired', 8_000);
  const response = await client.send('Page.navigate', { url });
  if (response.errorText) throw new Error(`Navigation failed: ${response.errorText}`);
  await loaded;
}

async function evaluate(client, expression) {
  const response = await client.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
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
      if (typeof address !== 'object' || address === null) return reject(new Error('Could not reserve port.'));
      const port = address.port;
      server.close((error) => error ? reject(error) : resolve(port));
    });
  });
}

async function waitForHttp(url, processHandle, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (processHandle.exitCode !== null) throw new Error(`Preview exited early (${processHandle.exitCode}).`);
    try {
      const response = await fetch(url);
      if (response.ok) return;
    } catch {
      // Retry until bounded deadline.
    }
    await delay(100);
  }
  throw new Error(`Timed out waiting for ${url}.`);
}

async function waitForPageTarget(port, processHandle, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (processHandle.exitCode !== null) throw new Error(`Chrome exited early (${processHandle.exitCode}).`);
    try {
      const response = await fetch(`http://127.0.0.1:${port}/json/list`);
      if (response.ok) {
        const targets = await response.json();
        const page = targets.find((target) => target.type === 'page' && target.webSocketDebuggerUrl);
        if (page) return page;
      }
    } catch {
      // Retry until bounded deadline.
    }
    await delay(100);
  }
  throw new Error('Timed out waiting for Chrome DevTools target.');
}

async function terminateAndWait(processHandle) {
  if (!processHandle || processHandle.exitCode !== null) return;
  const exited = new Promise((resolve) => processHandle.once('exit', resolve));
  try { processHandle.kill(); } catch { return; }
  await Promise.race([exited, delay(2_000)]);
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
