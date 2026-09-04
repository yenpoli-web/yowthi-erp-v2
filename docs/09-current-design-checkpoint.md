# Current Design Checkpoint — YowThi ERP V2

Checkpoint status: **v0.1 implementation baseline through P6 V8 partial — Supplier / Customer Hard Delete complete; Sales Allocation Correction C3 implemented in the current validation slice. Formal C3 completion requires exact-SHA self-hosted validation and fast-forward promotion to main.**
Purpose: recover the project design and current implementation state if conversational context is lost.

## 1. Highest-level rule

Business Rules come only from real YowThi operations.
ERP Control Governance / security architecture is separate from Business Rules.
AI/developers must not invent Business Rules for technical convenience.

Implementation recovery precedence:
- Business Facts / Business Rules: Business Discovery, Command Contracts, Gap Register, owning-domain documents
- current implementation checkpoint: this document
- relational implementation: `docs/10-relational-model-consolidation-v0.1.md`
- EF Core/Npgsql implementation architecture: `docs/11-ef-core-mapping-architecture-v0.1.md`
- HTTP/REST implementation architecture: `docs/12-rest-api-architecture-v0.1.md`
- implementation sequencing/readiness gates: `docs/13-implementation-sequencing-build-plan-v0.1.md`
- GitHub cost governance: `docs/14-github-cost-governance-v0.1.md`
- AuthN/AuthZ implementation/security architecture: `docs/15-authn-authz-implementation-architecture-v0.1.md`
- adaptive Desktop/Tablet/Mobile web presentation architecture: `docs/16-adaptive-web-ui-architecture-v0.1.md` and ADR-006

Earlier PostgreSQL Schema Parts 1–6 remain design history. `docs/10` is the later consolidated relational baseline when relational details conflict.
For the auth-specific external identity binding only, `docs/15` is the later confirmed correction adding `system.accounts.identity_issuer` / `identity_subject`; relation count remains 55.

## 2. Repository and Legacy safety

Formal repository:
- GitHub: `yenpoli-web/yowthi-erp-v2`
- local: `C:\Dev\yowthi-erp-v2`

Protected Legacy ERP:
- `C:\yowthi-erp`
- read-only reference only
- never modify, delete, move, reset, overwrite, or derive ERP V2 architecture directly from the Legacy schema

Legacy H01/H02/H03 and old schema are references only; they do not define the V2 domain model.

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
- UUID v7 internal technical IDs
- explicit `row_version bigint`
- persistent CommandId idempotency
- transactional Outbox where applicable
- append-oriented Audit
- typed real foreign keys
- no Generic Repository / generic CRUD / generic command endpoint / generic hard-delete resolver

## 4. Completed design and implementation stages

Completed architecture/design to v0.1:
- Project Charter / Business Discovery / Ubiquitous Language
- Domain Map / Aggregate Boundaries / Transaction Boundaries
- Application Command Contracts
- Correction Framework
- Business Rule Gap Register
- Persistence Architecture
- PostgreSQL Schema Parts 1–6
- ADR-005 Audit Reference Boundary
- Full Relational Model Consolidation v0.1
- EF Core Mapping Architecture v0.1
- REST/API Architecture v0.1
- Implementation Sequencing / Build Plan v0.1
- GitHub Cost Governance v0.1
- AuthN/AuthZ Implementation Architecture v0.1
- Adaptive Web UI Architecture v0.1 + ADR-006

