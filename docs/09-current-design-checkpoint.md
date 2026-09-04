# Current Design Checkpoint — YowThi ERP V2

Checkpoint status: **v0.1 implementation baseline through P6 V8 partial — Supplier Hard Delete, Customer Hard Delete, and Sales Allocation Correction are complete. P6 V8 remains IN PROGRESS because the remaining lifecycle / additional hard-delete / Finance correction work is still behind explicit Business Fact or security-architecture gates.**

Purpose: recover the current architecture and implementation state if conversational context is lost.

## 1. Highest-authority rules

Business Rules come only from real YowThi operating facts. Technical convenience must not create a Business Rule.

Recovery precedence:
1. Business Discovery / Command Contracts / Business Rule Gap Register / owning-domain facts
2. this current checkpoint
3. `docs/10-relational-model-consolidation-v0.1.md`
4. `docs/11-ef-core-mapping-architecture-v0.1.md`
5. `docs/12-rest-api-architecture-v0.1.md`
6. `docs/13-implementation-sequencing-build-plan-v0.1.md`
7. `docs/14-github-cost-governance-v0.1.md`
8. `docs/15-authn-authz-implementation-architecture-v0.1.md`
9. `docs/16-adaptive-web-ui-architecture-v0.1.md` and applicable ADRs

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
- transactional Outbox
- append-oriented Audit
- typed real foreign keys
- no Generic Repository / generic CRUD / generic command endpoint / generic hard-delete resolver

Persistence invariants remain:
- Inventory Movement is append-oriented ledger truth
- Inventory Position is a transactional rebuildable projection
- Inventory Position logical identity uses typed nullable dimensions and PostgreSQL `UNIQUE NULLS NOT DISTINCT`
- Audit/Outbox CommandId values are correlation snapshots, not FKs to CommandExecution
- no global EF soft-delete query filter
- no Npgsql `xmin` substitution for row version
- migration architecture never reverse-defines the domain model

`InitialV01` remains:
- migration: `20260828033151_InitialV01`
- development PostgreSQL endpoint: `127.0.0.1:55432/yowthi_dev`
- accepted PostgreSQL version: 18.6
- P5 status: 1 applied / 0 pending
- migration-state fingerprint: `9645A93DBC1642819210DFA99904A81776AF0CBE0116458945409A9611889E6E`

## 4. Completed implementation phases

```text
P0    Repository / solution scaffolding                  COMPLETE
P1    Shared technical foundation                        COMPLETE
P2    Full 55-relation Domain + EF model                COMPLETE
P3    API technical shell                                COMPLETE
P3.5  AuthN/AuthZ architecture hard gate                 COMPLETE
P4    InitialV01 generation / static review              COMPLETE
P5    PostgreSQL 18 persistence acceptance               COMPLETE
P6    Business vertical slices                           IN PROGRESS
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
      remaining lifecycle / targets / finance            HARD GATE
P7    React UI vertical slices                           NOT FORMALLY COMPLETE
P8    CI / production hardening                          FUTURE
```

Do not mark P6 or all of V8 COMPLETE until the remaining V8 set is implemented or explicitly deferred by a formal decision.

## 5. Current formal Git baseline and V8-C3 evidence

Formal implementation baseline immediately before this checkpoint-document commit:
- `main = origin/main`
- SHA: `e5b92dcc8283f6f1f59e4452fc582718236e5a77`
- commit: `feat: add sales allocation correction command`
- promotion method: fast-forward only
- push: non-force
- working tree: clean after remote fetch/read-back

V8-C3 validation branch:
- `p6-v8-sales-allocation-correction-validation`
- exact validated SHA: `e5b92dcc8283f6f1f59e4452fc582718236e5a77`

