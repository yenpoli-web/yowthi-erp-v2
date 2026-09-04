# Outsourced Vendor Lifecycle Control v0.1 — YowThi ERP V2

Status: **IMPLEMENTED / V8-C10 COMPLETE**

This document is the later implementation supplement for Outsourced Vendor Soft Delete / Restore. It supplements older Party command/REST omissions without redefining the broader architecture.

## 1. Classification

`SoftDeleteOutsourcedVendor` and `RestoreOutsourcedVendor` are **ERP Control Commands**.

They maintain ERP master-data lifecycle state. They do not create a new YowThi Business Fact and do not require a new business rule merely to permit the lifecycle mutation.

Authoritative classification:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

## 2. Commands and routes

Commands:
- `SoftDeleteOutsourcedVendor`
- `RestoreOutsourcedVendor`

Routes:
- `POST /api/v1/party/outsourced-vendors/{outsourcedVendorId}/soft-delete`
- `POST /api/v1/party/outsourced-vendors/{outsourcedVendorId}/restore`

OperationIds:
- `Party_SoftDeleteOutsourcedVendor`
- `Party_RestoreOutsourcedVendor`

Capability:
- `party.outsourced-vendor.lifecycle`

Request:
- route Outsourced Vendor ID
- `expectedRowVersion`
- required `Idempotency-Key`

Canonical command identity includes:
- command type
- route Outsourced Vendor ID
- expected row version

## 3. Soft Delete semantics

Soft Delete:
- keeps the `party.outsourced_vendors` row
- sets `deleted_at`
- sets `deleted_by_account_id` from the authenticated actor
- increments `row_version`
- preserves the existing `active` value
- does not cascade or delete historical Outsourced Supply Batch references

Historical dependency:
- `outsourced.outsourced_supply_batches.outsourced_vendor_id`

An existing Batch FK does not by itself block Soft Delete because the Vendor row remains available for typed FK integrity and historical traceability.

Current-use vendor selectors already require:

```text
active = true
AND deleted_at IS NULL
```

Therefore a soft-deleted Vendor naturally exits new Outsourced Supply Detail selection.

## 4. Restore semantics

Restore:
- clears `deleted_at`
- clears `deleted_by_account_id`
- increments `row_version`
- preserves the existing `active` value

Restore does **not** automatically reactivate an inactive Vendor.

Consequences:
- active Vendor -> Soft Delete -> Restore -> selectable again
- inactive Vendor -> Soft Delete -> Restore -> remains inactive and remains excluded from current-use selector

## 5. Authorization boundary

`party.outsourced-vendor.lifecycle` is target-specific technical authorization.

It is deliberately separate from:
- `outsourced.confirm`
- `party.supplier.lifecycle`
- `party.customer.lifecycle`
- `data-protection.hard-delete`

Permission to register an Outsourced Supply Detail does not automatically imply permission to change Vendor lifecycle state.

Permission to change Vendor lifecycle state does not imply Hard Delete.

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
- Vendor not found -> `party.outsourced-vendor-not-found`
- Soft Delete already deleted -> `party.outsourced-vendor-already-deleted`
- Restore current/non-deleted -> `party.outsourced-vendor-not-deleted`
- stale expected row version -> `concurrency.stale-row-version`

Failed lifecycle/concurrency attempts roll back newly acquired CommandExecution and do not leave lifecycle Audit residue.

## 7. Audit

Audit Event:
- `event_kind = DATA_LIFECYCLE`
- command type = target lifecycle command
- actor = authenticated server-resolved Account

Audit subject:
- `subject_kind = party.outsourced-vendor`
- Soft Delete `change_kind = SOFT_DELETE`
- Restore `change_kind = RESTORE`
- before/after row versions retained

Ordinary Party lifecycle follows the established Supplier/Customer pattern and does not require an Outbox message merely for the local master lifecycle transition.

## 8. Persistence impact

V8-C10 introduces no:
- relation
- schema
- EF model mapping change
- model snapshot change
- migration

`InitialV01` remains unchanged.

## 9. Acceptance evidence

Implementation commit:
- `470dfdda2fcf5da000f6174892d338c443815fd9`
- `feat: add outsourced vendor lifecycle control`

Validation branch:
- `p6-v8-outsourced-vendor-lifecycle-validation`

Local hard gates:
- Domain: 29/29 PASS
- Architecture: 66/66 PASS
- API Contract: 86/86 PASS
- PostgreSQL Integration: 95/95 PASS
- total: **276/276 PASS**

Final Release solution build:
- 0 errors
- only existing solution custom-output `NETSDK1194`

Self-hosted validation:
- workflow: `dotnet.yml` / `dotnet-self-hosted`
- run: `33877179734`
- exact SHA: `470dfdda2fcf5da000f6174892d338c443815fd9`
- runner: `YowThi-ERP-V2`
- labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`

Promotion:
- ff-only local main
- non-force push
- fetch/read-back clean

Formal implementation baseline after promotion:
- `main = origin/main = 470dfdda2fcf5da000f6174892d338c443815fd9`

## 10. Outsourced Batch Reopen remains deferred

V8-C10 does not implement Outsourced Supply Batch Reopen.

`OUT-003` remains a Class A Business Rule gap: a Closed Outsourced Supply Batch currently blocks normal late detail registration.

The existing `ConfirmOutsourcedSupplyDetail` command blocks when the resolved Batch is `CLOSED`. If an explicit Reopen command simply changes that Batch to `ACTIVE`, the existing Business Fact command would subsequently allow late detail. That would effectively decide an unconfirmed real operating rule.

Therefore:
- do not infer that explicit Reopen permits late Outsourced Supply Detail
- do not implement Outsourced Batch Reopen until the real YowThi late-detail behavior is confirmed, or until a design can preserve the unresolved Business Fact safely
- do not use lifecycle control to silently resolve `OUT-003`

This is a Business Fact hard gate, distinct from the C10 Vendor lifecycle control.
