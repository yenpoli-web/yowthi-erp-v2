# Employee Lifecycle Control v0.1 — YowThi ERP V2

Status: **IMPLEMENTED / V8-C12 COMPLETE**

This document is the later implementation supplement for Employee Soft Delete / Restore and the Employee current-use command guards completed with that lifecycle slice. It supplements older Party command/REST omissions without redefining the broader architecture or inventing a YowThi Business Rule.

## 1. Classification

`SoftDeleteEmployee` and `RestoreEmployee` are **ERP Control Commands**.

They maintain ERP master-data lifecycle state. They do not create a new YowThi Business Fact and do not require a new Business Rule merely to permit the lifecycle mutation.

Authoritative classification:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

C12 also closes an existing current-master-state enforcement gap in two Business Fact executors:
- `RecordSalesPackagingWork`
- `ConfirmEmployeeDailyWage`

That guard is not a new Business Rule. It makes server-side command behavior consistent with the established ERP master-state rule already used by Processing: a master row that is inactive or soft-deleted is unavailable for **new** operational registration, while historical records remain intact.

## 2. Commands and routes

Commands:
- `SoftDeleteEmployee`
- `RestoreEmployee`

Routes:
- `POST /api/v1/party/employees/{employeeId}/soft-delete`
- `POST /api/v1/party/employees/{employeeId}/restore`

OperationIds:
- `Party_SoftDeleteEmployee`
- `Party_RestoreEmployee`

Capability:
- `party.employee.lifecycle`

Request:
- route Employee ID
- `expectedRowVersion`
- required `Idempotency-Key`

Canonical command identity includes:
- command type
- route Employee ID
- expected row version

## 3. Structural dependency boundary

Employee has typed historical dependencies in the implemented model, including:
- Processing Execution
- Sales Packaging Work Record
- Employee Daily Wage

These relations use real typed Employee foreign keys with restrictive delete behavior.

Soft Delete keeps the `party.employees` row, so those historical typed references remain valid. Existing work/wage/processing history therefore does **not** itself block Employee Soft Delete.

C12 does not rewrite, detach, cascade-delete, or replace historical Employee references.

## 4. Soft Delete semantics

Soft Delete:
- keeps the `party.employees` row
- sets `deleted_at`
- sets `deleted_by_account_id` from the authenticated actor
- increments `row_version`
- preserves the existing `active` value
- does not cascade or delete historical Processing, Sales Packaging, or Daily Wage records

Current-use Processing Employee selection requires:

```text
active = true
AND deleted_at IS NULL
```

Therefore a soft-deleted Employee exits current Processing selection while historical Processing records remain intact.

## 5. Restore semantics

Restore:
- clears `deleted_at`
- clears `deleted_by_account_id`
- increments `row_version`
- preserves the existing `active` value

Restore does **not** automatically reactivate an inactive Employee.

Consequences:
- active Employee -> Soft Delete -> Restore -> available again for current use
- inactive Employee -> Soft Delete -> Restore -> remains inactive and remains excluded from current-use Processing selection

## 6. Business Fact current-use guards

Before C12, the common Employee command lock already exposed current Employee state, and Processing already rejected an Employee that was inactive or soft-deleted.

`RecordSalesPackagingWork` and `ConfirmEmployeeDailyWage` previously checked only Employee existence after acquiring that same state. This meant a caller that supplied an Employee ID directly could still attempt a new Business Fact registration even when the Employee was no longer current-use eligible.

C12 closes that inconsistency:

`RecordSalesPackagingWork` now rejects:
- `!employeeState.Active`
- `employeeState.Deleted`

with:
- `sales-handling.employee-inactive`
- HTTP/application conflict semantics

`ConfirmEmployeeDailyWage` now rejects the same current-master states with:
- `labor.employee-inactive`
- HTTP/application conflict semantics

Failure occurs inside the command transaction and rolls back newly acquired command state. Acceptance verifies no committed:
- CommandExecution
- Audit
- Outbox

residue for those rejected new registrations.

This does not invalidate or alter already-recorded work or wage history.

## 7. Authorization boundary