Local acceptance evidence:
- Domain: 29 / 29 PASS
- Architecture: 66 / 66 PASS
- API Contract: 65 / 65 PASS
- PostgreSQL Integration: 73 / 73 PASS
- total: **233 / 233 PASS**
- focused project builds: 0 warnings / 0 errors
- full Release solution build: 0 errors
- solution-level custom-output build only emitted the known `NETSDK1194` warning caused by applying one output directory to a solution

Self-hosted validation evidence:
- workflow: `dotnet.yml` / `dotnet-self-hosted`
- run ID: `33828986488`
- head SHA: `e5b92dcc8283f6f1f59e4452fc582718236e5a77`
- event: `push`
- status: `completed`
- conclusion: `success`
- job: `build-test`
- runner: `YowThi-ERP-V2`
- required labels: `self-hosted`, `yowthi-erp-v2`
- required labels present: true
- `eligibleForMainFastForward=true`

V8-C3 introduced no relation, schema, snapshot, or EF migration change.

## 6. Source-batch / processing baseline

IN_HOUSE batch identity:
- Procurement Batch = same Procurement Date + same Procurement Product

OUTSOURCED batch identity:
- Outsourced Supply Batch = same Supply Date + same Outsourced Vendor
- Product is not part of Outsourced Supply Batch identity

Processing modes remain:
- `SOURCE_TRACKED`
- `POOLED_OUTPUT`
- `FINAL_PACKAGING`

Final Packaging may consume source into negative inventory under the confirmed mode semantics. Packaging Weight and Sales Weight remain distinct.

## 7. Inventory / Batch lifecycle baseline

Inventory identity dimensions:
- Origin
- Source Batch
- Inventory Object
- Storage Location
- optional Raw Source Segment

Relevant movement types include receipts, processing consume/produce, final-package consume/produce, transfers, `SALES_ISSUE`, `SALES_ALLOCATION_ADJUSTMENT`, manual adjustment, and batch reconciliation.

Batch lifecycle concurrency protection:
- `TransferInventory`, `AdjustInventory`, and `ConfirmProcessingExecution` use the shared Batch lifecycle guard
- shared writer lock uses PostgreSQL `FOR SHARE`
- Batch Close uses a conflicting exclusive lifecycle lock/update
- Closed/deleted Batch rejects later inventory/processing writes
- Close never silently reopens a Batch

Procurement Close:
- sellable inventory must be zero
- remaining nonzero positions are reconciled with `BATCH_RECONCILIATION = -current balance`
- whole batch positions must then be zero
- mark Closed

Outsourced Close:
- sellable inventory must be zero
- no Procurement-style reconciliation
- mark Closed

Still unresolved:
- BATCH-001 automatic vs user-confirmed Close
- LIFE-001 Closed Batch reopen

## 8. Sales baseline

Sales Header:
- Sales Date + Customer

Sales Detail includes Sales Product, quantity, pricing basis snapshot, Sales Weight snapshot when applicable, Unit Price, and amount.

Allocation priority v0.1:
1. OUTSOURCED oldest → newest
2. IN_HOUSE oldest → newest
3. authorized manual override

Confirmed Sales atomically creates:
- Allocation Revision 0
- immutable Allocation Revision Items
- pointer-only current `sales_allocations`
- `SALES_ISSUE`
- Receivable

### SALES-003 — RESOLVED 2026-09-04

Confirmed Sales Allocation correction supports two explicit operating modes. The caller selects the mode; the system does not infer it.

`COMPLETE_REPLACEMENT`:
- user submits the complete official replacement allocation set
- each Sales Detail submitted total must equal its Sales Detail quantity
- no automatic remainder allocation occurs

`OVERRIDE_AND_REALLOCATE`:
- explicit user overrides are applied first
- remaining quantity is allocated using the established priority: OUTSOURCED oldest→newest, then IN_HOUSE oldest→newest

### V8-C3 — CorrectSalesAllocation COMPLETE

Endpoint:
- `POST /api/v1/sales/{salesId}/allocation-revisions`

Operation-specific capability policy:
- `sales.correct-allocation`