Completed implementation phases before the current C3 validation slice:
- P0 repository / .NET / React scaffolding
- P1 shared technical foundation
- P2 full 55-relation EF model through M7
- P3 API technical shell
- P3.5 AuthN/AuthZ architecture hard gate and `system.accounts` auth mapping revision
- P4 `InitialV01` generation / static review / drift verification
- P5 PostgreSQL 18 persistence acceptance
- P6 V1 `ConfirmProcurementEntry`
- P6 V2 `ConfirmOutsourcedSupplyDetail`
- P6 V3 `ConfirmProcessingExecution`
- P6 V4 `ConfirmSales`
- P6 V5 `RecordSalesPackagingWork` + `ConfirmEmployeeDailyWage`
- P6 V6 `AddPayableAdjustment` + `PayPayable` + `ReceiveReceivable`
- P6 V7 `TransferInventory` + `AdjustInventory` + Procurement/Outsourced Batch Close
- P6 V8-C1 target-specific Supplier Hard Delete
- P6 V8-C2 target-specific Customer Hard Delete

Current phase:

**P6 V8 — Correction / Lifecycle / Hard Delete commands: IN PROGRESS.**

Current validation slice:
- V8-C3 `CorrectSalesAllocation`
- Business Fact `SALES-003` resolved 2026-09-04
- implementation and local hard gates complete
- exact-SHA self-hosted validation + fast-forward promotion remain the formal completion gate for C3

Do not mark P6 or V8 COMPLETE until the remaining V8 command set is confirmed and implemented or explicitly deferred by formal decision.

## 5. Current formal Git baseline and C3 validation state

Formal baseline at the start of the current C3 validation slice:
- `main = origin/main`
- SHA: `609a4274c4f658f2c4b8e551b8decfc22da64afa`
- working tree was clean before creating the validation branch

Current validation branch:
- `p6-v8-sales-allocation-correction-validation`
- created from the formal main SHA above
- no schema or EF migration change is required by C3

Current local C3 acceptance evidence:
- Domain: 29 / 29 PASS
- Architecture: 66 / 66 PASS
- API Contract: 65 / 65 PASS
- PostgreSQL Integration: 73 / 73 PASS
- total: 233 / 233 PASS
- full Release solution build: 0 errors
- solution-level custom-output build emits only the known `NETSDK1194` warning caused by applying one output directory to a solution; focused project builds are warning-free

Formal C3 completion still requires:
1. focused commit on the validation branch
2. publish exact branch SHA
3. successful `dotnet.yml` run on the Windows self-hosted runner with required labels
4. `eligibleForMainFastForward=true`
5. fast-forward-only promotion to main and non-force push

## 6. Relational / persistence baseline

- PostgreSQL 18, one database, module schemas
- 55 relations / 14 schemas
- core FKs default RESTRICT / NO ACTION
- explicit schema/table/column mapping
- exact numeric values for quantities/rates/prices
- confirmed THB amounts use `bigint`
- business dates use `date`
- timestamps use `timestamptz`
- internal IDs generated application-side using UUID v7
- CommandId is externally supplied and never regenerated
- `row_version bigint` is explicit optimistic concurrency; no Npgsql `xmin` substitution
- Inventory Movement is append-oriented ledger truth
- Inventory Position is a transactional rebuildable projection
- Inventory Position complete nullable typed identity uses PostgreSQL `UNIQUE NULLS NOT DISTINCT`
- persistent idempotency uses `system.command_executions`
- Audit/Outbox CommandId values are correlation snapshots, not FKs to CommandExecution
- no global EF soft-delete query filter
- no generic soft-delete interceptor
- no Generic Repository

`InitialV01`:
- migration ID: `20260828033151_InitialV01`
- fixed development endpoint: `127.0.0.1:55432/yowthi_dev`
- accepted PostgreSQL version: 18.6
- migration status at P5 acceptance: 1 applied / 0 pending
- migration-state fingerprint: `9645A93DBC1642819210DFA99904A81776AF0CBE0116458945409A9611889E6E`

## 7. Core source-batch and product identities

IN_HOUSE:
- Procurement Batch = same Procurement Date + same Procurement Product

OUTSOURCED:
- Outsourced Supply Batch = same Supply Date + same Outsourced Vendor
- Product is not part of Outsourced Supply Batch identity

Product concepts:
- Procurement Product = purchased product master
- Process Material = route/version-owned traceable intermediate virtual product
- Sales Product = final sellable product master
- Sales Product Group = grouping/content identity, not inventory

