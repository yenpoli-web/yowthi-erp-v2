# Current Design Checkpoint — YowThi ERP V2

Checkpoint status: **v0.1 implementation baseline through P6 V8-C8 complete. Supplier/Customer Hard Delete, Sales Allocation Correction, ERP Registration / Data Control clarification, Supplier/Customer lifecycle, Payment Amount Correction, Receipt Amount Correction, and Payable Adjustment Correction are formally implemented and validated. P6 V8 remains IN PROGRESS for remaining focused ERP Control slices.**

Purpose: recover the current architecture and implementation state if conversational context is lost.

## 1. Highest-authority rules

Business Rules come only from real YowThi operating facts.

**Do not use Business Rules to unnecessarily constrain ERP data maintenance.** ERP registration of real operations and ERP control of system data/state are different logical concerns.

Authoritative classification:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

Application writes are interpreted as:

```text
Application Write Command
├─ Business Fact Command
└─ ERP Control Command
```

Business Fact Command:
- records an operation/result that actually happened
- Business Rules must come from real YowThi facts

ERP Control Command:
- corrects or maintains ERP data/state because an authorized operator needs a system change
- does not require inventing a Business Rule merely to justify the mutation
- remains target-specific and technically controlled

Physical Inventory Reconciliation:
- physical stock may differ because of shrinkage, damage, weighing variance, handling loss, spoilage, missing stock, or other real-world discrepancy
- reconcile through stocktake / `AdjustInventory`
- do not rewrite unrelated historical business movements solely to force inventory to equal a later physical count

Recovery precedence:
1. Business Discovery / Command Contracts / Business Rule Gap Register for real Business Facts
2. `docs/17-erp-registration-data-control-boundary-v0.1.md`
3. this current checkpoint
4. `docs/10-relational-model-consolidation-v0.1.md`
5. `docs/11-ef-core-mapping-architecture-v0.1.md`
6. `docs/12-rest-api-architecture-v0.1.md`, interpreted through docs/17 where older wording labels every persisted write as a Business Write
7. `docs/13-implementation-sequencing-build-plan-v0.1.md`
8. `docs/14-github-cost-governance-v0.1.md`
9. `docs/15-authn-authz-implementation-architecture-v0.1.md`
10. `docs/16-adaptive-web-ui-architecture-v0.1.md` and applicable ADRs

Earlier PostgreSQL Schema Parts remain design history. `docs/10` is the consolidated relational baseline when relational details conflict.

## 2. Repository / Legacy safety

Formal repository:
- GitHub: `yenpoli-web/yowthi-erp-v2`
- local: `C:\Dev\yowthi-erp-v2`

Protected Legacy ERP:
- `C:\yowthi-erp`
- read-only reference / migration / validation source only
- never modify, delete, move, reset, overwrite, or derive ERP V2 architecture directly from the Legacy schema

## 3. Technical baseline

- .NET 10 / ASP.NET Core 10 / C#
- EF Core 10 + Npgsql
- PostgreSQL 18
- Modular Monolith
- one write `ErpDbContext`
- React + TypeScript + Vite
- React Router
- TanStack Query
- pnpm
- UUID v7 internal IDs
- explicit `row_version bigint`
- persistent CommandId idempotency
- transactional Outbox where applicable
- append-oriented Audit
- typed real foreign keys
- no Generic Repository / generic CRUD / generic command endpoint / generic `(type,id)` lifecycle/correction/hard-delete resolver

Persistence invariants:
- Inventory Movement is append-oriented ledger truth
- Inventory Position is a transactional rebuildable projection
- Inventory Position logical identity uses typed nullable dimensions and PostgreSQL `UNIQUE NULLS NOT DISTINCT`
- Audit/Outbox CommandId values are correlation snapshots, not FKs to CommandExecution
- no global EF soft-delete query filter
- no Npgsql `xmin` substitution for row version
- migration architecture never reverse-defines the domain model

`InitialV01`:
- migration: `20260828033151_InitialV01`
- development PostgreSQL endpoint: `127.0.0.1:55432/yowthi_dev`
- accepted PostgreSQL version: 18.6
- P5 status: 1 applied / 0 pending
- migration-state fingerprint: `9645A93DBC1642819210DFA99904A81776AF0CBE0116458945409A9611889E6E`