Authorization remains capability-based and deployment-configured by Account UUID. This policy does not create a business role hierarchy or new authorization persistence.

Correction semantics:
- only CONFIRMED Sales can be corrected by this command
- append next immutable Allocation Revision + Revision Items
- replace only current `sales_allocations` pointers
- compare old official allocation with new official allocation
- create `SALES_ALLOCATION_ADJUSTMENT` movements only for actual net position deltas
- update Inventory Positions transactionally
- increment Sales row version
- never rewrite prior `SALES_ISSUE` or other movement history
- persistent CommandId replay/conflict handling
- correction Audit + correction lineage
- transactional Outbox

Audit semantics:
- Audit event kind = `CORRECTION`
- changed subject = Sales row
- subject `change_kind` = existing controlled value `UPDATE`
- `audit.correction_links.correction_mode = COMPENSATION`

Lifecycle invariant:
- correction must not silently reopen a Closed/deleted source Batch
- if correction requires a nonzero inventory delta on a Closed/deleted source Batch, block it
- unchanged allocation on a closed source does not create an artificial movement

SALES-001 remains unresolved: ambiguous Storage Location for one source batch still requires explicit resolution; C3 does not invent a location-selection Business Rule.

## 9. Hard Delete baseline

Hard Delete remains:
- highest-authority data-protection boundary
- target-specific command / route only
- dependency assessment by owning-domain knowledge
- no silent cascade across core traceability
- physical deletion with retained same-transaction `HARD_DELETE` Audit
- replay resolved from CommandExecution before current target lookup

### V8-C1 Supplier Hard Delete — COMPLETE

Endpoint:
- `POST /api/v1/data-protection/suppliers/{supplierId}/hard-delete`

Capability:
- `data-protection.hard-delete`

Dependency closure checks Supplier references in Procurement Entry, SOURCE_TRACKED Processing, raw Supplier Inventory lineage, and Procurement Supplier Payable. Any dependency blocks physical deletion.

### V8-C2 Customer Hard Delete — COMPLETE

Endpoint:
- `POST /api/v1/data-protection/customers/{customerId}/hard-delete`

Capability:
- `data-protection.hard-delete`

Direct typed-FK dependency is `sales.sales.customer_id`. Any Sale blocks physical deletion.

Do not infer Hard Delete support for Farmer, Employee, Outsourced Vendor, product/configuration, transactions, or other entities. Each target requires explicit support and dependency closure.

## 10. Finance / Labor baseline

Labor:
- Processing Execution + Sales Packaging Work → Employee Daily Wage → Employee Payable → Payment
- Daily Wage aggregates quantity before applying the wage rate
- Sales Packaging/Handling is day-rate in v0.1
- HANDLING-001 resolved 2026-09-03: handling work may be recorded while Sales is DRAFT or CONFIRMED
- LABOR-001 late work after confirmed Daily Wage remains unresolved

Finance:
- Original Obligation + Adjustments - Settlements = Outstanding
- obligations / adjustments / settlements are truth
- Outstanding is a rebuildable transactional projection
- partial settlements are supported
- Supplier quality/weight deduction is a Payable Adjustment and never rewrites Procurement
- no generic edit/delete of confirmed Finance facts

FIN-003 Payment/Receipt correction and FIN-005 adjustment correction/reversal remain unresolved. Do not expose generic reversal/edit/delete APIs.

## 11. AuthN/AuthZ baseline

Authentication:
- provider-neutral external OIDC Authorization Code + PKCE
- ASP.NET Core encrypted Cookie session
- pre-provisioned `system.accounts`
- exact validated `(issuer, subject)` resolves Actor Account
- account must remain active
- no ERP password store
- React does not own access/refresh tokens

Authorization:
- explicit operation/capability policy names
- grants deployment-configured by persistent Account UUID
- no invented Admin/Manager/SuperAdmin business roles
- no roles/permissions/account-role relations in v0.1

