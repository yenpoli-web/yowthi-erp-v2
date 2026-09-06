import { postApiCommand } from '../../app/api/apiTransport';
import type { OperationalLocale } from '../../app/i18n/locale';

export { ApiProblemError } from '../../app/api/apiTransport';

export interface PayPayableRequest {
  amountThb: number | null;
  expectedOutstandingVersion: number;
}

export interface PayPayableResult {
  paymentId: string;
  payableId: string;
  amountThb: number;
  outstandingThb: number;
  outstandingVersion: number;
  confirmedAt: string;
}

export interface ReceiveReceivableRequest {
  amountThb: number | null;
  expectedOutstandingVersion: number;
}

export interface ReceiveReceivableResult {
  receiptId: string;
  receivableId: string;
  amountThb: number;
  outstandingThb: number;
  outstandingVersion: number;
  confirmedAt: string;
}

interface FinanceCommandOptions {
  idempotencyKey: string;
  locale: OperationalLocale;
  signal?: AbortSignal;
}

export function payPayable(
  payableId: string,
  request: PayPayableRequest,
  options: FinanceCommandOptions,
): Promise<PayPayableResult> {
  return postApiCommand<PayPayableRequest, PayPayableResult>(
    `/api/v1/finance/payables/${payableId}/payments`,
    request,
    options,
  );
}

export function receiveReceivable(
  receivableId: string,
  request: ReceiveReceivableRequest,
  options: FinanceCommandOptions,
): Promise<ReceiveReceivableResult> {
  return postApiCommand<ReceiveReceivableRequest, ReceiveReceivableResult>(
    `/api/v1/finance/receivables/${receivableId}/receipts`,
    request,
    options,
  );
}
