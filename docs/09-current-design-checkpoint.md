# Current Design Checkpoint — YowThi ERP V2

Checkpoint status: **v0.1 implementation baseline through P6 / V8 COMPLETE and P7 COMPLETE. The P7 system skeleton S0–S12, Nature Green presentation, Desktop / Tablet / Mobile adaptive UX, and current-route zh-TW / th-TH presentation acceptance are COMPLETE. Remaining lifecycle / Hard Delete / Batch-control candidates remain explicitly deferred or future target-specific scope.**

Purpose: recover the current architecture and implementation state if conversational context is lost.

## 1. Highest-authority interpretation

Business Rules come only from real YowThi operating facts.

Do **not** use a business model to unnecessarily constrain ERP data maintenance. Registration of real operations and ERP control of system data/state are different logical concerns.

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

Physical inventory reconciliation:
- physical stock may differ because of shrinkage, damage, weighing variance, handling loss, spoilage, missing stock, or other real-world discrepancy
- reconcile through stocktake / `AdjustInventory`
- do not rewrite unrelated historical business movements solely to force inventory to equal a later physical count

Recovery precedence:
1. Business Discovery / Command Contracts / Business Rule Gap Register for real Business Facts
2. `docs/17-erp-registration-data-control-boundary-v0.1.md`
3. this current checkpoint
4. `docs/10-relational-model-consolidation-v0.1.md`
5. `docs/11-ef-core-mapping-architecture-v0.1.md`
6. `docs/12-rest-api-architecture-v0.1.md`
7. `docs/13-implementation-sequencing-build-plan-v0.1.md`
8. `docs/14-github-cost-governance-v0.1.md`
9. `docs/15-authn-authz-implementation-architecture-v0.1.md`
10. `docs/16-adaptive-web-ui-architecture-v0.1.md` and applicable ADRs

Later implementation supplements:
- `docs/18-procurement-batch-reopen-control-v0.1.md` — V8-C9 Procurement Batch Reopen
- `docs/19-outsourced-vendor-lifecycle-control-v0.1.md` — V8-C10 Outsourced Vendor Soft Delete / Restore
- `docs/20-farmer-lifecycle-control-v0.1.md` — V8-C11 Farmer Soft Delete / Restore
- `docs/21-employee-lifecycle-control-v0.1.md` — V8-C12 Employee Soft Delete / Restore + current-use command guards
- `docs/22-sales-packaging-item-lifecycle-control-v0.1.md` — V8-C13 Sales Packaging Item Soft Delete / Restore
- `docs/23-sales-product-group-lifecycle-control-v0.1.md` — V8-C14 Sales Product Group Soft Delete / Restore
- `docs/24-container-lifecycle-control-v0.1.md` — V8-C15 Container Soft Delete / Restore + Processing tare/current-use boundary
- `docs/25-warehouse-lifecycle-control-v0.1.md` — V8-C16 Warehouse Soft Delete / Restore + child Storage Location preservation boundary
- `docs/26-outsourced-vendor-hard-delete-control-v0.1.md` — V8-C17 Outsourced Vendor Hard Delete + structural dependency closure / runner-local validation evidence
- `docs/27-farmer-hard-delete-control-v0.1.md` — V8-C18 Farmer Hard Delete + Procurement Entry / Finance Payable structural dependency closure / runner-local validation evidence
- `docs/28-p6-v8-closure-checkpoint-v0.1.md` — formal P6 / V8 closure + deferred/future target-specific scope boundary
- `docs/29-p7-system-skeleton-closure-checkpoint-v0.1.md` — P7 S0–S12 system-skeleton closure + 12/12 operational web modules + presentation redesign boundary
- `docs/30-p7-presentation-closure-checkpoint-v0.1.md` — P7 Nature Green presentation closure + Desktop/Tablet/Mobile + current-route zh-TW/th-TH acceptance
- for their target-specific scopes, these later supplements resolve older omissions without superseding the broader Command/REST architecture

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
- React + TypeScript + Vite + React Router + TanStack Query + pnpm
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
- development PostgreSQL: `127.0.0.1:55432/yowthi_dev`
- PostgreSQL 18.6
- P5: 1 applied / 0 pending
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
P6    Business / ERP Control vertical slices             COMPLETE
  V1  ConfirmProcurementEntry                            COMPLETE
  V2  ConfirmOutsourcedSupplyDetail                      COMPLETE
  V3  ConfirmProcessingExecution                         COMPLETE
  V4  ConfirmSales                                       COMPLETE
  V5  Sales Packaging Work + Daily Wage                 COMPLETE
  V6  Finance adjustment / settlements                   COMPLETE
  V7  Inventory transfer / adjustment / Batch Close     COMPLETE
  V8  Correction / Lifecycle / Hard Delete              COMPLETE
      C1 Supplier Hard Delete                            COMPLETE
      C2 Customer Hard Delete                            COMPLETE
      C3 Sales Allocation Correction                     COMPLETE
      ERP Registration / Data Control clarification      COMPLETE
      C4 Supplier Soft Delete / Restore                  COMPLETE
      C5 Customer Soft Delete / Restore                  COMPLETE
      C6 Payment Amount Correction                       COMPLETE
      C7 Receipt Amount Correction                       COMPLETE
      C8 Payable Adjustment Correction                   COMPLETE
      C9 Procurement Batch Reopen                        COMPLETE
      C10 Outsourced Vendor Soft Delete / Restore        COMPLETE
      C11 Farmer Soft Delete / Restore                   COMPLETE
      C12 Employee Soft Delete / Restore                 COMPLETE
      C13 Sales Packaging Item Soft Delete / Restore     COMPLETE
      C14 Sales Product Group Soft Delete / Restore      COMPLETE
      C15 Container Soft Delete / Restore                COMPLETE
      C16 Warehouse Soft Delete / Restore                COMPLETE
      C17 Outsourced Vendor Hard Delete                  COMPLETE
      C18 Farmer Hard Delete                              COMPLETE
      remaining candidate ERP Control scope               DEFERRED / FUTURE
