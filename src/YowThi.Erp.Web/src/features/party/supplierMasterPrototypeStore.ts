import { useSyncExternalStore } from 'react';

export type PrototypeSupplier = {
  id: string;
  nameZhTw: string;
  nameThTh: string;
  phone: string;
  address: string;
  bankName: string;
  bankAccount: string;
  active: boolean;
  deleted: boolean;
  deletedAt: string | null;
  rowVersion: number;
};

export type PrototypeSupplierDraft = Pick<
  PrototypeSupplier,
  'nameZhTw' | 'nameThTh' | 'phone' | 'address' | 'bankName' | 'bankAccount' | 'active'
>;

const storageKey = 'yowthi.erp.prototype.suppliers.v1';

const initialSuppliers: PrototypeSupplier[] = [
  {
    id: 'prototype-supplier-001',
    nameZhTw: '清邁農產合作社',
    nameThTh: 'สหกรณ์เกษตรเชียงใหม่',
    phone: '053-245-810',
    address: 'Chiang Mai',
    bankName: 'Kasikornbank',
    bankAccount: '123-4-56789-0',
    active: true,
    deleted: false,
    deletedAt: null,
    rowVersion: 4,
  },
  {
    id: 'prototype-supplier-002',
    nameZhTw: '北泰包材',
    nameThTh: 'นอร์ทเทิร์นแพ็ก',
    phone: '081-782-4451',
    address: 'Lamphun',
    bankName: 'Bangkok Bank',
    bankAccount: '215-0-77884-2',
    active: true,
    deleted: false,
    deletedAt: null,
    rowVersion: 2,
  },
  {
    id: 'prototype-supplier-003',
    nameZhTw: '綠源農材',
    nameThTh: 'กรีนซอร์ส',
    phone: '089-530-1148',
    address: 'Chiang Rai',
    bankName: 'Krungthai Bank',
    bankAccount: '510-1-90344-6',
    active: false,
    deleted: false,
    deletedAt: null,
    rowVersion: 7,
  },
  {
    id: 'prototype-supplier-004',
    nameZhTw: '舊供應商示例',
    nameThTh: 'ตัวอย่างผู้จำหน่ายเดิม',
    phone: '086-220-9971',
    address: 'Lampang',
    bankName: 'SCB',
    bankAccount: '409-2-11880-3',
    active: false,
    deleted: true,
    deletedAt: '2026-09-01T09:30:00.000Z',
    rowVersion: 5,
  },
];

function isPrototypeSupplier(value: unknown): value is PrototypeSupplier {
  if (typeof value !== 'object' || value === null) return false;
  const item = value as Record<string, unknown>;
  return typeof item.id === 'string'
    && typeof item.nameZhTw === 'string'
    && typeof item.nameThTh === 'string'
    && typeof item.phone === 'string'
    && typeof item.address === 'string'
    && typeof item.bankName === 'string'
    && typeof item.bankAccount === 'string'
    && typeof item.active === 'boolean'
    && typeof item.deleted === 'boolean'
    && (typeof item.deletedAt === 'string' || item.deletedAt === null)
    && typeof item.rowVersion === 'number';
}

function loadInitialSuppliers(): PrototypeSupplier[] {
  if (typeof window === 'undefined') return initialSuppliers.map((item) => ({ ...item }));
  try {
    const raw = window.sessionStorage.getItem(storageKey);
    if (raw === null) return initialSuppliers.map((item) => ({ ...item }));
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed) || !parsed.every(isPrototypeSupplier)) {
      return initialSuppliers.map((item) => ({ ...item }));
    }
    return parsed.map((item) => ({ ...item }));
  } catch {
    return initialSuppliers.map((item) => ({ ...item }));
  }
}

let suppliers = loadInitialSuppliers();
const listeners = new Set<() => void>();

function persist() {
  if (typeof window === 'undefined') return;
  try {
    window.sessionStorage.setItem(storageKey, JSON.stringify(suppliers));
  } catch {
    // Prototype persistence is best-effort and must not block UI interaction.
  }
}

function emit() {
  for (const listener of listeners) listener();
}

function updateSnapshot(next: PrototypeSupplier[]) {
  suppliers = next;
  persist();
  emit();
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function getPrototypeSuppliers(): PrototypeSupplier[] {
  return suppliers;
}

export function usePrototypeSuppliers(): PrototypeSupplier[] {
  return useSyncExternalStore(subscribe, getPrototypeSuppliers, getPrototypeSuppliers);
}

export function createPrototypeSupplier(draft: PrototypeSupplierDraft): PrototypeSupplier {
  const created: PrototypeSupplier = {
    id: `prototype-${crypto.randomUUID()}`,
    ...draft,
    deleted: false,
    deletedAt: null,
    rowVersion: 1,
  };
  updateSnapshot([created, ...suppliers]);
  return created;
}

export function updatePrototypeSupplier(id: string, draft: PrototypeSupplierDraft): void {
  updateSnapshot(suppliers.map((item) => item.id === id
    ? { ...item, ...draft, rowVersion: item.rowVersion + 1 }
    : item));
}

export function togglePrototypeSupplierActive(id: string): void {
  updateSnapshot(suppliers.map((item) => item.id === id && !item.deleted
    ? { ...item, active: !item.active, rowVersion: item.rowVersion + 1 }
    : item));
}

export function softDeletePrototypeSupplier(id: string): void {
  const deletedAt = new Date().toISOString();
  updateSnapshot(suppliers.map((item) => item.id === id
    ? { ...item, deleted: true, deletedAt, rowVersion: item.rowVersion + 1 }
    : item));
}

export function restorePrototypeSupplier(id: string): void {
  updateSnapshot(suppliers.map((item) => item.id === id
    ? { ...item, deleted: false, deletedAt: null, rowVersion: item.rowVersion + 1 }
    : item));
}

export function hardDeletePrototypeSupplier(id: string): void {
  updateSnapshot(suppliers.filter((item) => item.id !== id));
}