No Universal Product or Universal Party aggregate is introduced.

## 8. Processing baseline

One composable processing system with three execution modes:
- SOURCE_TRACKED
- POOLED_OUTPUT
- FINAL_PACKAGING

SOURCE_TRACKED:
- Supplier or Farmers Combined
- input scale
- 1..N outputs

POOLED_OUTPUT:
- no Supplier/Farmer source selection
- source consumption = output quantity

FINAL_PACKAGING:
- completed Sales Product quantity
- source deduction = completed quantity × Packaging Weight
- source may go negative under confirmed Final Packaging semantics
- Packaging Weight and Sales Weight are distinct

No fixed H01/H02/H03 persistence architecture.
Batch-bound Route Version equality is transactionally revalidated at processing confirmation.

## 9. Inventory and Batch lifecycle baseline

Inventory identity dimensions:
- Origin
- Source Batch
- Inventory Object
- Storage Location
- optional Raw Source Segment

Movement types include receipts, processing consume/produce, final-package consume/produce, transfers, sales issue, allocation adjustment, manual adjustment, and batch reconciliation.

P6 V7 lifecycle/concurrency protection:
- `TransferInventory`, `AdjustInventory`, and `ConfirmProcessingExecution` acquire a shared Batch lifecycle guard
- shared writer lock uses PostgreSQL `FOR SHARE`
- Batch Close uses conflicting exclusive lifecycle lock/update
- Closed/deleted Batch rejects later manual inventory/processing writes
- Close never silently reopens a Batch

Procurement Close:
- sellable inventory must be zero
- remaining positions reconciled using explicit `BATCH_RECONCILIATION = -current balance`
- validate whole batch positions = zero
- mark Closed

Outsourced Close:
- sellable inventory must be zero
- no processing reconciliation
- mark Closed

Automatic vs user-confirmed Close remains unresolved under BATCH-001; sold-out detection stays separate from explicit Close.
Closed Batch reopen remains deferred under LIFE-001.

## 10. Sales baseline

Sales Header:
- Sales Date + Customer

Sales Details:
- Sales Product
- quantity
- pricing-basis snapshot
- Sales Weight snapshot when weighted
- Unit Price
- amount

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

Real YowThi operations can use either of two allocation-correction input modes. The mode is explicit and must not be inferred by the system.

`COMPLETE_REPLACEMENT`:
- the user submits the complete official replacement allocation set
- each Sales Detail submitted total must equal its Sales Detail quantity
- no automatic remainder allocation occurs

`OVERRIDE_AND_REALLOCATE`:
- explicit user overrides are applied first
- the remainder is automatically allocated using the established priority: OUTSOURCED oldest→newest, then IN_HOUSE oldest→newest

Confirmed correction persistence/result semantics:
- only a CONFIRMED Sales can be corrected through this command
- append the next immutable Allocation Revision + Items
- replace current `sales_allocations` pointers
- compare old official allocation with new official allocation
- create compensating `SALES_ALLOCATION_ADJUSTMENT` movements only for actual net position deltas
- update Inventory Position projection transactionally
- never rewrite prior Inventory Movement history
- increment the Sales row version
- persistent idempotency / Audit correction lineage / Outbox are part of the same transaction

Lifecycle invariant for correction:
- correction must not silently reopen a Closed/deleted source Batch
- a correction that requires a nonzero Inventory Position delta on a Closed/deleted source Batch is blocked
- an unchanged allocation on a closed source does not create an artificial movement

SALES-001 remains unresolved: if one applicable source batch spans ambiguous Storage Locations, the command does not invent a location choice.

## 11. Outsourced / Labor / Finance baseline

Outsourced:
- Outsourced Vendor is distinct from Supplier
- confirmed supply detail immediately creates OUTSOURCED sellable inventory and Vendor Payable obligation