## 4. Phase status

```text
P0    Repository / solution scaffolding                  COMPLETE
P1    Shared technical foundation                        COMPLETE
P2    Full 55-relation Domain + EF model                COMPLETE
P3    API technical shell                                COMPLETE
P3.5  AuthN/AuthZ architecture hard gate                 COMPLETE
P4    InitialV01 generation / static review              COMPLETE
P5    PostgreSQL 18 persistence acceptance               COMPLETE
P6    Business / ERP Control vertical slices             IN PROGRESS
  V1  ConfirmProcurementEntry                            COMPLETE
  V2  ConfirmOutsourcedSupplyDetail                      COMPLETE
  V3  ConfirmProcessingExecution                         COMPLETE
  V4  ConfirmSales                                       COMPLETE
  V5  Sales Packaging Work + Daily Wage                 COMPLETE
  V6  Finance adjustment / settlements                   COMPLETE
  V7  Inventory transfer / adjustment / Batch Close     COMPLETE
  V8  Correction / Lifecycle / Hard Delete              IN PROGRESS
      C1 Supplier Hard Delete                            COMPLETE
      C2 Customer Hard Delete                            COMPLETE
      C3 Sales Allocation Correction                     COMPLETE
      ERP Registration / Data Control clarification      COMPLETE
      C4 Supplier Soft Delete / Restore                  COMPLETE
      C5 Customer Soft Delete / Restore                  COMPLETE
      C6 Payment Amount Correction                       COMPLETE
      C7 Receipt Amount Correction                       COMPLETE
      C8 Payable Adjustment Correction                   COMPLETE
      remaining focused ERP Control slices               IN PROGRESS
P7    React UI vertical slices                           NOT FORMALLY COMPLETE
P8    CI / production hardening                          FUTURE
```

Do not mark P6 or all of V8 COMPLETE until the remaining required V8 control scope is implemented or formally deferred.

## 5. Current formal Git baseline

Formal code baseline immediately before this checkpoint-document commit:
- `main = origin/main`
- SHA: `8b660b677eef4463645496140eff5dd471432cca`
- commit: `feat: add payable adjustment correction`
- promotion: fast-forward only
- push: non-force
- remote fetch/read-back: clean

C4 docs checkpoint:
- commit: `f81cfb97eea4fecdb99a2e556dd746e0dac9ab9c`
- commit: `docs: checkpoint supplier lifecycle completion`
- validation branch: `p6-v8-c4-checkpoint-validation`
- self-hosted run: `33841667574`
- conclusion: `success`
- ff-only main promotion complete

C5 docs checkpoint:
- commit: `85b3c21f7e25ee8847b894d08f23c5bf595fe246`
- commit: `docs: checkpoint customer lifecycle completion`
- validation branch: `p6-v8-c5-checkpoint-validation`
- self-hosted run: `33845174504`
- conclusion: `success`
- ff-only main promotion complete

C6 Payment Amount Correction implementation:
- commit: `cdd652b639a56c9896d7403cbbf501331ce7aca0`
- commit: `feat: add payment amount correction`
- validation branch: `p6-v8-payment-correction-validation`
- local hard gates: 251/251 PASS
- self-hosted run: `33850492563`
- runner: `YowThi-ERP-V2`
- required labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`
- ff-only main promotion + non-force push/read-back: COMPLETE

C7 Receipt Amount Correction implementation:
- commit: `768c9673f242cfbc0c53b1cd7ecd6e3fa273f770`
- commit: `feat: add receipt amount correction`
- validation branch: `p6-v8-receipt-correction-validation`
- local hard gates: 257/257 PASS
- self-hosted run: `33854672829`
- runner: `YowThi-ERP-V2`
- required labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`
- ff-only main promotion + non-force push/read-back: COMPLETE

