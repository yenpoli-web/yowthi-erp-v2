# Sales Product Group Lifecycle Control v0.1 — YowThi ERP V2

Status: **IMPLEMENTED / V8-C14 COMPLETE**

This document is the implementation supplement for Sales Product Group Soft Delete / Restore. It records the target-specific ERP Control semantics implemented in C14 without redefining Sales, Processing, or Product business behavior and without inventing a YowThi Business Rule.

## 1. Classification

`SoftDeleteSalesProductGroup` and `RestoreSalesProductGroup` are **ERP Control Commands**.

They maintain lifecycle state of the Sales Product Group master. They do not register a real business event/result and therefore do not require a new Business Rule merely to permit the lifecycle mutation.

Authoritative classification:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

C14 does not change Sales Product lifecycle state and does not change any Business Fact command.

## 2. Commands and routes

Commands:
- `SoftDeleteSalesProductGroup`
- `RestoreSalesProductGroup`

Routes:
- `POST /api/v1/product/sales-product-groups/{salesProductGroupId}/soft-delete`
- `POST /api/v1/product/sales-product-groups/{salesProductGroupId}/restore`

OperationIds:
- `Product_SoftDeleteSalesProductGroup`
- `Product_RestoreSalesProductGroup`

Capability:
- `product.sales-product-group.lifecycle`

Request:
- route Sales Product Group ID
- `expectedRowVersion`
- required `Idempotency-Key`

Canonical command identity includes:
- command type
- route Sales Product Group ID
- expected row version

## 3. Structural dependency boundary

Direct typed dependency:
- `product.sales_products.sales_product_group_id -> product.sales_product_groups.id`
- delete behavior: Restrict

Soft Delete preserves the `sales_product_groups` row, so existing Sales Product rows keep their typed FK and category identity.

An existing Sales Product therefore does **not** block Sales Product Group Soft Delete.

C14 does not:
- modify `sales_products.sales_product_group_id`
- modify Sales Product `active`
- modify Sales Product `deleted_at` / `deleted_by_account_id`
- modify Sales Product `row_version`
- cascade Soft Delete into Sales Products
- rewrite Sales Details, Processing configuration, inventory, or historical business records

## 4. Soft Delete semantics

Soft Delete:
- keeps the `product.sales_product_groups` row
- sets `deleted_at`
- sets `deleted_by_account_id` from the authenticated actor
- increments Group `row_version`
- preserves the existing Group `active` value
- does not cascade to Sales Products
- creates no Outbox message for the local master lifecycle transition

Sales Products that reference the Group remain exactly as they were before the Group lifecycle operation.

## 5. Restore semantics

Restore:
- clears Group `deleted_at`
- clears Group `deleted_by_account_id`
- increments Group `row_version`
- preserves the existing Group `active` value
- does not modify any child Sales Product row

Restore does **not** automatically reactivate an inactive Group.

Consequences:
- active Group -> Soft Delete -> Restore -> remains active
- inactive Group -> Soft Delete -> Restore -> remains inactive
- child Sales Product lifecycle/current state is not coupled to either transition

## 6. Business Fact boundary

C14 deliberately does not change:
- `ConfirmSales`
- Sales allocation behavior
- Processing execution behavior
- Processing module output behavior
- Sales Product lifecycle behavior
- Procurement Product lifecycle behavior

The Group is a master/category control target. C14 does not infer that Group Soft Delete should disable existing Sales Products or invalidate existing Business Facts.

No Business Rule is added to make a Sales Product unavailable merely because its Group is soft-deleted.

## 7. Authorization boundary

`product.sales-product-group.lifecycle` is target-specific technical authorization.

It is deliberately separate from:
- `sales.confirm`
- `sales.correct-allocation`
- `sales-handling.packaging-item.lifecycle`
- `data-protection.hard-delete`

Permission to confirm Sales does not imply permission to change Product Group lifecycle state.

Permission to change Product Group lifecycle state does not imply permission to mutate Sales Product lifecycle or perform Hard Delete.

