# Sales Packaging Item Lifecycle Control v0.1 — YowThi ERP V2

Status: **IMPLEMENTED / V8-C13 COMPLETE**

This document is the implementation supplement for Sales Packaging Item Soft Delete / Restore. It records the target-specific ERP Control semantics implemented in C13 without redefining Sales Packaging business behavior or inventing a YowThi Business Rule.

## 1. Classification

`SoftDeleteSalesPackagingItem` and `RestoreSalesPackagingItem` are **ERP Control Commands**.

They maintain the lifecycle state of the Sales Packaging Item master. They do not register a new business event/result and therefore do not require a new Business Rule merely to permit the lifecycle mutation.

Authoritative classification:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

C13 does not change the already-established `RecordSalesPackagingWork` Business Fact behavior. That command already rejects a packaging item when:
- `active = false`, or
- `deleted_at IS NOT NULL`

with `sales-handling.item-inactive`.

That current-use check remains unchanged.

## 2. Commands and routes

Commands:
- `SoftDeleteSalesPackagingItem`
- `RestoreSalesPackagingItem`

Routes:
- `POST /api/v1/sales-handling/packaging-items/{salesPackagingItemId}/soft-delete`
- `POST /api/v1/sales-handling/packaging-items/{salesPackagingItemId}/restore`

OperationIds:
- `SalesHandling_SoftDeletePackagingItem`
- `SalesHandling_RestorePackagingItem`

Capability:
- `sales-handling.packaging-item.lifecycle`

Request:
- route Sales Packaging Item ID
- `expectedRowVersion`
- required `Idempotency-Key`

Canonical command identity includes:
- command type
- route Sales Packaging Item ID
- expected row version

## 3. Structural dependency boundary

Historical typed dependency:
- `sales_handling.sales_packaging_work_records.sales_packaging_item_id -> sales_handling.sales_packaging_items.id`
- delete behavior: Restrict

Soft Delete preserves the `sales_packaging_items` row, so existing Sales Packaging Work Records keep their typed FK and historical traceability.

An existing work record therefore does **not** by itself block Sales Packaging Item Soft Delete.

C13 does not:
- rewrite historical work records
- detach item references
- cascade-delete historical work
- replace historical item identity

## 4. Soft Delete semantics

Soft Delete:
- keeps the `sales_handling.sales_packaging_items` row
- sets `deleted_at`
- sets `deleted_by_account_id` from the authenticated actor
- increments `row_version`
- preserves the existing `active` value
- creates no cascade deletion
- creates no Outbox message for the local master lifecycle transition

After Soft Delete, `RecordSalesPackagingWork` rejects the item through its existing current-use check.

Historical Sales Packaging Work Records remain intact.

## 5. Restore semantics

Restore:
- clears `deleted_at`
- clears `deleted_by_account_id`
- increments `row_version`
- preserves the existing `active` value

Restore does **not** automatically reactivate an inactive item.

Consequences:
- active item -> Soft Delete -> Restore -> current-use eligible again
- inactive item -> Soft Delete -> Restore -> remains inactive and remains rejected for new packaging-work registration

## 6. Authorization boundary

`sales-handling.packaging-item.lifecycle` is target-specific technical authorization.

It is deliberately separate from:
- `sales-handling.work-record.record`
- Party lifecycle capabilities
- `data-protection.hard-delete`

Permission to record Sales Packaging Work does not imply permission to change Sales Packaging Item lifecycle state.

Permission to change packaging-item lifecycle state does not imply Hard Delete.

Capability grants remain deployment-configured by persistent ERP Account UUID under `docs/15-authn-authz-implementation-architecture-v0.1.md`.

## 7. Idempotency and concurrency

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
- item not found -> `sales-handling.packaging-item-not-found`
- Soft Delete already deleted -> `sales-handling.packaging-item-already-deleted`
- Restore current/non-deleted -> `sales-handling.packaging-item-not-deleted`
- stale expected row version -> `concurrency.stale-row-version`

Invalid command identity/version input uses:
- `sales-handling.packaging-item-lifecycle-invalid`

Failed lifecycle/concurrency attempts roll back newly acquired CommandExecution and do not leave lifecycle Audit residue.

## 8. Audit / Outbox

Lifecycle Audit Event:
- `event_kind = DATA_LIFECYCLE`
- command type = target lifecycle command
- actor = authenticated server-resolved Account

Audit subject:
- `subject_kind = sales-handling.packaging-item`
- Soft Delete `change_kind = SOFT_DELETE`
- Restore `change_kind = RESTORE`
- before/after row versions retained

No Outbox message is emitted merely for this local master lifecycle transition.

The existing `RecordSalesPackagingWork` command retains its own normal Business Fact Audit/Outbox semantics when it succeeds. When it rejects a soft-deleted/inactive item, the failed registration leaves no committed:
- CommandExecution
- Audit
- Outbox

residue.

## 9. Persistence impact

V8-C13 introduces no:
- relation
- schema
- EF model mapping change
- model snapshot change
- migration

`InitialV01` remains unchanged.

No Generic Repository, generic CRUD, or generic lifecycle resolver is introduced.

## 10. Acceptance evidence

Implementation commit:
- `75d2a24cdf1a6ffac883151283c2702309b22b32`
- `feat: add sales packaging item lifecycle`

Validation branch:
- `p6-v8-sales-packaging-item-lifecycle-validation`

Local hard gates:
- Domain: 29/29 PASS
- Architecture: 66/66 PASS
- API Contract: 95/95 PASS
- PostgreSQL Integration: 106/106 PASS
- total: **296/296 PASS**

Final Release solution build:
- 0 errors
- only existing solution custom-output `NETSDK1194`

PostgreSQL acceptance endpoint during validation:
- PostgreSQL 18.6
- `yowthi_dev`
- `isInRecovery=false`

Self-hosted validation:
- workflow: `dotnet.yml` / `dotnet-self-hosted`
- run: `33932510549`
- exact SHA: `75d2a24cdf1a6ffac883151283c2702309b22b32`
- runner: `YowThi-ERP-V2`
- labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`

Promotion:
- ff-only local main
- non-force push
- fetch/read-back clean

Formal implementation baseline after promotion:
- `main = origin/main = 75d2a24cdf1a6ffac883151283c2702309b22b32`

## 11. Business Rule boundary

V8-C13 does not introduce, resolve, or reinterpret a YowThi Business Rule.

Sales Packaging Item lifecycle is ERP Data Control over a master record. Historical packaging-work registration remains intact through the typed Item FK. Current-use rejection of an inactive/soft-deleted item already existed before C13 and continues to govern only new registration.

Existing unresolved Business Rule gaps remain unchanged. In particular, C13 does not affect `OUT-003` or authorize Outsourced Supply Batch Reopen.