`party.employee.lifecycle` is target-specific technical authorization.

It is deliberately separate from:
- Processing operational capabilities
- Sales Packaging operational capabilities
- Labor/Daily Wage operational capabilities
- `party.supplier.lifecycle`
- `party.farmer.lifecycle`
- `party.customer.lifecycle`
- `party.outsourced-vendor.lifecycle`
- `data-protection.hard-delete`

Permission to register Employee-related operational facts does not automatically imply permission to change Employee lifecycle state.

Permission to change Employee lifecycle state does not imply Hard Delete.

Capability grants remain deployment-configured by persistent ERP Account UUID under `docs/15-authn-authz-implementation-architecture-v0.1.md`.

## 8. Idempotency and concurrency

CommandId acquisition/replay occurs before current lifecycle/version validation.

Same:
- actor
- command type
- canonical request hash

replays the committed lifecycle result.

Changed actor/type/hash under the same CommandId returns:
- `409`
- `idempotency.key-reused`

Lifecycle state/concurrency failures include:
- Employee not found -> `party.employee-not-found`
- Soft Delete already deleted -> `party.employee-already-deleted`
- Restore current/non-deleted -> `party.employee-not-deleted`
- stale expected row version -> `concurrency.stale-row-version`

Invalid command identity/version input uses:
- `party.employee-lifecycle-invalid`

Failed lifecycle/concurrency attempts roll back newly acquired CommandExecution and do not leave lifecycle Audit residue.

## 9. Audit / Outbox

Lifecycle Audit Event:
- `event_kind = DATA_LIFECYCLE`
- command type = target lifecycle command
- actor = authenticated server-resolved Account

Audit subject:
- `subject_kind = party.employee`
- Soft Delete `change_kind = SOFT_DELETE`
- Restore `change_kind = RESTORE`
- before/after row versions retained

Ordinary Party lifecycle follows the established target-specific Party pattern and does not require an Outbox message merely for the local Employee master lifecycle transition.

Existing Business Fact commands retain their own normal Outbox semantics when they succeed. The new inactive/deleted Employee guards commit no Outbox residue on rejection.

## 10. Persistence impact

V8-C12 introduces no:
- relation
- schema
- EF model mapping change
- model snapshot change
- migration

`InitialV01` remains unchanged.

No generic lifecycle resolver, Generic Repository, or generic CRUD path is introduced.

## 11. Acceptance evidence

Implementation commit:
- `82fe55f583e066c04456695b735b6ac8c8ef3d1f`
- `feat: add employee lifecycle control`

Validation branch:
- `p6-v8-employee-lifecycle-validation`

Local hard gates:
- Domain: 29/29 PASS
- Architecture: 66/66 PASS
- API Contract: 92/92 PASS
- PostgreSQL Integration: 102/102 PASS
- total: **289/289 PASS**

Final Release solution build:
- 0 errors
- only existing solution custom-output `NETSDK1194`

PostgreSQL acceptance endpoint during validation:
- PostgreSQL 18.6
- `yowthi_dev`
- `isInRecovery=false`

Self-hosted validation:
- workflow: `dotnet.yml` / `dotnet-self-hosted`
- run: `33889424952`
- exact SHA: `82fe55f583e066c04456695b735b6ac8c8ef3d1f`
- runner: `YowThi-ERP-V2`
- labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`

Promotion:
- ff-only local main
- non-force push
- fetch/read-back clean

Formal implementation baseline after promotion:
- `main = origin/main = 82fe55f583e066c04456695b735b6ac8c8ef3d1f`

## 12. Business Rule boundary

V8-C12 does not introduce, resolve, or reinterpret a YowThi Business Rule.

Employee lifecycle is ERP Data Control over a master record. Historical operational registration remains intact through typed Employee foreign keys. Current-use rejection of inactive/soft-deleted Employee IDs prevents new registration against a master that is no longer operationally selectable; it does not rewrite prior facts.

Existing unresolved Business Rule gaps remain unchanged. In particular, C12 does not affect `OUT-003` or authorize Outsourced Supply Batch Reopen.