# Current Design Checkpoint — YowThi ERP V2

Checkpoint status: **v0.1 implementation baseline through P6 V8 partial — Supplier / Customer Hard Delete complete; remaining V8 work at confirmed hard gate**
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

Completed implementation phases:
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

**P6 V8 — Correction / Lifecycle / Hard Delete commands: IN PROGRESS, currently at a hard Business Fact / security-architecture gate after C1–C2.**

Do not mark P6 or V8 COMPLETE until the remaining V8 command set is confirmed and implemented or explicitly deferred by formal decision.

## 5. Current formal Git baseline

Formal implementation baseline before this checkpoint-doc commit:

- `main = origin/main`
- SHA: `2c7a80adee40d61f6dae59036682a7682d36a871`
- commit: `feat: add customer hard delete command`
- promotion method: fast-forward only
- no force push
- working tree was clean after remote fetch/read-back

P6 V7 formal evidence:
- commit: `e294107f2f991cbf44ebc0c28765a456e4967d22` — `feat: complete batch close commands`
- self-hosted run: `33818749647`
- runner: `YowThi-ERP-V2`
- labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- local gates: Domain 29/29, Architecture 66/66, API Contract 55/55, PostgreSQL Integration 59/59

P6 V8-C1 Supplier Hard Delete evidence:
- commit: `7c7398002d237b288a80952e0d75c73a1167d3bf` — `feat: add supplier hard delete command`
- validation branch: `p6-v8-supplier-hard-delete-validation`
- self-hosted run: `33822363262`
- runner: `YowThi-ERP-V2`
- required labels present
- conclusion: `success`
- local gates: Domain 29/29, Architecture 66/66, API Contract 58/58, PostgreSQL Integration 66/66 — total 219/219

P6 V8-C2 Customer Hard Delete evidence:
- commit: `2c7a80adee40d61f6dae59036682a7682d36a871` — `feat: add customer hard delete command`
- validation branch: `p6-v8-customer-hard-delete-validation`
- self-hosted run: `33824567970`
- runner: `YowThi-ERP-V2`
- required labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`
- local gates: Domain 29/29, Architecture 66/66, API Contract 61/61, PostgreSQL Integration 69/69 — total 225/225

V8-C1/C2 introduced no schema or EF migration changes.

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
- PostgreSQL accepted version: 18.6
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

## 9. Inventory baseline

Inventory identity dimensions:
- Origin
- Source Batch
- Inventory Object
- Storage Location
- optional Raw Source Segment

Movement types include receipts, processing consume/produce, final-package consume/produce, transfers, sales issue, adjustment, allocation adjustment, and batch reconciliation.

P6 V7 lifecycle/concurrency protection:
- `TransferInventory`, `AdjustInventory`, and `ConfirmProcessingExecution` acquire a shared Batch lifecycle guard
- shared writer lock uses PostgreSQL `FOR SHARE`
- Batch Close uses conflicting exclusive lifecycle lock/update
- concurrent writers remain possible while Close cannot race through them
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

Confirmed allocation correction persistence architecture is already defined:
- append a new immutable Allocation Revision + Items
- create compensating `SALES_ALLOCATION_ADJUSTMENT` Inventory Movements
- replace current allocation pointers
- never rewrite prior Inventory Movement history

However the **business input semantics for the correction command are not confirmed**: it is not yet known whether a correction submits a complete replacement allocation set or supplies manual overrides followed by system re-allocation of the remainder. This is now registered as `SALES-003` Class A. Do not expose the correction command until that real YowThi behavior is confirmed.

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

Dependency closure explicitly checked:
- Procurement Entry supplier reference
- SOURCE_TRACKED Processing Execution supplier reference
- Inventory Movement raw Supplier segment
- Inventory Position raw Supplier segment
- Procurement Supplier Payable

Any dependency blocks physical deletion and the failed command leaves no committed CommandExecution/Audit.
Successful physical deletion retains `party.supplier` HARD_DELETE audit and same-key replay succeeds after the Supplier row is gone.

### V8-C2 — Customer Hard Delete COMPLETE

Endpoint:
- `POST /api/v1/data-protection/customers/{customerId}/hard-delete`

Capability:
- `data-protection.hard-delete`

Direct typed-FK dependency closure:
- `sales.sales.customer_id`

Any Sale dependency blocks physical deletion and the failed command leaves no committed CommandExecution/Audit.
Successful physical deletion retains `party.customer` HARD_DELETE audit and same-key replay succeeds after the Customer row is gone.

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

`audit.correction_links` remains available for owning-domain correction lineage when a confirmed correction command exists.

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

Problem Details stable status/code taxonomy remains authoritative:
- 400 transport/request validation
- 404 addressed resource absent
- 409 current-state/concurrency/idempotency/dependency conflict
- 422 semantic command input invalid independent of a race

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

Confirmed examples include:
- `sales.confirm`
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
      remaining correction/lifecycle/targets             HARD GATE
P7    React UI vertical slices                           NOT FORMALLY COMPLETE
P8    CI / production hardening                          FUTURE
```