C8 Payable Adjustment Correction implementation:
- commit: `8b660b677eef4463645496140eff5dd471432cca`
- commit: `feat: add payable adjustment correction`
- validation branch: `p6-v8-payable-adjustment-correction-validation`
- local hard gates: 264/264 PASS
- self-hosted run: `33858815725`
- runner: `YowThi-ERP-V2`
- required labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`
- ff-only main promotion + non-force push/read-back: COMPLETE

ERP Registration / Data Control clarification:
- commit: `53e9122b440209a9734697dd36a0f104dc59176c`
- self-hosted run: `33838713391`
- conclusion: `success`

## 6. ERP Registration / Data Control boundary — CONFIRMED 2026-09-04

Authoritative decision:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

Business Fact Registration examples:
- Procurement
- Processing
- Sales
- Payment
- Receipt
- Employee Work

When these operations are registered, Business Rules must represent real YowThi operating facts. If reality later contains another event, register another Business Fact.

ERP Data Control / Maintenance examples:
- correction of wrongly entered data
- Soft Delete
- Restore
- Activate / Deactivate
- Reopen
- Hard Delete under Data Protection

These do not require a separate Business Rule merely to permit an ERP state/data change. They still require the applicable technical controls:
- authenticated actor
- explicit capability authorization
- target-specific endpoint/command
- Idempotency Key
- expected row version where applicable
- structural/dependency safety
- Audit
- transaction/projection consistency

No generic CRUD is introduced.

If ERP registration is wrong:
- correct the registered fact through an ERP Control command
- Audit before/after
- rebuild/update affected projection transactionally

If reality later contains another real event:
- record the new Business Fact
- do not rewrite the earlier real event away

Physical inventory discrepancy is reconciled through stocktake / `AdjustInventory`.

## 7. Business Rule Gap Register classification

`docs/06-business-rule-gap-register-v0.1.md` includes `CONTROL`.

`CONTROL` means:
- ERP Data Control / maintenance concern
- not a Business Rule blocker
- implementation governed by technical safety and authorization

Reclassified on 2026-09-04:
- `FIN-003` → CONTROL
- `FIN-005` → CONTROL
- `LIFE-001` → CONTROL

`SALES-003` remains RESOLVED Business Fact history.

## 8. Sales Allocation Correction — V8-C3 COMPLETE

Endpoint:
- `POST /api/v1/sales/{salesId}/allocation-revisions`

Capability:
- `sales.correct-allocation`

Modes:
- `COMPLETE_REPLACEMENT`
- `OVERRIDE_AND_REALLOCATE`

Persistence/control semantics:
- append immutable Allocation Revision + Items
- replace current allocation pointers
- `SALES_ALLOCATION_ADJUSTMENT` only for net allocation delta
- update Inventory Positions transactionally
- increment Sales row version
- correction Audit + `audit.correction_links`
- persistent idempotency
- transactional Outbox
- no rewrite of prior `SALES_ISSUE` / Inventory Movement history

Formal implementation:
- SHA `e5b92dcc8283f6f1f59e4452fc582718236e5a77`
- local hard gates 233/233 PASS
- self-hosted run `33828986488` SUCCESS

## 9. Hard Delete baseline

Hard Delete remains the highest-authority Data Protection operation.

It is controlled by:
- explicit target support
- dependency closure
- `data-protection.hard-delete`
- expected row version where applicable
- retained same-transaction `HARD_DELETE` Audit
- replay before target lookup

Completed targets:
- Supplier — V8-C1 COMPLETE
- Customer — V8-C2 COMPLETE

Additional Hard Delete targets do not need a Business Rule merely to be considered. They still require target-specific structural dependency closure before physical deletion is implemented.

No generic `/data-protection/entities/{type}/{id}` endpoint.

## 10. Supplier lifecycle — V8-C4 COMPLETE

Commands:
- `SoftDeleteSupplier`
- `RestoreSupplier`

Endpoints:
- `POST /api/v1/party/suppliers/{supplierId}/soft-delete`
- `POST /api/v1/party/suppliers/{supplierId}/restore`

Capability:
- `party.supplier.lifecycle`

Semantics:
- Soft Delete keeps the Supplier row and historical typed FKs
- sets deleted metadata and increments `row_version`
- preserves `active`
- current-use Supplier selectors exclude deleted rows
- historical dependencies do not by themselves block Soft Delete because the row remains for FK/traceability history
- Restore clears deleted metadata, increments `row_version`, preserves `active`, and does not automatically reactivate an inactive Supplier

Technical controls:
- acquire/replay CommandId before current lifecycle/version lookup
- idempotency/concurrency/state conflicts roll back CommandExecution acquisition and Audit
- Audit event kind `DATA_LIFECYCLE`, subject `party.supplier`, change kind `SOFT_DELETE` / `RESTORE`

Acceptance:
- Domain 29/29
- Architecture 66/66
- API Contract 68/68
- PostgreSQL Integration 76/76
- total 239/239 PASS
- implementation SHA `22fd7770445d915fd428591dff3d7d1490911922`
- self-hosted run `33840747740` SUCCESS

No relation, schema, model snapshot, or EF migration change.

## 11. Customer lifecycle — V8-C5 COMPLETE

Commands:
- `SoftDeleteCustomer`
- `RestoreCustomer`

Endpoints:
- `POST /api/v1/party/customers/{customerId}/soft-delete`
- `POST /api/v1/party/customers/{customerId}/restore`

Capability:
- `party.customer.lifecycle`

Semantics:
- Soft Delete keeps the Customer row and all historical Sale references
- sets `deleted_at` / `deleted_by_account_id`
- increments `row_version`
- preserves `active`
- does not cascade/delete `sales.sales.customer_id` history
- an existing Sale dependency does **not** block Soft Delete because Customer remains present for FK/traceability history
- Restore clears deleted metadata, increments `row_version`, and preserves the existing `active` value
- Restore does not automatically reactivate an inactive Customer

Idempotency/concurrency:
- acquire/replay CommandId before current lifecycle/version lookup
- same actor + command type + canonical hash replays committed result
- changed actor/type/hash → `idempotency.key-reused`
- stale expected row version → `concurrency.stale-row-version`
- already deleted Soft Delete → `party.customer-already-deleted`
- Restore of a current Customer → `party.customer-not-deleted`
- failed state/concurrency attempts roll back CommandExecution acquisition and Audit

Audit:
- event kind `DATA_LIFECYCLE`
- subject kind `party.customer`
- subject change kind `SOFT_DELETE` / `RESTORE`
- before/after row versions retained

C5 local acceptance:
- Domain: 29/29 PASS
- Architecture: 66/66 PASS
- API Contract: 71/71 PASS
- PostgreSQL Integration: 79/79 PASS
- total: **245/245 PASS**
- focused project builds: 0 warnings / 0 errors
- full Release solution build: 0 errors; only the known solution custom-output `NETSDK1194` warning

C5 formal implementation evidence:
- branch: `p6-v8-customer-lifecycle-validation`
- exact SHA: `e22d281e4c1c92bca6d57e6a301e2ea1cacc5cbb`
- commit: `feat: add customer lifecycle control`
- workflow: `dotnet.yml` / `dotnet-self-hosted`
- run: `33842998109`
- runner: `YowThi-ERP-V2`
- required labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`
- ff-only main promotion + non-force push/read-back: COMPLETE