Labor:
- Processing Execution + Sales Packaging Work → Employee Daily Wage → Employee Payable → Payment
- Processing wage aggregates before multiply, then floors final THB amount
- applied wage rate may be overridden at Daily Wage confirmation without rewriting configuration
- Sales Packaging/Handling is day-rate with no quantity/kg/box/hour field in v0.1
- Handling Work may be recorded while Sales is DRAFT or CONFIRMED (HANDLING-001 resolved 2026-09-03)
- late work after confirmed Daily Wage remains unresolved

Finance:
- Original Obligation + Adjustments - Settlements = Outstanding
- obligations / adjustments / settlements are truth
- Outstanding is a rebuildable transactional projection
- partial settlements are supported
- Supplier quality/weight deduction is a Payable Adjustment and never rewrites Procurement
- Payment/Receipt/Adjustment concurrency uses the applicable Outstanding Position row version
- confirmed Finance facts do not expose generic edit/delete
- FIN-003 confirmed Payment/Receipt correction method remains unresolved
- FIN-005 adjustment correction/reversal remains unresolved
- FIN-007/008/009 Company Pickup Transport final rounding, grouping boundary, and payee semantics remain unresolved

## 12. Data lifecycle / correction / Audit architecture

Edit / Soft Delete / Restore / Hard Delete are not generic CRUD.

Correction framework decisions are target/owning-domain specific:
- `ALLOW_DIRECT_AMENDMENT`
- `ALLOW_WITH_COMPENSATION`
- `BLOCK`

Confirmed-transaction correction requires:
- dependency assessment
- transaction-time revalidation
- owning-domain decision
- compensating facts when required
- audit
- idempotency
- atomic commit

There is no generic Update Any Entity, generic JSON Patch, generic Undo, or Hard Delete as correction.

Hard Delete architecture:
- highest-authority capability boundary
- target-specific route / command only
- dependency assessment by owning-domain knowledge
- no silent cascade across core traceability
- physical deletion
- retained same-transaction `HARD_DELETE` Audit
- Audit subject locator survives target deletion through ADR-005 historical non-FK metadata
- replay is resolved from CommandExecution before target lookup

### V8-C1 — Supplier Hard Delete COMPLETE

Endpoint:
- `POST /api/v1/data-protection/suppliers/{supplierId}/hard-delete`

Capability:
- `data-protection.hard-delete`

Dependency closure explicitly checks Procurement Entry supplier reference, SOURCE_TRACKED Processing Execution supplier reference, Inventory Movement/Position raw Supplier segment, and Procurement Supplier Payable. Any dependency blocks physical deletion. Successful physical deletion retains `party.supplier` HARD_DELETE audit and same-key replay succeeds after the Supplier row is gone.

### V8-C2 — Customer Hard Delete COMPLETE

Endpoint:
- `POST /api/v1/data-protection/customers/{customerId}/hard-delete`

Capability:
- `data-protection.hard-delete`

Direct typed-FK dependency closure is `sales.sales.customer_id`. Any Sale dependency blocks physical deletion. Successful physical deletion retains `party.customer` HARD_DELETE audit and same-key replay succeeds after the Customer row is gone.

### V8-C3 — Sales Allocation Correction validation slice

Endpoint:
- `POST /api/v1/sales/{salesId}/allocation-revisions`

Operation-specific capability policy:
- `sales.correct-allocation`

This is a technical authorization policy identifier around the confirmed correction operation. It does not create a role hierarchy or authorization persistence; capability grants remain deployment-configured by Account UUID under `docs/15`.

C3 uses `ALLOW_WITH_COMPENSATION` semantics:
- next immutable allocation revision
- current pointer replacement only
- `SALES_ALLOCATION_ADJUSTMENT` compensation for net allocation delta
- correction Audit event with subject UPDATE semantics
- `audit.correction_links` mode `COMPENSATION`
- persistent CommandId replay/conflict handling
- expected Sales row version concurrency
- Batch lifecycle guard before nonzero Inventory delta