Current examples include:
- `sales.confirm`
- `sales.correct-allocation`
- `finance.pay`
- `inventory.adjust`
- `data-protection.hard-delete`

`data-protection.hard-delete` must not be reused as ordinary Soft Delete / Restore permission.

## 12. React/UI baseline

One React application: `src/YowThi.Erp.Web`.

Presentation experiences:
- Desktop
- Tablet
- Mobile

They share REST contracts, server-state/query core, authentication, locale, Problem Details, idempotency, concurrency, and Business Command semantics.

Implemented routes currently include:
- `/procurement/entries/new`
- `/outsourced/supply-details/new`
- `/processing/executions/new`

The complete ERP UI is not finished. Broad formal UI sequencing remains P7 after required P6 backend gates.

## 13. Important unresolved Business Rule gaps

Authoritative source: `docs/06-business-rule-gap-register-v0.1.md`.

High-impact unresolved items include:
- PROC-001 completed Procurement Batch late entry
- PROC-003 Procurement Entry zero Net Quantity confirmation
- PROCESS-001 multi-location input resolution
- OUT-002 Outsourced Supply Detail zero Quantity confirmation
- OUT-003 late detail after Outsourced Batch Closed
- SALES-001 Sales issue location when stock spans locations
- BATCH-001 automatic vs user-confirmed Batch Close
- LABOR-001 late work after Daily Wage confirmation
- FIN-001/002 over-settlement controls
- FIN-003 confirmed Payment/Receipt correction method
- FIN-004 deduction causing negative Payable Outstanding
- FIN-005 Payable Adjustment correction/reversal
- FIN-007/008/009 Company Pickup Transport amount/grouping/payee semantics
- LIFE-001 Closed Batch reopen

`SALES-003` is no longer unresolved; it is retained as RESOLVED history in the Gap Register.

Safe/deferred handling in the Gap Register must not be silently promoted into permanent Business Rules.

## 14. Remaining V8 hard gates

### A. Soft Delete / Restore

Need target-specific facts:
- which target is applicable next
- owning-domain Restore eligibility and dependency behavior

Also requires an explicit security-architecture capability decision for ordinary lifecycle management. Do not reuse `data-protection.hard-delete`.

### B. Additional Hard Delete targets

Need explicit target support plus owning-domain dependency closure before adding another target-specific route.

Do not create a generic `/data-protection/entities/{type}/{id}` endpoint.

### C. Finance correction / reversal

Blocked by FIN-003 and FIN-005.

### D. Closed Batch reopen

Deferred under LIFE-001. Do not implement.

## 15. GitHub validation / cost governance

Routine validation uses the Windows self-hosted runner only.

Required labels:
- `self-hosted`
- `yowthi-erp-v2`

Validation branches:
- `m*-validation`
- `p*-validation`

Formal main advances only after the exact validation-branch SHA has a successful self-hosted `dotnet.yml` run and `eligibleForMainFastForward=true`.

No routine GitHub-hosted runner, larger/paid runner, Codespaces, force push, or unconfirmed metered service is required.

## 16. Recovery / next step

Formal implementation state immediately before this checkpoint-document commit:

```text
main@e5b92dcc8283f6f1f59e4452fc582718236e5a77
→ V8-C1 Supplier Hard Delete COMPLETE
→ V8-C2 Customer Hard Delete COMPLETE
→ SALES-003 RESOLVED 2026-09-04
→ V8-C3 CorrectSalesAllocation COMPLETE
→ local hard gates 233/233 PASS
→ self-hosted run 33828986488 SUCCESS
→ eligibleForMainFastForward=true
→ ff-only main promotion + non-force push/read-back COMPLETE
```

The next action is **not** to invent another V8 command.

Select the next focused slice only after at least one remaining hard-gate decision in section 14 is confirmed using real YowThi operating facts or an explicit security-architecture decision. Until then:
- P6 V8 = **IN PROGRESS / HARD GATE**
- P6 = **IN PROGRESS**