## 18. Important unresolved Business Rule gaps

Authoritative details remain in `docs/06-business-rule-gap-register-v0.1.md`.

High-impact current gaps include:
- PROC-001 completed Procurement Batch late entry
- PROCESS-001 multi-location input resolution
- OUT-003 late detail after Outsourced Batch Closed
- SALES-001 Sales issue location when stock spans locations
- SALES-003 allocation correction submission semantics
- BATCH-001 automatic vs user-confirmed Batch Close
- LABOR-001 late work after Daily Wage confirmation
- FIN-001/002 over-settlement controls
- FIN-003 confirmed Payment/Receipt correction method
- FIN-004 deduction causing negative Payable Outstanding
- FIN-005 Payable Adjustment correction/reversal
- FIN-007/008/009 Company Pickup Transport final amount/grouping/payee semantics
- LIFE-001 Closed Batch reopen

Deferred/safe controls in the Gap Register must not be silently upgraded into permanent Business Rules.

## 19. V8 hard-gate decisions now required before further implementation

The next V8 implementation cannot safely be selected until at least one owning-domain/security decision becomes available.

### A. Sales Allocation correction

Persistence/result semantics are confirmed, but the command input behavior is not.
Need a real YowThi Business Fact for `SALES-003`:
- correction supplies the complete official replacement allocation set, **or**
- correction supplies manual overrides and the system re-runs allocation for the remaining quantity, **or**
- another real operating behavior

Do not implement one of these by assumption.

### B. Soft Delete / Restore

Need a target-specific lifecycle decision:
- which target is next/applicable
- owning-domain Restore eligibility/dependency behavior

Also need a security-architecture capability decision for ordinary lifecycle management.
Do not reuse `data-protection.hard-delete`.

### C. Additional Hard Delete targets

Need explicit target support and owning-domain dependency closure before adding another target-specific route.
Do not create `/data-protection/entities/{type}/{id}` or infer support for every mapped entity.

### D. Finance correction / reversal

Blocked by FIN-003 and FIN-005.
Do not expose generic reversal/edit/delete APIs.

### E. Closed Batch reopen

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

Current implementation state after V8-C2:

```text
main@2c7a80adee40d61f6dae59036682a7682d36a871
→ Supplier Hard Delete COMPLETE
→ Customer Hard Delete COMPLETE
→ V8 remaining work reaches confirmed hard gate
```

Next action is **not** to invent another command.

Recovery sequence:
1. confirm at least one V8 hard-gate decision from section 19 using real YowThi operating facts / explicit security architecture
2. cross-check `docs/05`, `docs/06`, `docs/10`, `docs/12`, `docs/13`, `docs/15`, ADR-005
3. create one focused target/owning-domain validation slice
4. include persistent idempotency, Audit, concurrency/dependency/rollback semantics as applicable
5. local build/test hard gates
6. focused commit to `p*-validation`
7. exact-SHA self-hosted validation on `YowThi-ERP-V2`
8. fast-forward-only promotion to main after success

Until one of those decisions is confirmed, P6 V8 remains **IN PROGRESS / HARD GATE**, and P6 must not be declared complete.