No relation/schema/migration change is introduced by C3.

### Remaining V8 lifecycle / hard-delete boundary

Do **not** infer that Farmer, Employee, Outsourced Vendor, product/configuration, transaction, or other entities automatically support Hard Delete merely because Supplier/Customer do. REST architecture requires target-specific routes only for supported targets; a generic target catalogue has not been confirmed.

Soft Delete / Restore remain explicit lifecycle commands, but further implementation is currently blocked because:
- Restore is formally defined as business-aware
- owning-domain Restore rules for the next target are not yet confirmed
- current AuthN/AuthZ capability baseline does not define a general lifecycle-management capability
- `data-protection.hard-delete` must not be reused as an ordinary Soft Delete/Restore capability

Adding or assigning a new lifecycle capability is a security-architecture decision; do not silently create one during implementation.

## 13. Audit reference boundary

ADR-005 remains authoritative:
- normal domain relationships use typed real FKs
- `audit.audit_event_subjects.subject_kind + subject_key` is historical locator metadata only
- Audit locators intentionally do not FK back to deleted targets
- do not build a generic entity resolver over Audit locators
- audit payloads are not automatic row backups
- Hard Delete audit keeps minimum technical identity and controlled audit-safe metadata

For Sales Allocation Correction:
- Audit event kind is `CORRECTION`
- the changed subject is the Sales row, so subject `change_kind` remains the existing controlled value `UPDATE`
- correction lineage uses `audit.correction_links` with `COMPENSATION`

## 14. REST/API baseline

- ASP.NET Core 10 Minimal APIs
- `/api/v1`
- module `MapGroup` composition
- `TypedResults`
- first-party validation / Problem Details / OpenAPI
- HTTP DTO ≠ Application Command ≠ Domain Entity ≠ EF Entity
- business-intent command endpoints only

Every persisted Business Write requires:
- authenticated actor
- `Idempotency-Key` UUID
- canonical request hash containing Command Type + route business IDs + semantic body fields + expected concurrency tokens

Replay ordering is mandatory:
- acquire/check CommandId before current target/state/version lookup

Same key is replayable only for same actor + same Command Type + same canonical hash.
Different actor/type/hash returns `409 idempotency.key-reused`.

Versioned target mutation uses explicit `expectedRowVersion`; no ETag/If-Match concurrency contract.

Sales allocation correction route:
- `POST /api/v1/sales/{salesId}/allocation-revisions`
- request explicitly carries `mode`
- success is `201 Created`
- transport errors use 400; semantic input errors 422; stale/current-state/lifecycle/idempotency conflicts 409

Problem Details stable status/code taxonomy remains authoritative.

## 15. AuthN/AuthZ baseline

Authentication:
- provider-neutral external OIDC Authorization Code + PKCE
- ASP.NET Core encrypted Cookie application session
- pre-provisioned `system.accounts`
- exact validated `(issuer, subject)` resolves actor account
- account must remain active
- no ERP password store
- React does not own access/refresh tokens

Authorization:
- explicit operation/capability policy names
- grants are deployment-configured by persistent Account UUID
- no invented Admin/Manager/SuperAdmin business roles
- no roles/permissions/account-role persistence in v0.1

Confirmed operation-policy examples now include:
- `sales.confirm`
- `sales.correct-allocation`
- `finance.pay`
- `inventory.adjust`
- `data-protection.hard-delete`

`data-protection.hard-delete` is the highest-authority Hard Delete boundary and is not a generic lifecycle-management permission.

Cookie-authenticated unsafe browser requests require antiforgery; confirmed React header is `X-CSRF-TOKEN`.
Tailscale is network transport only and does not replace ERP authentication.

## 16. Adaptive React web baseline

One React application: `src/YowThi.Erp.Web`.

Three presentation experiences:
- Desktop
- Tablet
- Mobile

They share REST contracts, server-state/query core, authentication, locale, Problem Details, idempotency, concurrency, and business semantics.
Presentation composition may differ by device.