C5 introduced no relation, schema, model snapshot, or EF migration change.

## 12. Finance correction baseline

Finance truth/projection remains:

```text
Original Obligation + Adjustments - Settlements = Outstanding
```

If Payment / Receipt / Adjustment registration is wrong:
- target-specific correction may amend the wrongly registered data
- re-evaluate/rebuild affected Outstanding transactionally
- preserve concurrency protection
- Audit before/after
- idempotency

Do not fabricate a fake reversal business event merely to justify correcting an ERP registration error.

If money actually moves again, record the new real Finance Business Fact.

`FIN-003` / `FIN-005` are ERP Control items, not Business Rule hard gates. V8-C6 completes Payment amount correction and V8-C7 completes Receipt amount correction under FIN-003. V8-C8 completes Payable Adjustment correction under FIN-005.

### Payment Amount Correction - V8-C6 COMPLETE

Command:
- `CorrectPaymentAmount`

Endpoint:
- `POST /api/v1/finance/payables/{payableId}/payments/{paymentId}/correct-amount`

Capability:
- `finance.correct`

Semantics:
- direct amendment of a wrongly registered Payment amount
- preserve original `confirmed_at` and `confirmed_by_account_id`
- Payable Outstanding Position row version is the monetary concurrency boundary
- update Payment, `settlement_total_thb`, and `outstanding_thb` atomically by the correction delta
- block a correction that would make Outstanding negative
- no fabricated reversal Business Fact

