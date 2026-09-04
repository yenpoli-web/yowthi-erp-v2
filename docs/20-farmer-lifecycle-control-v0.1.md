# Farmer Lifecycle Control v0.1 — YowThi ERP V2

Status: **IMPLEMENTED / V8-C11 COMPLETE**

This document is the later implementation supplement for Farmer Soft Delete / Restore. It supplements older Party command/REST omissions without redefining the broader architecture.

## 1. Classification

`SoftDeleteFarmer` and `RestoreFarmer` are **ERP Control Commands**.

They maintain ERP master-data lifecycle state. They do not create a new YowThi Business Fact and do not require a new Business Rule merely to permit the lifecycle mutation.

Authoritative classification:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

## 2. Commands and routes

Commands:
- `SoftDeleteFarmer`
- `RestoreFarmer`

Routes:
- `POST /api/v1/party/farmers/{farmerId}/soft-delete`
- `POST /api/v1/party/farmers/{farmerId}/restore`

OperationIds:
- `Party_SoftDeleteFarmer`
- `Party_RestoreFarmer`

Capability:
- `party.farmer.lifecycle`

Request:
- route Farmer ID
- `expectedRowVersion`
- required `Idempotency-Key`

Canonical command identity includes:
- command type
- route Farmer ID
- expected row version

## 3. Soft Delete semantics

Soft Delete:
- keeps the `party.farmers` row
- sets `deleted_at`
- sets `deleted_by_account_id` from the authenticated actor
- increments `row_version`
- preserves the existing `active` value
- does not cascade or delete historical Procurement references

Historical dependency:
- `procurement.procurement_entries.farmer_id`

An existing Procurement Entry FK does not by itself block Soft Delete because the Farmer row remains available for typed FK integrity and historical traceability.

Current-use Procurement Farmer selectors require:

```text
active = true
AND deleted_at IS NULL
```

Therefore a soft-deleted Farmer naturally exits new Procurement source selection while historical Procurement rows keep their Farmer reference.

## 4. Restore semantics

Restore:
- clears `deleted_at`
- clears `deleted_by_account_id`
- increments `row_version`
- preserves the existing `active` value

Restore does **not** automatically reactivate an inactive Farmer.

Consequences:
- active Farmer -> Soft Delete -> Restore -> selectable again for current Procurement use
- inactive Farmer -> Soft Delete -> Restore -> remains inactive and remains excluded from current-use Procurement selection

## 5. Authorization boundary

`party.farmer.lifecycle` is target-specific technical authorization.

It is deliberately separate from:
- `procurement.confirm`
- `party.supplier.lifecycle`
- `party.customer.lifecycle`
- `party.outsourced-vendor.lifecycle`
- `data-protection.hard-delete`

Permission to register Procurement does not automatically imply permission to change Farmer lifecycle state.

Permission to change Farmer lifecycle state does not imply Hard Delete.

Capability grants remain deployment-configured by persistent ERP Account UUID under `docs/15-authn-authz-implementation-architecture-v0.1.md`.

## 6. Idempotency and concurrency

CommandId acquisition/replay occurs before current lifecycle/version validation.

Same:
- actor
- command type
- canonical request hash

replays the committed result.

Changed actor/type/hash under the same CommandId returns:
- `409`
- `idempotency.key-reused`

State/concurrency failures include:
- Farmer not found -> `party.farmer-not-found`
- Soft Delete already deleted -> `party.farmer-already-deleted`
- Restore current/non-deleted -> `party.farmer-not-deleted`
- stale expected row version -> `concurrency.stale-row-version`

Invalid command identity/version input uses:
- `party.farmer-lifecycle-invalid`

Failed lifecycle/concurrency attempts roll back newly acquired CommandExecution and do not leave lifecycle Audit residue.

## 7. Audit

Audit Event:
- `event_kind = DATA_LIFECYCLE`
- command type = target lifecycle command
- actor = authenticated server-resolved Account

Audit subject:
- `subject_kind = party.farmer`
- Soft Delete `change_kind = SOFT_DELETE`
- Restore `change_kind = RESTORE`
- before/after row versions retained

Ordinary Party lifecycle follows the established Supplier/Customer/Outsourced Vendor pattern and does not require an Outbox message merely for the local master lifecycle transition.

## 8. Persistence impact

V8-C11 introduces no:
- relation
- schema
- EF model mapping change
- model snapshot change
- migration

`InitialV01` remains unchanged.

## 9. Acceptance evidence

Implementation commit:
- `ac5a312bae6e4d20663bb29f0b272deedf9ba568`
- `feat: add farmer lifecycle control`

Validation branch:
- `p6-v8-farmer-lifecycle-validation`

Local hard gates:
- Domain: 29/29 PASS
- Architecture: 66/66 PASS
- API Contract: 89/89 PASS
- PostgreSQL Integration: 98/98 PASS
- total: **282/282 PASS**

Final Release solution build:
- 0 errors
- only existing solution custom-output `NETSDK1194`

Self-hosted validation:
- workflow: `dotnet.yml` / `dotnet-self-hosted`
- run: `33882137271`
- exact SHA: `ac5a312bae6e4d20663bb29f0b272deedf9ba568`
- runner: `YowThi-ERP-V2`
- labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`

Promotion:
- ff-only local main
- non-force push
- fetch/read-back clean

Formal implementation baseline after promotion:
- `main = origin/main = ac5a312bae6e4d20663bb29f0b272deedf9ba568`

## 10. Business Rule boundary

V8-C11 does not introduce, resolve, or reinterpret a YowThi Business Rule.

Farmer lifecycle is ERP Data Control over a master record. Historical Procurement registration remains intact through the typed Farmer FK, while current-use selection is governed by current master state.

Existing unresolved Business Rule gaps remain unchanged. In particular, C11 does not affect `OUT-003` or authorize Outsourced Supply Batch Reopen.