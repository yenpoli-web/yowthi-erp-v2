# Farmer Hard Delete Control v0.1 — YowThi ERP V2

Status: **IMPLEMENTED / V8-C18 COMPLETE**

This document is the implementation supplement for Farmer Hard Delete. It records the target-specific Data Protection / ERP Control semantics implemented in C18 without redefining Procurement, Finance, payment, Batch lifecycle, or other Business Fact behavior and without inventing a YowThi Business Rule.

## 1. Classification

`HardDeleteFarmer` is an **ERP Control / Data Protection Command**.

It physically removes a Farmer master only when structural dependency closure is empty. It does not register a real business event/result and does not require a new Business Rule merely to permit the physical deletion.

Authoritative classification:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

Hard Delete remains higher authority than ordinary Farmer lifecycle control and is deliberately target-specific.

## 2. Command and route

Command:
- `HardDeleteFarmer`

Route:
- `POST /api/v1/data-protection/farmers/{farmerId}/hard-delete`

OperationId:
- `DataProtection_HardDeleteFarmer`

Capability:
- `data-protection.hard-delete`

Request identity:
- route Farmer ID
- `expectedRowVersion`
- required `Idempotency-Key`

Canonical command identity includes:
- command type
- route Farmer ID
- expected row version

## 3. Structural dependency closure

Repository-wide reference scan and EF mapping review identify two direct typed Restrict dependencies:
- `procurement.procurement_entries.farmer_id -> party.farmers.id`
- `finance.payables.farmer_id -> party.farmers.id`

C18 therefore permits physical deletion only when **neither a Procurement Entry nor a Finance Payable row references the Farmer**.

Dependency assessment intentionally has no lifecycle, payment-state, outstanding-state, Batch-state, or deletion-state interpretation:
- any Procurement Entry reference blocks
- any Finance Payable reference blocks

The existence of either typed FK dependency preserves the Farmer row for referential integrity and historical traceability.

C18 does not:
- delete, detach, or rewrite Procurement Entries
- delete, detach, or rewrite Finance Payables
- cascade into Payments, Adjustments, Outstanding projections, or Procurement Batches
- infer payment completion behavior
- infer Procurement Batch lifecycle behavior
- rewrite historical Business Facts

## 4. Hard Delete semantics

Successful Hard Delete:
- locks the target Farmer row
- validates exact expected `row_version`
- verifies both dependency classes are absent
- physically deletes `party.farmers`
- records same-transaction `HARD_DELETE` Audit
- marks persistent CommandExecution `SUCCEEDED`
- emits no Outbox message

The command does not require the Farmer to be active or non-deleted. Hard Delete is a separate Data Protection authority from ordinary lifecycle control; structural dependency closure and concurrency are the decisive safety gates.

## 5. Dependency-block semantics

If either typed dependency exists:
- command fails with conflict
- error code: `data-protection.farmer-dependency-blocked`
- Farmer remains present
- referenced Procurement Entry / Finance Payable remains present and unchanged
- no cascade occurs
- newly acquired CommandExecution is rolled back
- no Hard Delete Audit remains
- no Outbox message is created

This is structural dependency safety, not a Business Rule about whether Procurement or Finance operations may occur.

## 6. Idempotency and concurrency

Persistent CommandId acquisition occurs before target lookup, preserving replay after the physical Farmer row has been removed.

Same:
- actor
- command type
- canonical request hash

replays the committed successful result even though the Farmer no longer exists.

Changed actor/type/hash under the same CommandId returns:
- `409`
- `idempotency.key-reused`

Other failures:
- Farmer not found -> `data-protection.farmer-not-found`
- dependency exists -> `data-protection.farmer-dependency-blocked`
- stale expected row version -> `concurrency.stale-row-version`
- invalid command input -> `data-protection.hard-delete-invalid`

Dependency/concurrency/not-found failures roll back newly acquired CommandExecution state.

## 7. Audit / Outbox

Successful Hard Delete Audit Event:
- `event_kind = HARD_DELETE`
- command type = `HardDeleteFarmer`
- actor = authenticated server-resolved Account

Audit subject:
- `subject_kind = party.farmer`
- `change_kind = HARD_DELETE`
- `before_row_version` retained
- `after_row_version = NULL`

No Outbox message is emitted for this Data Protection master deletion.

## 8. Authorization boundary

`data-protection.hard-delete` remains the highest-authority deletion capability in the current API baseline.

It remains separate from:
- `party.farmer.lifecycle`
- Procurement confirmation/lifecycle capabilities
- Finance capabilities
- Inventory capabilities

Ordinary Farmer lifecycle permission does not imply Hard Delete permission.

No generic `/data-protection/entities/{type}/{id}` endpoint or generic hard-delete resolver is introduced.

