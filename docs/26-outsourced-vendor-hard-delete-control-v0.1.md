# Outsourced Vendor Hard Delete Control v0.1 — YowThi ERP V2

Status: **IMPLEMENTED / V8-C17 COMPLETE**

This document is the implementation supplement for Outsourced Vendor Hard Delete. It records the target-specific Data Protection / ERP Control semantics implemented in C17 without redefining Outsourced Supply, Finance, Inventory, or late-detail Business Fact behavior and without inventing a YowThi Business Rule.

## 1. Classification

`HardDeleteOutsourcedVendor` is an **ERP Control / Data Protection Command**.

It physically removes an Outsourced Vendor master only when structural dependency closure is empty. It does not register a real business event/result and does not require a new Business Rule merely to permit the physical deletion.

Authoritative classification:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

Hard Delete remains higher authority than ordinary lifecycle control and is deliberately target-specific.

## 2. Command and route

Command:
- `HardDeleteOutsourcedVendor`

Route:
- `POST /api/v1/data-protection/outsourced-vendors/{outsourcedVendorId}/hard-delete`

OperationId:
- `DataProtection_HardDeleteOutsourcedVendor`

Capability:
- `data-protection.hard-delete`

Request identity:
- route Outsourced Vendor ID
- `expectedRowVersion`
- required `Idempotency-Key`

Canonical command identity includes:
- command type
- route Outsourced Vendor ID
- expected row version

## 3. Structural dependency closure

Repository-wide reference scan and EF mapping review identify the direct typed dependency:
- `outsourced.outsourced_supply_batches.outsourced_vendor_id -> party.outsourced_vendors.id`
- delete behavior is Restrict

C17 therefore permits physical deletion only when **no Outsourced Supply Batch row exists for the Vendor**.

Dependency assessment intentionally has no lifecycle/deletion filter:
- ACTIVE Batch blocks
- CLOSED Batch blocks
- soft-deleted Batch still blocks

The presence of any Batch preserves the Vendor row because the typed FK/history dependency exists.

C17 does not:
- delete or detach Outsourced Supply Batches
- cascade into Outsourced Supply Details
- rewrite Outsourced history
- rewrite Finance obligations/payments
- rewrite Inventory history/projections
- infer any late-detail behavior

## 4. Hard Delete semantics

Successful Hard Delete:
- locks the target Vendor row
- validates exact expected `row_version`
- verifies dependency closure is empty
- physically deletes `party.outsourced_vendors`
- records same-transaction `HARD_DELETE` Audit
- marks the persistent CommandExecution `SUCCEEDED`
- emits no Outbox message

The command does not require the Vendor to be active or non-deleted. Hard Delete is a separate Data Protection authority from ordinary lifecycle control; structural dependency closure and concurrency are the decisive safety gates.

## 5. Dependency-block semantics

If any `outsourced_supply_batches.outsourced_vendor_id` references the Vendor:
- command fails with conflict
- error code: `data-protection.outsourced-vendor-dependency-blocked`
- Vendor remains present
- Batch remains present and unchanged
- no cascade occurs
- newly acquired CommandExecution is rolled back
- no Hard Delete Audit remains
- no Outbox message is created

This is structural dependency safety, not a Business Rule about whether Outsourced Supply operations may occur.

## 6. Idempotency and concurrency

Persistent CommandId acquisition occurs before target lookup, preserving replay after the physical target row has been removed.

Same:
- actor
- command type
- canonical request hash

replays the committed successful result even though the Vendor no longer exists.

Changed actor/type/hash under the same CommandId returns:
- `409`
- `idempotency.key-reused`

Other failures:
- Vendor not found -> `data-protection.outsourced-vendor-not-found`
- dependency exists -> `data-protection.outsourced-vendor-dependency-blocked`
- stale expected row version -> `concurrency.stale-row-version`
- invalid command input -> `data-protection.hard-delete-invalid`

Dependency/concurrency/not-found failures roll back newly acquired CommandExecution state.

## 7. Audit / Outbox

Successful Hard Delete Audit Event:
- `event_kind = HARD_DELETE`
- command type = `HardDeleteOutsourcedVendor`
- actor = authenticated server-resolved Account

Audit subject:
- `subject_kind = party.outsourced-vendor`
- `change_kind = HARD_DELETE`
- `before_row_version` retained
- `after_row_version = NULL`

No Outbox message is emitted for this Data Protection master deletion.

## 8. Authorization boundary

`data-protection.hard-delete` is the highest-authority deletion capability in the current API baseline.

It remains separate from:
- `party.outsourced-vendor.lifecycle`
- Outsourced Supply confirmation/close capabilities
- Finance capabilities
- Inventory capabilities