P7    React UI system skeleton S0-S12                  COMPLETE
      Nature Green presentation / adaptive UX / i18n    COMPLETE
P8    CI / production hardening                          FUTURE
```

P6/V8 is COMPLETE because remaining candidate control scope is now explicitly deferred or future target-specific scope; unresolved Business Facts remain governed by the Gap Register.

## 5. Current formal Git baseline

Formal implementation baseline immediately before this checkpoint-document commit:
- `main = origin/main`
- SHA: `cae5a5c0bf2f1cec7bf0ffa96dde7cbafbbe4ced`
- commit: `feat(web): complete nature green management presentation`
- promotion: ff-only
- push: non-force
- remote fetch/read-back: clean
- Formal r20 primary channel; Bootstrap r2 remains independent recovery/read-back channel

Current docs checkpoint branch:
- `p7-presentation-closure-checkpoint-validation`

C18 implementation validation:
- branch: `p6-v8-farmer-hard-delete-validation`
- exact SHA: `0871bd006dfe1fa48db0b305a043277e09debfa2`
- local hard gates: **328/328 PASS**
- Domain 29/29
- Architecture 66/66
- API Contract 110/110
- PostgreSQL Integration 123/123
- final Release solution build: 0 errors; only known solution custom-output `NETSDK1194`
- PostgreSQL validation endpoint: 18.6 / `yowthi_dev` / `isInRecovery=false`
- normal Formal r17 / Bootstrap r2 typed GitHub workflow-status queries returned invocation errors for this publication
- self-hosted runner-local Worker evidence proves exact SHA, workflow branch/ref, job `build-test`, and final `Succeeded`
- runner registration: `YowThi-ERP-V2`
- workflow definition requires labels `self-hosted`, `yowthi-erp-v2`
- Worker log: `C:\actions-runner\actions-runner\_diag\Worker_20260905-081554-utc.log`
- no GitHub run ID is asserted because the typed query did not return one during the incident
- ff-only main promotion + non-force push + fetch/read-back: COMPLETE

## 6. ERP Registration / Data Control boundary — CONFIRMED 2026-09-04


Business Fact Registration examples:
- Procurement
- Processing
- Sales
- Payment
- Receipt
- Employee Work

ERP Data Control / Maintenance examples:
- correction of wrongly entered data
- Soft Delete
- Restore
- Activate / Deactivate
- Reopen
- Hard Delete under Data Protection

ERP Control does not require a separate Business Rule merely to permit a state/data change. It still requires applicable technical controls:
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

## 7. Gap classification

`docs/06-business-rule-gap-register-v0.1.md` includes `CONTROL`.

`CONTROL` means:
- ERP Data Control / maintenance concern
- not a Business Rule blocker
- implementation governed by technical safety and authorization

Current control history:
- `FIN-003` → CONTROL; Payment and Receipt amount correction complete in C6/C7
- `FIN-005` → CONTROL; Payable Adjustment correction complete in C8
- `LIFE-001` → CONTROL; Procurement Batch Reopen complete in C9; other Batch types are separate target-specific scope
- `SALES-003` remains RESOLVED Business Fact history

Important unresolved Business Rule gaps remain authoritative, including applicable Procurement, Processing, Outsourced, Sales location, Labor, Finance transport, and deferred extension gaps. Do not invent values for them.

`OUT-003` remains a Class A Business Rule gap after P6/V8 closure. `ConfirmOutsourcedSupplyDetail` blocks while the Batch is `CLOSED`; therefore simply reopening that Batch to `ACTIVE` would subsequently permit late detail and would decide an unconfirmed real operating fact. Outsourced Supply Batch Reopen remains DEFERRED until real late-detail behavior is confirmed, or a control design can preserve the unresolved Business Fact safely.

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
- correction Audit + lineage
- persistent idempotency + Outbox
- no rewrite of prior `SALES_ISSUE` / Inventory Movement history

Formal implementation:
- SHA `e5b92dcc8283f6f1f59e4452fc582718236e5a77`
- local hard gates 233/233 PASS
- self-hosted run `33828986488` SUCCESS

## 9. Hard Delete baseline

Hard Delete remains the highest-authority Data Protection operation.

Capability:
- `data-protection.hard-delete`

Completed targets:
- Supplier — V8-C1
- Customer — V8-C2
- Outsourced Vendor — V8-C17 (`docs/26-outsourced-vendor-hard-delete-control-v0.1.md`); any typed Outsourced Supply Batch dependency blocks physical deletion
- Farmer — V8-C18 (`docs/27-farmer-hard-delete-control-v0.1.md`); any typed Procurement Entry or Finance Payable dependency blocks physical deletion

Requirements:
- explicit target support
- structural dependency closure
- expected row version where applicable
- same-transaction `HARD_DELETE` Audit
- replay before target lookup
- no silent cascade

Additional Hard Delete targets are DEFERRED / future target-specific scope. They do not need a Business Rule merely to be considered, but they require a real operational need and target-specific dependency closure before implementation.

No generic `/data-protection/entities/{type}/{id}` endpoint.

## 10. Party lifecycle baseline

Supplier C4:
- `SoftDeleteSupplier` / `RestoreSupplier`
- capability `party.supplier.lifecycle`
- historical dependencies do not themselves block Soft Delete because the row remains for FK/traceability
- Restore preserves original `active`
- local hard gates 239/239 PASS
- implementation SHA `22fd7770445d915fd428591dff3d7d1490911922`
- self-hosted run `33840747740` SUCCESS

Customer C5:
- `SoftDeleteCustomer` / `RestoreCustomer`
- capability `party.customer.lifecycle`
- existing Sale FK does not block Soft Delete; it does block Hard Delete
- Restore preserves original `active`
- local hard gates 245/245 PASS
- implementation SHA `e22d281e4c1c92bca6d57e6a301e2ea1cacc5cbb`
- self-hosted run `33842998109` SUCCESS

Outsourced Vendor C10:
- `SoftDeleteOutsourcedVendor` / `RestoreOutsourcedVendor`
- capability `party.outsourced-vendor.lifecycle`
- existing Outsourced Supply Batch FK does not block Soft Delete because the Vendor row remains for typed FK integrity and historical traceability
- current-use selectors require `active = true AND deleted_at IS NULL`
- Restore preserves original `active`; it does not reactivate an inactive Vendor
- local hard gates 276/276 PASS
- implementation SHA `470dfdda2fcf5da000f6174892d338c443815fd9`
- self-hosted run `33877179734` SUCCESS
- authoritative implementation supplement: `docs/19-outsourced-vendor-lifecycle-control-v0.1.md`

Farmer C11:
- `SoftDeleteFarmer` / `RestoreFarmer`
- capability `party.farmer.lifecycle`
- existing Procurement Entry Farmer FK does not block Soft Delete because the Farmer row remains for typed FK integrity and historical traceability
- current-use Procurement Farmer selectors require `active = true AND deleted_at IS NULL`
- Restore preserves original `active`; it does not reactivate an inactive Farmer
- local hard gates 282/282 PASS
- implementation SHA `ac5a312bae6e4d20663bb29f0b272deedf9ba568`
- self-hosted run `33882137271` SUCCESS
- authoritative implementation supplement: `docs/20-farmer-lifecycle-control-v0.1.md`

Employee C12:
- `SoftDeleteEmployee` / `RestoreEmployee`
- capability `party.employee.lifecycle`
- historical Processing Execution, Sales Packaging Work Record, and Employee Daily Wage typed references remain intact because Soft Delete keeps the Employee row
- current-use Processing Employee selector requires `active = true AND deleted_at IS NULL`
- Restore preserves original `active`; it does not reactivate an inactive Employee
- `RecordSalesPackagingWork` and `ConfirmEmployeeDailyWage` now reject inactive/soft-deleted Employee IDs server-side, matching the established Processing current-use guard
- rejected new registrations leave no committed CommandExecution/Audit/Outbox residue
- local hard gates 289/289 PASS
- implementation SHA `82fe55f583e066c04456695b735b6ac8c8ef3d1f`
- self-hosted run `33889424952` SUCCESS
- authoritative implementation supplement: `docs/21-employee-lifecycle-control-v0.1.md`

Ordinary lifecycle capabilities do not imply `data-protection.hard-delete`.

## 11. Finance correction baseline

Finance truth/projection:

```text
Original Obligation + Adjustments - Settlements = Outstanding
```

Registration error:
- target-specific direct amendment is valid ERP Control when structurally safe
- rebuild affected Outstanding transactionally
- preserve original event metadata where designed
- Audit before/after + idempotency + concurrency
- do not fabricate a reversal Business Fact

Completed:
- C6 `CorrectPaymentAmount` — SHA `cdd652b639a56c9896d7403cbbf501331ce7aca0`, 251/251, run `33850492563`
- C7 `CorrectReceiptAmount` — SHA `768c9673f242cfbc0c53b1cd7ecd6e3fa273f770`, 257/257, run `33854672829`
- C8 `CorrectPayableAdjustment` — SHA `8b660b677eef4463645496140eff5dd471432cca`, 264/264, run `33858815725`

Capability:
- `finance.correct`

If money actually moves again, record a new real Finance Business Fact.

## 12. Procurement Batch Reopen — V8-C9 COMPLETE

Authoritative C9 supplement:
- `docs/18-procurement-batch-reopen-control-v0.1.md`

Command:
- `ReopenProcurementBatch`

Endpoint:
- `POST /api/v1/procurement/batches/{procurementBatchId}/reopen`

OperationId:
- `Procurement_ReopenBatch`

Capability:
- `procurement.batch.lifecycle`

Current-state semantics:
- require current non-deleted `CLOSED` Procurement Batch
- compare expected Procurement Batch row version
- set `lifecycle_status = ACTIVE`
- clear current-state `closed_at` / `closed_by_account_id` because the existing structural closing-state constraint requires ACTIVE rows to have null close markers
- increment row version
- preserve `procurement_status` and completion metadata

Historical/inventory semantics:
- prior Close Audit remains
- prior `BATCH_RECONCILIATION` Operation/Movements remain immutable
- Reopen creates no reversal Inventory Movement
- Inventory Position remains at post-Close value
- actual physical discrepancy after reopen is registered through stocktake / `AdjustInventory`

Audit/idempotency:
- `DATA_LIFECYCLE`
- subject `procurement.batch`
- structural change kind `UPDATE`
- `change_summary` records `CLOSED -> ACTIVE`
- persistent CommandId replay before current-state lookup
- Outbox `procurement.batch.reopened`

Security:
- `procurement.batch.lifecycle` is separate from `procurement.confirm`
- it does not imply Hard Delete or permission to rewrite Inventory history

No relation/schema/model snapshot/migration change.

C9 completes Procurement Batch Reopen only. Outsourced Supply Batch Reopen remains deferred under `OUT-003`; do not infer that a lifecycle state change permits late Outsourced Supply Detail.

## 13. Outsourced Vendor lifecycle — V8-C10 COMPLETE

Authoritative C10 supplement:
- `docs/19-outsourced-vendor-lifecycle-control-v0.1.md`

Commands:
- `SoftDeleteOutsourcedVendor`
- `RestoreOutsourcedVendor`

Endpoints:
- `POST /api/v1/party/outsourced-vendors/{outsourcedVendorId}/soft-delete`
- `POST /api/v1/party/outsourced-vendors/{outsourcedVendorId}/restore`

Capability:
- `party.outsourced-vendor.lifecycle`

Lifecycle semantics:
- Soft Delete sets `deleted_at` / `deleted_by_account_id`, increments row version, and preserves `active`
- Restore clears deletion markers, increments row version, and preserves `active`
- no cascade or removal of historical Outsourced Supply Batch references
- current-use selectors already require both active and non-deleted Vendor state

Audit/idempotency:
- `DATA_LIFECYCLE`
- subject `party.outsourced-vendor`
- `SOFT_DELETE` / `RESTORE`
- persistent CommandId replay before current lifecycle/version validation
- failed lifecycle/concurrency attempts leave no committed CommandExecution or lifecycle Audit residue

No relation/schema/model snapshot/migration change.

C10 does not implement or authorize Outsourced Supply Batch Reopen. `OUT-003` remains unresolved and authoritative.

## 14. Farmer lifecycle — V8-C11 COMPLETE

Authoritative C11 supplement:
- `docs/20-farmer-lifecycle-control-v0.1.md`

Commands:
- `SoftDeleteFarmer`
- `RestoreFarmer`

Endpoints:
- `POST /api/v1/party/farmers/{farmerId}/soft-delete`
- `POST /api/v1/party/farmers/{farmerId}/restore`

Capability:
- `party.farmer.lifecycle`

Lifecycle semantics:
- Soft Delete sets `deleted_at` / `deleted_by_account_id`, increments row version, and preserves `active`
- Restore clears deletion markers, increments row version, and preserves `active`
- no cascade or removal of historical Procurement Entry Farmer references
- current-use Procurement Farmer selector requires `active = true AND deleted_at IS NULL`
- an inactive Farmer remains inactive after Restore and is not available for new Procurement registration

Audit/idempotency:
- `DATA_LIFECYCLE`
- subject `party.farmer`
- `SOFT_DELETE` / `RESTORE`
- persistent CommandId replay before current lifecycle/version validation
- failed lifecycle/concurrency attempts leave no committed CommandExecution or lifecycle Audit residue
- no Outbox message for this local Party master lifecycle transition

No relation/schema/model snapshot/migration change.

C11 does not alter any unresolved Business Rule gap. `OUT-003` and Outsourced Supply Batch Reopen remain unchanged/deferred.

## 15. Employee lifecycle — V8-C12 COMPLETE

Authoritative C12 supplement:
- `docs/21-employee-lifecycle-control-v0.1.md`

Commands:
- `SoftDeleteEmployee`
- `RestoreEmployee`

Endpoints:
- `POST /api/v1/party/employees/{employeeId}/soft-delete`
- `POST /api/v1/party/employees/{employeeId}/restore`

Capability:
- `party.employee.lifecycle`

Lifecycle semantics:
- Soft Delete sets `deleted_at` / `deleted_by_account_id`, increments row version, and preserves `active`
- Restore clears deletion markers, increments row version, and preserves `active`
- no cascade or removal of historical Processing Execution, Sales Packaging Work Record, or Employee Daily Wage references
- current-use Processing Employee selector requires `active = true AND deleted_at IS NULL`
- an inactive Employee remains inactive after Restore and remains unavailable for current-use Processing selection

Current-use command guard closure:
- Processing already rejected inactive/soft-deleted Employee state
- C12 adds the same server-side current-master-state rejection to `RecordSalesPackagingWork`
- C12 adds the same server-side current-master-state rejection to `ConfirmEmployeeDailyWage`
- these are consistency guards for new Business Fact registration, not new Business Rules
- historical work/wage/processing facts remain intact
- rejected commands roll back newly acquired CommandExecution and leave no committed Audit/Outbox residue

Audit/idempotency:
- lifecycle `event_kind = DATA_LIFECYCLE`
- subject `party.employee`
- `SOFT_DELETE` / `RESTORE`
- persistent CommandId replay before current lifecycle/version validation
- failed lifecycle/concurrency attempts leave no committed CommandExecution or lifecycle Audit residue
- no Outbox message for this local Party master lifecycle transition

No relation/schema/model snapshot/migration change.

C12 does not alter any unresolved Business Rule gap. `OUT-003` and Outsourced Supply Batch Reopen remain unchanged/deferred.

## 16. Sales Packaging Item lifecycle — V8-C13 COMPLETE

Authoritative C13 supplement:
- `docs/22-sales-packaging-item-lifecycle-control-v0.1.md`

Commands:
- `SoftDeleteSalesPackagingItem`
- `RestoreSalesPackagingItem`

Endpoints:
- `POST /api/v1/sales-handling/packaging-items/{salesPackagingItemId}/soft-delete`
- `POST /api/v1/sales-handling/packaging-items/{salesPackagingItemId}/restore`

Capability:
- `sales-handling.packaging-item.lifecycle`

Lifecycle semantics:
- Soft Delete sets `deleted_at` / `deleted_by_account_id`, increments row version, and preserves `active`
- Restore clears deletion markers, increments row version, and preserves `active`
- no cascade or removal of historical Sales Packaging Work Record references
- historical `sales_packaging_work_records.sales_packaging_item_id` typed FK remains intact
- an inactive item remains inactive after Restore

Current-use behavior:
- `RecordSalesPackagingWork` already required the packaging item to be active and non-deleted before C13
- C13 does not change that Business Fact command or add a new Business Rule
- a soft-deleted/inactive item is rejected for new packaging-work registration with `sales-handling.item-inactive`
- rejected registration rolls back newly acquired CommandExecution and leaves no committed Audit/Outbox residue

Audit/idempotency:
- lifecycle `event_kind = DATA_LIFECYCLE`
- subject `sales-handling.packaging-item`
- `SOFT_DELETE` / `RESTORE`
- persistent CommandId replay before current lifecycle/version validation
- failed lifecycle/concurrency attempts leave no committed CommandExecution or lifecycle Audit residue
- no Outbox message for this local master lifecycle transition

No relation/schema/model snapshot/migration change.

C13 does not alter any unresolved Business Rule gap. `OUT-003` and Outsourced Supply Batch Reopen remain unchanged/deferred.

## 17. Sales Product Group lifecycle — V8-C14 COMPLETE

Authoritative C14 supplement:
- `docs/23-sales-product-group-lifecycle-control-v0.1.md`

Commands:
- `SoftDeleteSalesProductGroup`
- `RestoreSalesProductGroup`

Endpoints:
- `POST /api/v1/product/sales-product-groups/{salesProductGroupId}/soft-delete`
- `POST /api/v1/product/sales-product-groups/{salesProductGroupId}/restore`

Capability:
- `product.sales-product-group.lifecycle`

Lifecycle semantics:
- Soft Delete sets Group `deleted_at` / `deleted_by_account_id`, increments Group row version, and preserves Group `active`
- Restore clears deletion markers, increments Group row version, and preserves Group `active`
- existing `sales_products.sales_product_group_id` typed FK remains intact
- no cascade into child Sales Product lifecycle state
- child Sales Product `active`, deletion markers, and row version remain unchanged
- inactive Group remains inactive after Restore

Business Fact boundary:
- C14 does not change `ConfirmSales`, Sales allocation, Processing execution, Processing module output, Sales Product lifecycle, or Procurement Product lifecycle
- Group lifecycle does not silently disable existing Sales Products
- no new Business Rule was added

Audit/idempotency:
- lifecycle `event_kind = DATA_LIFECYCLE`
- subject `product.sales-product-group`
- `SOFT_DELETE` / `RESTORE`
- persistent CommandId replay before current lifecycle/version validation
- failed lifecycle/concurrency attempts leave no committed CommandExecution or lifecycle Audit residue
- no Outbox message for this local master lifecycle transition

No relation/schema/model snapshot/migration change.

C14 does not alter any unresolved Business Rule gap. `OUT-003` and Outsourced Supply Batch Reopen remain unchanged/deferred.

## 18. Container lifecycle — V8-C15 COMPLETE

Authoritative C15 supplement:
- `docs/24-container-lifecycle-control-v0.1.md`

Commands:
- `SoftDeleteContainer`
- `RestoreContainer`

Endpoints:
- `POST /api/v1/infrastructure/containers/{containerId}/soft-delete`
- `POST /api/v1/infrastructure/containers/{containerId}/restore`

Capability:
- `infrastructure.container.lifecycle`

Structural/lifecycle semantics:
- Soft Delete sets Container `deleted_at` / `deleted_by_account_id`, increments Container row version, and preserves Container `active`
- Restore clears deletion markers, increments Container row version, and preserves Container `active`
- `route_input_configs.container_id` and `process_materials.container_id` typed Restrict FKs remain intact
- no cascade into Route Input or Process Material state
- Process Material `active`, deletion markers, and row version remain unchanged
- inactive Container remains inactive after Restore

Historical/current-use Processing boundary:
- completed SCALE_NET Processing stores `tare_weight_snapshot` rather than a Container FK
- C15 does not rewrite historical tare/derived/consumption facts
- `ConfirmProcessingExecution.ResolveTareAsync` already required Container `active = true AND deleted_at IS NULL`
- C15 preserves and validates that existing current-use guard instead of inventing a new Processing Business Rule
- a soft-deleted Container remains referenced by configuration but is rejected for new Processing use
- rejected new Processing rolls back newly acquired CommandExecution and leaves no committed Audit/Outbox residue

Audit/idempotency:
- lifecycle `event_kind = DATA_LIFECYCLE`
- subject `infrastructure.container`
- `SOFT_DELETE` / `RESTORE`
- persistent CommandId replay before current lifecycle/version validation
- failed lifecycle/concurrency attempts leave no committed CommandExecution or lifecycle Audit residue
- no Outbox message for this local master lifecycle transition

No relation/schema/model snapshot/migration change.

C15 does not alter any unresolved Business Rule gap. `ProcurementProduct` and `SalesProduct` lifecycle remain TO VERIFY / DEFERRED candidates, and `OUT-003` / Outsourced Supply Batch Reopen remain unchanged/deferred.

## 19. Warehouse lifecycle — V8-C16 COMPLETE

Authoritative C16 supplement:
- `docs/25-warehouse-lifecycle-control-v0.1.md`

Commands:
- `SoftDeleteWarehouse`
- `RestoreWarehouse`

Endpoints:
- `POST /api/v1/infrastructure/warehouses/{warehouseId}/soft-delete`
- `POST /api/v1/infrastructure/warehouses/{warehouseId}/restore`

Capability:
- `infrastructure.warehouse.lifecycle`

Structural/lifecycle semantics:
- Soft Delete sets Warehouse `deleted_at` / `deleted_by_account_id`, increments Warehouse row version, and preserves Warehouse `active`
- Restore clears deletion markers, increments Warehouse row version, and preserves Warehouse `active`
- `storage_locations.warehouse_id` typed Restrict FK remains intact
- no cascade into child Storage Location lifecycle state
- child Storage Location `warehouse_id`, `active`, deletion markers, and row version remain unchanged
- inactive Warehouse remains inactive after Restore

Business Fact boundary:
- C16 does not change Procurement, Inventory, Processing, Sales, or Sales Allocation command behavior
- Warehouse lifecycle does not silently disable or alter child Storage Locations
- no new Business Rule was added

Audit/idempotency:
- lifecycle `event_kind = DATA_LIFECYCLE`
- subject `infrastructure.warehouse`
- `SOFT_DELETE` / `RESTORE`
- persistent CommandId replay before current lifecycle/version validation
- failed lifecycle/concurrency attempts leave no committed CommandExecution or lifecycle Audit residue
- no Outbox message for this local master lifecycle transition

No relation/schema/model snapshot/migration change.

C16 dependency scan leaves `StorageLocation`, `ProcessMaterial`, and `ProcessingRoute` lifecycle as DEFERRED candidates because current-use behavior is not uniformly resolved. `ProcurementProduct` and `SalesProduct` lifecycle remain TO VERIFY / DEFERRED candidates. `OUT-003` and Outsourced Supply Batch Reopen remain unchanged/deferred.

## 20. Outsourced Vendor Hard Delete — V8-C17 COMPLETE

Authoritative C17 supplement:
- `docs/26-outsourced-vendor-hard-delete-control-v0.1.md`

Command:
- `HardDeleteOutsourcedVendor`

Endpoint:
- `POST /api/v1/data-protection/outsourced-vendors/{outsourcedVendorId}/hard-delete`

OperationId:
- `DataProtection_HardDeleteOutsourcedVendor`

Capability:
- `data-protection.hard-delete`

Structural dependency closure:
- direct typed FK: `outsourced.outsourced_supply_batches.outsourced_vendor_id -> party.outsourced_vendors.id`
- Restrict delete behavior
- any existing Outsourced Supply Batch blocks Hard Delete, including ACTIVE, CLOSED, or soft-deleted Batch state
- no cascade, detach, or history rewrite

Hard Delete semantics:
- lock Vendor and require exact expected row version
- replay persistent CommandId before target lookup so committed success remains replayable after physical deletion
- physical deletion only when dependency closure is empty
- successful same-transaction `HARD_DELETE` Audit subject `party.outsourced-vendor`
- no Outbox message
- dependency/stale/not-found failure rolls back newly acquired CommandExecution and leaves no Hard Delete Audit residue

Business Fact boundary:
- no change to `ConfirmOutsourcedSupplyDetail`, Batch Close, Finance, or Inventory behavior
- `OUT-003` remains unresolved
- Outsourced Supply Batch Reopen remains DEFERRED
- Hard Delete does not decide late-detail behavior

Validation:
- implementation SHA `c03e4433d20c3900a466054512fcf506034d9edd`
- local hard gates 321/321 PASS
- exact validation branch `p6-v8-outsourced-vendor-hard-delete-validation`
- typed GitHub workflow-status query was unavailable during publication; runner-local immutable Worker evidence proves the exact SHA and final successful `build-test` completion on `YowThi-ERP-V2`
- workflow definition requires `self-hosted` + `yowthi-erp-v2`

No relation/schema/model snapshot/migration change.

## 21. Farmer Hard Delete — V8-C18 COMPLETE

Authoritative C18 supplement:
- `docs/27-farmer-hard-delete-control-v0.1.md`

Command:
- `HardDeleteFarmer`

Endpoint:
- `POST /api/v1/data-protection/farmers/{farmerId}/hard-delete`

OperationId:
- `DataProtection_HardDeleteFarmer`

Capability:
- `data-protection.hard-delete`

Structural dependency closure:
- `procurement.procurement_entries.farmer_id -> party.farmers.id`
- `finance.payables.farmer_id -> party.farmers.id`
- both direct typed FKs use Restrict delete behavior
- any existing Procurement Entry or Finance Payable blocks Hard Delete
- no payment-state, outstanding-state, Procurement Batch-state, lifecycle-state, or soft-delete interpretation is needed
- no cascade, detach, or history rewrite

Hard Delete semantics:
- lock Farmer and require exact expected row version
- replay persistent CommandId before target lookup so committed success remains replayable after physical deletion
- physical deletion only when both typed dependency classes are empty
- successful same-transaction `HARD_DELETE` Audit subject `party.farmer`
- no Outbox message
- dependency/stale/not-found failure rolls back newly acquired CommandExecution and leaves no Hard Delete Audit residue

Business Fact boundary:
- no change to `ConfirmProcurementEntry`, Procurement Batch Close/Reopen, Finance settlement/adjustment, Farmer lifecycle, or Inventory behavior
- no payment state or Batch state is inferred from Hard Delete
- `OUT-003` remains unresolved
- Outsourced Supply Batch Reopen remains DEFERRED

Validation:
- implementation SHA `0871bd006dfe1fa48db0b305a043277e09debfa2`
- local hard gates 328/328 PASS
- exact validation branch `p6-v8-farmer-hard-delete-validation`
- API Contract 110/110; PostgreSQL Integration 123/123
- typed GitHub workflow-status query was unavailable during publication; runner-local immutable Worker evidence proves the exact SHA and final successful `build-test` completion on `YowThi-ERP-V2`
- workflow definition requires `self-hosted` + `yowthi-erp-v2`
- Worker log `C:\actions-runner\actions-runner\_diag\Worker_20260905-081554-utc.log`
- no GitHub run ID is asserted for the typed-query incident

No relation/schema/model snapshot/migration change.

## 22. AuthN/AuthZ interpretation



Capability policies are technical ERP Control / security identifiers, not Business Rules.

Current examples include:
- `sales.confirm`
- `sales.correct-allocation`
- `sales-handling.packaging-item.lifecycle`
- `product.sales-product-group.lifecycle`
- `infrastructure.container.lifecycle`
- `infrastructure.warehouse.lifecycle`
- `finance.pay`
- `finance.correct`
- `inventory.adjust`
- `procurement.batch.lifecycle`
- `party.supplier.lifecycle`
- `party.customer.lifecycle`
- `party.outsourced-vendor.lifecycle`
- `party.farmer.lifecycle`
- `party.employee.lifecycle`
- `data-protection.hard-delete`

Capability grants remain deployment-configured by persistent Account UUID.

`data-protection.hard-delete` remains highest authority and must not be reused for ordinary lifecycle/data correction.

## 23. React/UI baseline

One React application:
- `src/YowThi.Erp.Web`

Presentation experiences:
- Desktop
- Tablet
- Mobile

Implemented operational routes now include:
- `/procurement/entries/new`
- `/outsourced/supply-details/new`
- `/processing/executions/new`
- `/sales`
- `/sales-handling`
- `/labor`
- `/finance`
- `/inventory`
- `/party`
- `/infrastructure`
- `/product`
- `/data-protection`

P7 is formally complete for the current v0.1 route set at `main@cae5a5c0bf2f1cec7bf0ffa96dde7cbafbbe4ced`.

The accepted presentation baseline is Nature Green across Desktop / Tablet / Mobile. Browser visual acceptance covers Home plus all 12 operational modules with 40 / 40 PASS, no horizontal viewport overflow, and current static zh-TW / th-TH interface-language mixing checks passing. The presentation hard gate is part of the self-hosted validation workflow. See `docs/30-p7-presentation-closure-checkpoint-v0.1.md`.

## 24. P6 / V8 closure state

Do not reopen business-mode questions for operations that are merely ERP maintenance/control.

`OUT-003` is different: it governs the real Business Fact behavior of late Outsourced Supply Detail after Batch Close. Because reopening the Batch would currently change whether that Business Fact command is permitted, Outsourced Supply Batch Reopen is **DEFERRED**, not an eligible ordinary-control shortcut.

Current Party lifecycle targets implemented in the model are covered: Supplier, Customer, Outsourced Vendor, Farmer, and Employee.

Current low-risk non-Party lifecycle targets completed:
- Sales Packaging Item — C13
- Sales Product Group — C14
- Container — C15
- Warehouse — C16

Product lifecycle candidates that must **not** be auto-implemented:
- `ProcurementProduct`: new Procurement already rejects inactive/deleted Product, but existing ACTIVE Procurement Batch processing does not re-check current Product lifecycle; whether later Product lifecycle control should stop an already-created Batch is TO VERIFY / DEFERRED candidate behavior
- `SalesProduct`: Processing output availability checks current Product state, while an already-entered Sales Detail can reach `ConfirmSales` without re-validating current Product lifecycle; whether later Product lifecycle maintenance should invalidate an already-entered Sales transaction is TO VERIFY / DEFERRED candidate behavior

C15 confirms a low-risk lifecycle pattern for Container because:
- Processing configuration keeps Restrict typed Container FKs while Soft Delete keeps the master row
- completed Processing snapshots tare weight and does not depend on a historical Container FK
- new Processing already enforces current Container active/non-deleted state server-side

C16 confirms a target-only lifecycle pattern for Warehouse because:
- child Storage Location uses a Restrict typed Warehouse FK while Soft Delete keeps the Warehouse row
- Warehouse lifecycle can preserve child Storage Location lifecycle/current state exactly
- no Business Fact command needs modification merely to maintain the Warehouse master row

C17 confirms a target-specific Hard Delete pattern for Outsourced Vendor because:
- the direct dependency closure is explicit and small: `outsourced_supply_batches.outsourced_vendor_id`
- any Batch dependency blocks physical deletion without needing to interpret Batch lifecycle/business behavior
- no Business Fact command, Finance fact, Inventory fact, or `OUT-003` behavior is changed

C18 confirms Farmer Hard Delete as a target-specific structural Data Protection operation: both Procurement Entry and Finance Payable typed Farmer references independently block physical deletion, without interpreting payment state or Procurement Batch state.

C16 dependency closure scan also found candidates that must **not** be auto-implemented:
- `StorageLocation`: explicit Processing location validation and selectors enforce active/non-deleted state, but automatic single-position input inference and current `ConfirmSales` allocation do not uniformly re-check Storage Location lifecycle; DEFERRED candidate
- `ProcessMaterial`: Processing output resolution checks current lifecycle while existing Module input use does not re-check it; DEFERRED candidate
- `ProcessingRoute`: existing Batch execution uses stored Route Version without re-checking parent Route lifecycle; DEFERRED candidate

Deferred / future target-specific scope:
- additional non-Party master lifecycle only where structural/current-use behavior becomes unambiguous
- additional Hard Delete targets only when a real operational Data Protection need is established and dependency closure is complete
- other focused ERP Control targets that do not silently decide unresolved Business Facts

These candidates are not current P6 completion blockers. Do not introduce a generic lifecycle/correction resolver to accelerate this scope.

P6/V8 is **COMPLETE**. Any future control target reopens only its own target-specific scope; it does not retroactively make the v0.1 P6 skeleton incomplete.

## 25. Validation / cost governance

Routine validation uses only the Windows self-hosted runner.

Required labels:
- `self-hosted`
- `yowthi-erp-v2`

Formal main advances only after exact validation-branch SHA success. Normal evidence uses the typed workflow/job check including `eligibleForMainFastForward=true`; if that read-only GitHub query is unavailable, promotion requires independently proven runner-local evidence for the exact SHA, intended workflow/ref, configured self-hosted runner identity/required workflow labels, and final successful job completion.

Use ff-only promotion and non-force push.

Do not require routine GitHub-hosted runners, paid/larger runners, Codespaces, or unconfirmed metered services.

Formal/Bootstrap operational split:
- Formal r20 is the primary formal Repository mutation/validation channel
- Bootstrap r2 is an independent recovery/read-back channel
- both currently resolve to the same healthy Agent runtime/tool catalog but through separate formal/bootstrap tunnel profiles
- when a Formal tunnel call is ambiguous, use Bootstrap read-back before assuming whether a mutation occurred

## 26. Recovery

```text
main@cae5a5c0bf2f1cec7bf0ffa96dde7cbafbbe4ced
-> P5 PostgreSQL 18 persistence acceptance COMPLETE
-> P6 V1-V7 COMPLETE
-> V8-C1 Supplier Hard Delete COMPLETE
-> V8-C2 Customer Hard Delete COMPLETE
-> V8-C3 Sales Allocation Correction COMPLETE
-> ERP Registration / Data Control boundary COMPLETE
-> V8-C4 Supplier Soft Delete / Restore COMPLETE
-> V8-C5 Customer Soft Delete / Restore COMPLETE
-> V8-C6 CorrectPaymentAmount COMPLETE
-> V8-C7 CorrectReceiptAmount COMPLETE
-> V8-C8 CorrectPayableAdjustment COMPLETE
-> V8-C9 ReopenProcurementBatch COMPLETE
-> V8-C10 Outsourced Vendor Soft Delete / Restore COMPLETE
-> V8-C11 Farmer Soft Delete / Restore COMPLETE
-> V8-C12 Employee Soft Delete / Restore COMPLETE
-> C12 also closes inactive/soft-deleted Employee current-use guards for Sales Packaging and Daily Wage
-> V8-C13 Sales Packaging Item Soft Delete / Restore COMPLETE
-> existing RecordSalesPackagingWork current-use guard remains authoritative for inactive/soft-deleted packaging items
-> V8-C14 Sales Product Group Soft Delete / Restore COMPLETE
-> Group lifecycle preserves child Sales Product FK/current lifecycle state; no cascade and no Business Fact command changes
-> V8-C15 Container Soft Delete / Restore COMPLETE
-> Route Input / Process Material Container FKs remain intact; no cascade
-> completed Processing tare snapshot remains immutable after Container lifecycle change
-> existing ConfirmProcessingExecution Container current-use guard rejects inactive/soft-deleted Container for new use
-> V8-C16 Warehouse Soft Delete / Restore COMPLETE
-> child Storage Location Warehouse FK/current lifecycle state remains intact; no cascade
-> V8-C17 Outsourced Vendor Hard Delete COMPLETE
-> any existing Outsourced Supply Batch typed dependency blocks physical Vendor deletion; no cascade
-> local C17 hard gates 321/321 PASS
-> exact C17 runner-local self-hosted Worker evidence: build-test Succeeded for c03e4433d20c3900a466054512fcf506034d9edd
-> typed GitHub workflow-status query incident means no run ID is asserted for C17
-> C17 ff-only main promotion + non-force push/read-back COMPLETE
-> V8-C18 Farmer Hard Delete COMPLETE
-> Procurement Entry + Finance Payable typed Farmer dependencies each block physical deletion; no cascade
-> local C18 hard gates 328/328 PASS
-> exact C18 runner-local self-hosted Worker evidence: build-test Succeeded for 0871bd006dfe1fa48db0b305a043277e09debfa2
-> typed GitHub workflow-status query incident means no run ID is asserted for C18
-> C18 ff-only main promotion + non-force push/read-back COMPLETE
-> P6/V8 closure hard gate confirms current Gap Register CONTROL items are satisfied
-> remaining lifecycle / Hard Delete / Batch-control candidates are DEFERRED / future target-specific scope
-> P6 / V8 COMPLETE
-> Formal r20 is primary; Bootstrap r2 remains independent recovery/read-back channel
-> current docs checkpoint branch: p7-presentation-closure-checkpoint-validation
-> authoritative C18 supplement: docs/27-farmer-hard-delete-control-v0.1.md
-> authoritative P6/V8 closure supplement: docs/28-p6-v8-closure-checkpoint-v0.1.md
-> authoritative P7 system-skeleton closure supplement: docs/29-p7-system-skeleton-closure-checkpoint-v0.1.md
-> authoritative P7 presentation closure supplement: docs/30-p7-presentation-closure-checkpoint-v0.1.md
-> StorageLocation, ProcessMaterial, and ProcessingRoute lifecycle remain DEFERRED candidates due unresolved current-use consistency
-> ProcurementProduct and SalesProduct lifecycle remain TO VERIFY / DEFERRED candidates where current Business Fact behavior is ambiguous
-> Farmer Hard Delete dependency closure is COMPLETE for Procurement Entry + Finance Payable typed dependencies
-> OUT-003 still unresolved; Outsourced Supply Batch Reopen DEFERRED
-> P6 / V8 COMPLETE; remaining candidate ERP Control scope explicitly deferred/future target-specific
-> P7 system skeleton S0–S12 COMPLETE / 12 of 12 modules operational
-> Nature Green presentation + Desktop/Tablet/Mobile adaptive UX COMPLETE for current v0.1 route set
-> current static zh-TW/th-TH presentation acceptance COMPLETE; browser visual acceptance 40/40 PASS
-> P7 COMPLETE
```

