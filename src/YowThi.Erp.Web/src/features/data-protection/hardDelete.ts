import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';
import type { HardDeleteTargetKind } from './hardDeleteOptions';

export { ApiProblemError } from '../../app/api/apiTransport';

export interface HardDeleteRequest {
  expectedRowVersion: number;
}

export interface HardDeleteResult {
  id?: string;
  supplierId?: string;
  customerId?: string;
  outsourcedVendorId?: string;
  farmerId?: string;
  procurementBatchId?: string;
  procurementEntryId?: string;
}

interface CommandOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

const hardDeleteRoutes: Record<HardDeleteTargetKind, (id: string) => string> = {
  suppliers: (id) => `/api/v1/data-protection/suppliers/${id}/hard-delete`,
  customers: (id) => `/api/v1/data-protection/customers/${id}/hard-delete`,
  'outsourced-vendors': (id) => `/api/v1/data-protection/outsourced-vendors/${id}/hard-delete`,
  farmers: (id) => `/api/v1/data-protection/farmers/${id}/hard-delete`,
  employees: (id) => `/api/v1/data-protection/employees/${id}/hard-delete`,
  'sales-packaging-items': (id) => `/api/v1/data-protection/sales-packaging-items/${id}/hard-delete`,
  warehouses: (id) => `/api/v1/data-protection/warehouses/${id}/hard-delete`,
  'storage-locations': (id) => `/api/v1/data-protection/storage-locations/${id}/hard-delete`,
  containers: (id) => `/api/v1/data-protection/containers/${id}/hard-delete`,
  'procurement-products': (id) => `/api/v1/data-protection/procurement-products/${id}/hard-delete`,
  'sales-products': (id) => `/api/v1/data-protection/sales-products/${id}/hard-delete`,
  'sales-product-groups': (id) => `/api/v1/data-protection/sales-product-groups/${id}/hard-delete`,
  'outsourced-supply-batches': (id) => `/api/v1/outsourced/batches/${id}/hard-delete`,
  'outsourced-supply-details': (id) => `/api/v1/outsourced/supply-details/${id}/hard-delete`,
  'procurement-batches': (id) => `/api/v1/procurement/batches/${id}/hard-delete`,
  'procurement-entries': (id) => `/api/v1/procurement/entries/${id}/hard-delete`,
  sales: (id) => `/api/v1/sales/${id}/hard-delete`,
  'sales-details': (id) => `/api/v1/sales/details/${id}/hard-delete`,
  'processing-executions': (id) => `/api/v1/data-protection/processing-executions/${id}/hard-delete`,
  'processing-execution-inputs': (id) => `/api/v1/data-protection/processing-execution-inputs/${id}/hard-delete`,
  'processing-execution-outputs': (id) => `/api/v1/data-protection/processing-execution-outputs/${id}/hard-delete`,
};

export function hardDeleteTarget(
  kind: HardDeleteTargetKind,
  id: string,
  request: HardDeleteRequest,
  options: CommandOptions,
): Promise<HardDeleteResult> {
  return postApiCommand<HardDeleteRequest, HardDeleteResult>(
    hardDeleteRoutes[kind](id),
    request,
    options,
  );
}