## 9. Business Fact boundary

C18 does not modify:
- `ConfirmProcurementEntry`
- Procurement Batch Close / Reopen
- Finance obligation/adjustment/settlement behavior
- Farmer Soft Delete / Restore
- Inventory behavior

No payment state or Procurement Batch state is consulted to decide Hard Delete eligibility. Typed dependency existence alone is sufficient to block physical deletion.

`OUT-003` remains unresolved. Outsourced Supply Batch Reopen remains DEFERRED and is unrelated to Farmer Hard Delete.

## 10. Persistence impact

V8-C18 introduces no:
- relation
- schema
- EF model mapping change
- model snapshot change
- migration

`InitialV01` remains unchanged.

No Generic Repository, generic CRUD, or generic hard-delete architecture is introduced.

## 11. Acceptance evidence

Implementation commit:
- `0871bd006dfe1fa48db0b305a043277e09debfa2`
- `feat: add farmer hard delete`

Validation branch:
- `p6-v8-farmer-hard-delete-validation`

Local hard gates:
- Domain: 29/29 PASS
- Architecture: 66/66 PASS
- API Contract: 110/110 PASS
- PostgreSQL Integration: 123/123 PASS
- total: **328/328 PASS**

Acceptance specifically proves both dependency paths independently:
- existing Procurement Entry blocks Hard Delete without cascade/residue
- existing Finance Payable blocks Hard Delete without cascade/residue

Final Release solution build:
- 0 errors
- only existing solution custom-output `NETSDK1194`

PostgreSQL acceptance endpoint during validation:
- PostgreSQL 18.6
- `yowthi_dev`
- `isInRecovery=false`

An initial C18 integration run exposed only a test-cleanup relation-name typo (`finance.payable_outstanding` instead of the authoritative `finance.payable_outstanding_positions`). The test cleanup was corrected; no executor semantics changed. The rebuilt integration module then passed 123/123.

## 12. Self-hosted validation evidence

The normal Formal r17 / Bootstrap r2 typed GitHub workflow-status queries again returned invocation errors for this validation publication. C18 therefore used direct read-only evidence from the configured Windows self-hosted runner rather than assuming validation from branch publication.

Runner evidence:
- runner registration file `.runner` identifies `agentName = YowThi-ERP-V2`
- repository = `yenpoli-web/yowthi-erp-v2`
- workflow = `.github/workflows/dotnet.yml` / `dotnet-self-hosted`
- workflow requires `runs-on: [self-hosted, yowthi-erp-v2]`
- job = `build-test`
- Worker log = `C:\actions-runner\actions-runner\_diag\Worker_20260905-081554-utc.log`
- exact commit SHA appears as `0871bd006dfe1fa48db0b305a043277e09debfa2`
- workflow ref identifies `refs/heads/p6-v8-farmer-hard-delete-validation`
- all recorded job/step results are `succeeded`
- `complete_job` result = `succeeded`
- final `Job result after all job steps finish: Succeeded`

The typed GitHub query did not provide a run ID during this incident, so no run ID is asserted here.

This runner-local evidence closes the required facts: exact validation SHA, intended workflow/ref, intended self-hosted runner identity, required labels from the workflow definition, and successful job completion.

## 13. Promotion

Implementation promotion:
- ff-only local `main`
- non-force push
- fetch/read-back clean
- Formal r17 primary formal channel
- Bootstrap r2 independent recovery/read-back channel

Formal implementation baseline after promotion:
- `main = origin/main = 0871bd006dfe1fa48db0b305a043277e09debfa2`

## 14. C18 dependency-scan conclusion

Farmer Hard Delete is structurally closed over the two known direct typed Farmer dependencies:
- Procurement Entry
- Finance Payable

C18 does not generalize this closure to any other Party or master target. Additional Hard Delete targets still require their own repository-wide typed dependency scan and target-specific acceptance.

Existing deferred lifecycle candidates remain unchanged:
- `StorageLocation` -> DEFERRED
- `ProcessMaterial` -> DEFERRED
- `ProcessingRoute` -> DEFERRED
- `ProcurementProduct` -> TO VERIFY / DEFERRED
- `SalesProduct` -> TO VERIFY / DEFERRED

## 15. Business Rule boundary

V8-C18 does not introduce, resolve, or reinterpret a YowThi Business Rule.

The only new decision is target-specific structural Data Protection behavior: a Farmer can be physically removed only when neither Procurement Entry nor Finance Payable typed dependency remains.

Existing unresolved Business Rule gaps remain unchanged. In particular:
- `OUT-003` remains unresolved
- Outsourced Supply Batch Reopen remains DEFERRED
- no payment-state, Procurement Batch-state, or other Business Fact behavior is inferred from Hard Delete