Audit/idempotency:
- `CORRECTION` event
- subject `finance.payment`
- change kind `UPDATE`
- before/after amount summary
- correction link mode `DIRECT_AMENDMENT`
- persistent CommandId replay before current-state validation
- Outbox `finance.payment-corrected`

Acceptance:
- Domain 29/29 PASS
- Architecture 66/66 PASS
- API Contract 74/74 PASS
- PostgreSQL Integration 82/82 PASS
- total **251/251 PASS**
- final Release solution build: 0 errors; only known `NETSDK1194`
- exact SHA `cdd652b639a56c9896d7403cbbf501331ce7aca0`
- self-hosted run `33850492563` SUCCESS

No relation, schema, model snapshot, or EF migration change.

Receipt amount correction is complete in V8-C7. Payable Adjustment correction remains the pending target-specific Finance ERP Control slice.

### Receipt Amount Correction - V8-C7 COMPLETE

Command:
- `CorrectReceiptAmount`

Endpoint:
- `POST /api/v1/finance/receivables/{receivableId}/receipts/{receiptId}/correct-amount`

Capability:
- `finance.correct`

Semantics:
- direct amendment of a wrongly registered Receipt amount
- preserve original `confirmed_at` and `confirmed_by_account_id`
- Receivable Outstanding Position row version is the monetary concurrency boundary
- update Receipt, `settlement_total_thb`, and `outstanding_thb` atomically by the correction delta
- block a correction that would make Outstanding negative
- no fabricated reversal Business Fact

Audit/idempotency:
- `CORRECTION` event
- subject `finance.receipt`
- change kind `UPDATE`
- before/after amount summary
- correction link mode `DIRECT_AMENDMENT`
- persistent CommandId replay before current-state validation
- Outbox `finance.receipt-corrected`

Acceptance:
- Domain 29/29 PASS
- Architecture 66/66 PASS
- API Contract 77/77 PASS
- PostgreSQL Integration 85/85 PASS
- total **257/257 PASS**
- final Release solution build: 0 errors; only known `NETSDK1194`
- exact SHA `768c9673f242cfbc0c53b1cd7ecd6e3fa273f770`
- self-hosted run `33854672829` SUCCESS

No relation, schema, model snapshot, or EF migration change.

### Payable Adjustment Correction - V8-C8 COMPLETE

Command:
- `CorrectPayableAdjustment`

Endpoint:
- `POST /api/v1/finance/payables/{payableId}/adjustments/{adjustmentId}/correct`

Capability:
- `finance.correct`

Semantics:
- complete corrected state for Adjustment amount delta and reason text
- preserve original `adjustment_type`, `recorded_at`, and `recorded_by_account_id`
- Payable Outstanding Position row version is the concurrency boundary
- update Adjustment, `adjustment_total_thb`, and `outstanding_thb` atomically by the correction difference
- reason-only correction keeps monetary projection values unchanged but advances Outstanding row version
- block a correction that would make Outstanding negative
- no fabricated reversal Business Fact

Audit/idempotency:
- `CORRECTION` event
- subject `finance.payable-adjustment`
- change kind `UPDATE`
- before/after amount and reason summary
- correction link mode `DIRECT_AMENDMENT`
- persistent CommandId replay before current-state validation
- Outbox `finance.payable-adjustment-corrected`

Acceptance:
- Domain 29/29 PASS
- Architecture 66/66 PASS
- API Contract 80/80 PASS
- PostgreSQL Integration 89/89 PASS
- total **264/264 PASS**
- final Release solution build: 0 errors; only known `NETSDK1194`
- exact SHA `8b660b677eef4463645496140eff5dd471432cca`
- self-hosted run `33858815725` SUCCESS

No relation, schema, model snapshot, or EF migration change.