Ordinary Outsourced Vendor lifecycle permission does not imply Hard Delete permission.

No generic `/data-protection/entities/{type}/{id}` endpoint or generic hard-delete resolver is introduced.

## 9. Business Fact boundary

C17 does not modify:
- `ConfirmOutsourcedSupplyDetail`
- `CloseOutsourcedSupplyBatch`
- Finance settlement/adjustment behavior
- Inventory behavior
- Outsourced Vendor Soft Delete / Restore

`OUT-003` remains unresolved.

In particular, C17 does **not** authorize Outsourced Supply Batch Reopen and does not decide whether late Outsourced Supply Detail after Batch Close is valid. Any existing Batch blocks Vendor Hard Delete regardless of Batch lifecycle state, so C17 remains independent of that unresolved Business Fact.

## 10. Persistence impact

V8-C17 introduces no:
- relation
- schema
- EF model mapping change
- model snapshot change
- migration

`InitialV01` remains unchanged.

No Generic Repository, generic CRUD, or generic hard-delete architecture is introduced.

## 11. Acceptance evidence

Implementation commit:
- `c03e4433d20c3900a466054512fcf506034d9edd`
- `feat: add outsourced vendor hard delete`

Validation branch:
- `p6-v8-outsourced-vendor-hard-delete-validation`

Local hard gates:
- Domain: 29/29 PASS
- Architecture: 66/66 PASS
- API Contract: 107/107 PASS
- PostgreSQL Integration: 119/119 PASS
- total: **321/321 PASS**

Final Release solution build:
- 0 errors
- only existing solution custom-output `NETSDK1194`

PostgreSQL acceptance endpoint during validation:
- PostgreSQL 18.6
- `yowthi_dev`
- `isInRecovery=false`

## 12. Self-hosted validation evidence

The normal Formal r17 / Bootstrap r2 typed GitHub workflow-status queries returned invocation errors for this validation publication. Because validation must not be assumed from branch publication alone, C17 used direct read-only evidence from the configured Windows self-hosted runner.

Runner evidence:
- runner registration file `.runner` identifies `agentName = YowThi-ERP-V2`
- repository = `yenpoli-web/yowthi-erp-v2`
- workflow = `.github/workflows/dotnet.yml` / `dotnet-self-hosted`
- workflow requires `runs-on: [self-hosted, yowthi-erp-v2]`
- job = `build-test`
- Worker log = `C:\actions-runner\actions-runner\_diag\Worker_20260905-065811-utc.log`
- exact commit SHA appears repeatedly as `c03e4433d20c3900a466054512fcf506034d9edd`
- workflow ref identifies `refs/heads/p6-v8-outsourced-vendor-hard-delete-validation`
- all recorded job steps completed as `succeeded`
- final worker evidence: `complete_job` result = `succeeded`
- final `Job result after all job steps finish: Succeeded`

The typed GitHub query did not provide a run ID during this incident, so no run ID is asserted here.

This runner-local evidence closes the same required facts: exact validation SHA, intended workflow, intended self-hosted runner, required labels from the workflow definition, and successful job completion.

## 13. Promotion

Implementation promotion:
- ff-only local `main`
- non-force push
- fetch/read-back clean
- Formal r17 primary formal channel
- Bootstrap r2 independent recovery/read-back channel

Formal implementation baseline after promotion:
- `main = origin/main = c03e4433d20c3900a466054512fcf506034d9edd`

## 14. C17 dependency-scan context

C17 compared additional Hard Delete candidates after lifecycle candidates with unresolved current-use semantics were deferred.

Farmer was not selected because structural closure includes at least:
- `procurement.procurement_entries.farmer_id`
- `finance.payables.farmer_id`

Outsourced Vendor was selected because its direct dependency closure is smaller and explicit:
- `outsourced.outsourced_supply_batches.outsourced_vendor_id`

Existing deferred lifecycle candidates remain unchanged:
- `StorageLocation` -> DEFERRED
- `ProcessMaterial` -> DEFERRED
- `ProcessingRoute` -> DEFERRED
- `ProcurementProduct` -> TO VERIFY / DEFERRED
- `SalesProduct` -> TO VERIFY / DEFERRED

## 15. Business Rule boundary

V8-C17 does not introduce, resolve, or reinterpret a YowThi Business Rule.

The only new decision is target-specific structural Data Protection behavior: an Outsourced Vendor can be physically removed only when no typed Outsourced Supply Batch dependency remains.

Existing unresolved Business Rule gaps remain unchanged. In particular:
- `OUT-003` remains unresolved
- Outsourced Supply Batch Reopen remains DEFERRED
- no late-detail behavior is inferred from Hard Delete