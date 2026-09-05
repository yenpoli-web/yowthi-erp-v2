# Warehouse Lifecycle Control v0.1 — YowThi ERP V2

Status: **IMPLEMENTED / V8-C16 COMPLETE**

This document is the implementation supplement for Warehouse Soft Delete / Restore. It records the target-specific ERP Control semantics implemented in C16 without redefining Inventory, Sales, Processing, or Storage Location business behavior and without inventing a YowThi Business Rule.

## 1. Classification

`SoftDeleteWarehouse` and `RestoreWarehouse` are **ERP Control Commands**.

They maintain lifecycle state of the Warehouse master. They do not register a real business event/result and therefore do not require a new Business Rule merely to permit the lifecycle mutation.

Authoritative classification:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

C16 deliberately treats Warehouse lifecycle as target-only master-data control. It does not infer that Warehouse lifecycle must cascade into child Storage Locations or modify any Business Fact command.

## 2. Commands and routes

Commands:
- `SoftDeleteWarehouse`
- `RestoreWarehouse`

Routes:
- `POST /api/v1/infrastructure/warehouses/{warehouseId}/soft-delete`
- `POST /api/v1/infrastructure/warehouses/{warehouseId}/restore`

OperationIds:
- `Infrastructure_SoftDeleteWarehouse`
- `Infrastructure_RestoreWarehouse`

Capability:
- `infrastructure.warehouse.lifecycle`

Request:
- route Warehouse ID
- `expectedRowVersion`
- required `Idempotency-Key`

Canonical command identity includes:
- command type
- route Warehouse ID
- expected row version

## 3. Structural dependency boundary

Direct child dependency:
- `infrastructure.storage_locations.warehouse_id -> infrastructure.warehouses.id`
- delete behavior is Restrict

Soft Delete preserves the `infrastructure.warehouses` row, so the typed Warehouse FK remains intact.

C16 does not:
- delete or detach child Storage Locations
- rewrite `storage_locations.warehouse_id`
- modify child Storage Location `active`
- modify child Storage Location deletion markers
- modify child Storage Location `row_version`
- cascade lifecycle state into Inventory, Product defaults, Processing configuration, Sales, or Processing facts

This is a structural ERP Control boundary, not a rule that a child Storage Location must follow the parent Warehouse lifecycle state.

## 4. Child Storage Location preservation

C16 acceptance proves that an existing child Storage Location remains unchanged across Warehouse Soft Delete and Restore.

Preserved child state includes:
- same `warehouse_id`
- same `active`
- same `deleted_at`
- same `deleted_by_account_id`
- same `row_version`

The Warehouse row remains available for typed FK integrity and traceability while soft-deleted.

## 5. Soft Delete semantics

Soft Delete:
- keeps the `infrastructure.warehouses` row
- sets `deleted_at`
- sets `deleted_by_account_id` from the authenticated actor
- increments Warehouse `row_version`
- preserves the existing Warehouse `active` value
- leaves child Storage Location state unchanged
- creates no Outbox message for the local master lifecycle transition

## 6. Restore semantics

Restore:
- clears Warehouse `deleted_at`
- clears Warehouse `deleted_by_account_id`
- increments Warehouse `row_version`
- preserves the existing Warehouse `active` value
- does not modify child Storage Location state

Restore does **not** automatically reactivate an inactive Warehouse.

Consequences:
- active Warehouse -> Soft Delete -> Restore -> remains active
- inactive Warehouse -> Soft Delete -> Restore -> remains inactive

## 7. Business Fact command boundary

C16 does not modify:
- `ConfirmProcurementEntry`
- `ConfirmProcessingExecution`
- `ConfirmSales`
- Inventory Transfer / Adjustment
- Sales Allocation Correction
- any Processing or Sales option selector

Warehouse lifecycle therefore does not silently decide whether an existing Storage Location remains eligible for Inventory/Sales/Processing use.

That question belongs to Storage Location current-use semantics and is not answered by C16.

## 8. Authorization boundary

`infrastructure.warehouse.lifecycle` is target-specific technical authorization.

It is deliberately separate from:
- `infrastructure.container.lifecycle`
- `processing.confirm`
- Inventory capabilities
- Product lifecycle capabilities
- Party lifecycle capabilities
- `data-protection.hard-delete`

Permission to change Warehouse lifecycle state does not imply permission to change Storage Location lifecycle, rewrite Inventory history, perform Processing/Sales operations, or perform Hard Delete.

Capability grants remain deployment-configured by persistent ERP Account UUID under `docs/15-authn-authz-implementation-architecture-v0.1.md`.

## 9. Idempotency and concurrency

Persistent CommandId acquisition/replay occurs before current lifecycle/version validation.

Same:
- actor
- command type
- canonical request hash

replays the committed result.