FIN-005 target-specific correction is complete in V8-C8.

## 13. Closed Batch Reopen baseline

Reopen is an ERP lifecycle control.

Reopen means:
- Batch lifecycle becomes open for applicable ERP operations again

Reopen does **not** mean:
- erase prior Close Audit
- delete prior `BATCH_RECONCILIATION`
- rewrite immutable Inventory Movement history
- reconstruct an imagined pre-close physical inventory state

Physical inventory mismatch after operational history is handled through stocktake / `AdjustInventory`.

`LIFE-001` is an implementation/control item, not a Business Rule hard gate.

## 14. AuthN/AuthZ interpretation

Capability policies are technical ERP Control / security identifiers, not Business Rules.

Current examples include:
- `sales.confirm`
- `sales.correct-allocation`
- `finance.pay`
- `finance.correct`
- `inventory.adjust`
- `party.supplier.lifecycle`
- `party.customer.lifecycle`
- `data-protection.hard-delete`

Capability grants remain deployment-configured by persistent Account UUID.

`data-protection.hard-delete` remains highest authority and must not be reused for ordinary lifecycle/data correction.

## 15. React/UI baseline

One React application:
- `src/YowThi.Erp.Web`

Presentation experiences:
- Desktop
- Tablet
- Mobile

Implemented routes currently include:
- `/procurement/entries/new`
- `/outsourced/supply-details/new`
- `/processing/executions/new`

The complete ERP UI is not finished. Broad formal UI sequencing remains P7 after required backend/control slices are stable.

## 16. Remaining V8 sequencing

The old Business Rule hard gates for Finance correction and Batch Reopen are removed by docs/17. Remaining work should proceed as focused, target-specific ERP Control slices rather than reopening business-mode questions that are merely data-maintenance concerns.

Candidate next slices include:
- additional Party lifecycle targets using explicit target-specific commands/capabilities
- Closed Batch Reopen
- additional Hard Delete targets after structural dependency closure

Do not introduce a generic lifecycle/correction resolver to accelerate this sequence.

P6 V8 remains **IN PROGRESS** until the required remaining control scope is implemented or explicitly deferred.

## 17. Validation / cost governance

Routine validation uses only the Windows self-hosted runner.

Required labels:
- `self-hosted`
- `yowthi-erp-v2`

Formal main advances only after exact validation-branch SHA success and `eligibleForMainFastForward=true`.

Use ff-only promotion and non-force push.

Do not require routine GitHub-hosted runners, paid/larger runners, Codespaces, or unconfirmed metered services.

## 18. Recovery

```text
main@8b660b677eef4463645496140eff5dd471432cca
-> V8-C1 Supplier Hard Delete COMPLETE
-> V8-C2 Customer Hard Delete COMPLETE
-> V8-C3 Sales Allocation Correction COMPLETE
-> ERP Registration / Data Control boundary COMPLETE
-> FIN-003 / FIN-005 / LIFE-001 classified as CONTROL
-> V8-C4 Supplier Soft Delete / Restore COMPLETE
-> C4 docs checkpoint f81cfb97eea4fecdb99a2e556dd746e0dac9ab9c COMPLETE
-> V8-C5 Customer Soft Delete / Restore COMPLETE
-> C5 docs checkpoint 85b3c21f7e25ee8847b894d08f23c5bf595fe246 COMPLETE
-> V8-C6 CorrectPaymentAmount COMPLETE
-> local C6 hard gates 251/251 PASS
-> C6 self-hosted run 33850492563 SUCCESS
-> C6 ff-only main promotion + non-force push/read-back COMPLETE
-> V8-C7 CorrectReceiptAmount COMPLETE
-> local C7 hard gates 257/257 PASS
-> C7 self-hosted run 33854672829 SUCCESS
-> C7 ff-only main promotion + non-force push/read-back COMPLETE
-> V8-C8 CorrectPayableAdjustment COMPLETE
-> local C8 hard gates 264/264 PASS
-> C8 self-hosted run 33858815725 SUCCESS
-> C8 ff-only main promotion + non-force push/read-back COMPLETE
-> current docs checkpoint branch: p6-v8-c8-checkpoint-validation
```