Current implemented web routes include:
- `/procurement/entries/new`
- `/outsourced/supply-details/new`
- `/processing/executions/new`

A built `dist` exists. These are real implemented React/Vite screens, but the complete ERP UI is not finished.

Formal broad UI sequencing remains P7 after required P6 backend sequencing. Stable backend slices may inform UI contracts without redefining the P6 hard gates.

## 17. Implementation phase status

```text
P0    Repository / solution scaffolding                  COMPLETE
P1    Shared technical foundation                        COMPLETE
P2    Full 55-relation Domain + EF model                COMPLETE
P3    API technical shell                                COMPLETE
P3.5  AuthN/AuthZ implementation architecture hard gate COMPLETE
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
      C3 Sales Allocation Correction                     LOCAL ACCEPTED; FORMAL VALIDATION GATE
      remaining lifecycle / targets / finance            HARD GATE
P7    React UI vertical slices                           NOT FORMALLY COMPLETE
P8    CI / production hardening                          FUTURE
```

## 18. Important unresolved Business Rule gaps

Authoritative details remain in `docs/06-business-rule-gap-register-v0.1.md`.

High-impact current gaps include:
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
- FIN-007/008/009 Company Pickup Transport final amount/grouping/payee semantics
- LIFE-001 Closed Batch reopen

`SALES-003` is no longer an unresolved gap; it was confirmed on 2026-09-04 and is retained as RESOLVED history in the Gap Register.

Deferred/safe controls in the Gap Register must not be silently upgraded into permanent Business Rules.

## 19. Remaining V8 hard-gate decisions

Sales Allocation correction input semantics are no longer a Business Fact hard gate.

Remaining V8 selection gates include:

### A. Soft Delete / Restore

Need a target-specific lifecycle decision:
- which target is next/applicable
- owning-domain Restore eligibility/dependency behavior

Also need a security-architecture capability decision for ordinary lifecycle management.
Do not reuse `data-protection.hard-delete`.

### B. Additional Hard Delete targets

Need explicit target support and owning-domain dependency closure before adding another target-specific route.
Do not create `/data-protection/entities/{type}/{id}` or infer support for every mapped entity.

### C. Finance correction / reversal

Blocked by FIN-003 and FIN-005.
Do not expose generic reversal/edit/delete APIs.

### D. Closed Batch reopen

Deferred under LIFE-001.
Do not implement.

## 20. GitHub validation / cost governance

Routine validation uses the Windows self-hosted runner only.
Required labels:
- `self-hosted`
- `yowthi-erp-v2`

Validation branch convention:
- `m*-validation`
- `p*-validation`

Formal main advances only after the exact validation-branch SHA has a successful self-hosted `dotnet.yml` run and is eligible for fast-forward.

Do not require routine GitHub-hosted runners, larger/paid runners, Codespaces, or unconfirmed metered services.
No force push is used for normal progression.

## 21. Recovery / next step

Current formal starting point and validation slice:

```text
main@609a4274c4f658f2c4b8e551b8decfc22da64afa
→ V8-C1 Supplier Hard Delete COMPLETE
→ V8-C2 Customer Hard Delete COMPLETE
→ SALES-003 RESOLVED 2026-09-04
→ V8-C3 CorrectSalesAllocation implemented
→ local hard gates 233/233 PASS
→ formal exact-SHA self-hosted validation / ff promotion required
```

C3 completion sequence:
1. review intended diff and remove all tool backup/temporary artifacts
2. focused commit on `p6-v8-sales-allocation-correction-validation`
3. publish exact SHA
4. self-hosted `dotnet.yml` on `YowThi-ERP-V2`
5. require success + required labels + `eligibleForMainFastForward=true`
6. switch main and fast-forward only
7. non-force push and fetch/read-back

After C3 formal completion, do not invent the next V8 command. Select the next slice only after one remaining hard-gate decision in section 19 is confirmed.