Capability grants remain deployment-configured by persistent ERP Account UUID under `docs/15-authn-authz-implementation-architecture-v0.1.md`.

## 8. Idempotency and concurrency

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
- Group not found -> `product.sales-product-group-not-found`
- Soft Delete already deleted -> `product.sales-product-group-already-deleted`
- Restore current/non-deleted -> `product.sales-product-group-not-deleted`
- stale expected row version -> `concurrency.stale-row-version`

Invalid command identity/version input uses:
- `product.sales-product-group-lifecycle-invalid`

Failed lifecycle/concurrency attempts roll back newly acquired CommandExecution and do not leave lifecycle Audit residue.

## 9. Audit / Outbox

Lifecycle Audit Event:
- `event_kind = DATA_LIFECYCLE`
- command type = target lifecycle command
- actor = authenticated server-resolved Account

Audit subject:
- `subject_kind = product.sales-product-group`
- Soft Delete `change_kind = SOFT_DELETE`
- Restore `change_kind = RESTORE`
- before/after Group row versions retained

No Outbox message is emitted merely for this local master lifecycle transition.

## 10. Persistence impact

V8-C14 introduces no:
- relation
- schema
- EF model mapping change
- model snapshot change
- migration

`InitialV01` remains unchanged.

No Generic Repository, generic CRUD, or generic lifecycle resolver is introduced.

## 11. Acceptance evidence

Implementation commit:
- `30b724acea1ac252707800b2a6ebd13a79afde7b`
- `feat: add sales product group lifecycle`

Validation branch:
- `p6-v8-sales-product-group-lifecycle-validation`

Local hard gates:
- Domain: 29/29 PASS
- Architecture: 66/66 PASS
- API Contract: 98/98 PASS
- PostgreSQL Integration: 109/109 PASS
- total: **302/302 PASS**

Final Release solution build:
- 0 errors
- only existing solution custom-output `NETSDK1194`

PostgreSQL acceptance endpoint during validation:
- PostgreSQL 18.6
- `yowthi_dev`
- `isInRecovery=false`

Self-hosted validation:
- workflow: `dotnet.yml` / `dotnet-self-hosted`
- run: `33935411819`
- exact SHA: `30b724acea1ac252707800b2a6ebd13a79afde7b`
- runner: `YowThi-ERP-V2`
- labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`

Promotion:
- ff-only local main
- non-force push
- fetch/read-back clean
- Formal r16 and Bootstrap r2 read-back agree

Formal implementation baseline after promotion:
- `main = origin/main = 30b724acea1ac252707800b2a6ebd13a79afde7b`

## 12. Deferred lifecycle candidates discovered during C14 scan

The C14 read-only structural scan also examined lifecycle suitability for Product masters.

`ProcurementProduct` is **not automatically eligible** for the same treatment:
- new Procurement selection/confirmation already requires active and non-deleted Product state
- existing active Procurement Batches can still be presented for Processing without re-checking current Product lifecycle state
- changing that behavior would decide whether a Product lifecycle change should stop processing of an already-created Batch
- that operating fact is not inferred by C14

`SalesProduct` is also **not automatically eligible**:
- Processing output option availability does examine current Product state
- an already-created Sales Detail and available inventory can reach `ConfirmSales` without re-validating current Sales Product lifecycle state
- changing that behavior would decide whether later Product lifecycle maintenance should invalidate an already-entered Sales transaction
- that operating fact is not inferred by C14

Therefore these remain future structural/Business Fact verification candidates rather than automatic C15 work.

## 13. Business Rule boundary

V8-C14 does not introduce, resolve, or reinterpret a YowThi Business Rule.

Sales Product Group lifecycle is ERP Data Control over a grouping/master record. The typed Sales Product FK remains intact and child Product state is intentionally unchanged.

Existing unresolved Business Rule gaps remain unchanged. In particular, C14 does not affect `OUT-003` or authorize Outsourced Supply Batch Reopen.