Changed actor/type/hash under the same CommandId returns:
- `409`
- `idempotency.key-reused`

Lifecycle state/concurrency failures include:
- Warehouse not found -> `infrastructure.warehouse-not-found`
- Soft Delete already deleted -> `infrastructure.warehouse-already-deleted`
- Restore current/non-deleted -> `infrastructure.warehouse-not-deleted`
- stale expected row version -> `concurrency.stale-row-version`

Invalid command identity/version input uses:
- `infrastructure.warehouse-lifecycle-invalid`

Failed lifecycle/concurrency attempts roll back newly acquired CommandExecution and do not leave lifecycle Audit residue.

## 10. Audit / Outbox

Lifecycle Audit Event:
- `event_kind = DATA_LIFECYCLE`
- command type = target lifecycle command
- actor = authenticated server-resolved Account

Audit subject:
- `subject_kind = infrastructure.warehouse`
- Soft Delete `change_kind = SOFT_DELETE`
- Restore `change_kind = RESTORE`
- before/after Warehouse row versions retained

No Outbox message is emitted merely for this local master lifecycle transition.

## 11. Persistence impact

V8-C16 introduces no:
- relation
- schema
- EF model mapping change
- model snapshot change
- migration

`InitialV01` remains unchanged.

No Generic Repository, generic CRUD, or generic lifecycle resolver is introduced.

## 12. Acceptance evidence

Implementation commit:
- `9e9a7570ed581f325ab77e9c94c813525e23813c`
- `feat: add warehouse lifecycle`

Validation branch:
- `p6-v8-warehouse-lifecycle-validation`

Local hard gates:
- Domain: 29/29 PASS
- Architecture: 66/66 PASS
- API Contract: 104/104 PASS
- PostgreSQL Integration: 116/116 PASS
- total: **315/315 PASS**

Final Release solution build:
- 0 errors
- only existing solution custom-output `NETSDK1194`

PostgreSQL acceptance endpoint during validation:
- PostgreSQL 18.6
- `yowthi_dev`
- `isInRecovery=false`

Self-hosted validation:
- workflow: `dotnet.yml` / `dotnet-self-hosted`
- run: `33948173959`
- exact SHA: `9e9a7570ed581f325ab77e9c94c813525e23813c`
- runner: `YowThi-ERP-V2`
- labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`

Promotion:
- ff-only local main
- non-force push
- fetch/read-back clean
- Formal r17 is the primary formal channel
- Bootstrap r2 remains the independent recovery/read-back channel

Formal implementation baseline after promotion:
- `main = origin/main = 9e9a7570ed581f325ab77e9c94c813525e23813c`

## 13. C16 dependency-closure scan

C16 selected Warehouse because its target-only lifecycle can be implemented without changing Business Fact command behavior.

The same scan identified candidates that must **not** be auto-implemented:

`StorageLocation` lifecycle — DEFERRED candidate:
- Processing explicit location validation requires active/non-deleted Storage Location
- Processing selectors filter active/non-deleted locations
- the single-position automatic input-location path can infer a location from Inventory Position without re-validating current Storage Location lifecycle
- `ConfirmSales` current sellable-position allocation does not currently reject positions solely because their Storage Location master became inactive/soft-deleted
- therefore a Storage Location lifecycle slice would change current-use behavior unless these semantics are deliberately resolved

`ProcessMaterial` lifecycle — DEFERRED candidate:
- Processing output resolution rejects inactive/soft-deleted Process Material
- an existing Processing Module can use `InputProcessMaterialId` without the executor re-validating current Process Material lifecycle
- therefore lifecycle maintenance could otherwise decide whether an already-configured module remains executable

`ProcessingRoute` lifecycle — DEFERRED candidate:
- Procurement Batch executes through its stored Processing Route Version
- current Processing execution does not re-check parent Processing Route lifecycle
- therefore parent Route lifecycle cannot be assumed to invalidate an already-created Batch/Route Version without an explicit confirmed design

Existing Product candidates remain unchanged:
- `ProcurementProduct` lifecycle -> TO VERIFY / DEFERRED
- `SalesProduct` lifecycle -> TO VERIFY / DEFERRED

## 14. Business Rule boundary

V8-C16 does not introduce, resolve, or reinterpret a YowThi Business Rule.

Warehouse lifecycle is ERP Data Control over a master record. The child Storage Location FK remains intact and child state is not silently altered.

Existing unresolved Business Rule gaps remain unchanged. In particular:
- `StorageLocation`, `ProcessMaterial`, and `ProcessingRoute` lifecycle remain DEFERRED candidates because current-use behavior is not uniformly resolved
- `ProcurementProduct` and `SalesProduct` lifecycle remain TO VERIFY / DEFERRED candidates
- `OUT-003` remains unresolved and does not authorize Outsourced Supply Batch Reopen
