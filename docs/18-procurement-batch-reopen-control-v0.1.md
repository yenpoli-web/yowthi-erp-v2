# Procurement Batch Reopen Control v0.1 — YowThi ERP V2

Status: **IMPLEMENTED / V8-C9 COMPLETE**

This is a later implementation supplement for the Procurement Batch Reopen ERP Control slice. For V8-C9 only, it supplements older omissions in `docs/05-command-contracts-v0.1.md` and `docs/12-rest-api-architecture-v0.1.md`. It does not redefine the broader Command or REST architecture.

## 1. Classification

`ReopenProcurementBatch` is an **ERP Control Command**, not a Business Fact reversal.

It changes current ERP lifecycle state so authorized operators can continue applicable work. It does not assert that a new real-world procurement event occurred.

Authoritative classification remains:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`
- `docs/06-business-rule-gap-register-v0.1.md` → `LIFE-001 = CONTROL`

## 2. Command and route

Command:
- `ReopenProcurementBatch`

HTTP route:
- `POST /api/v1/procurement/batches/{procurementBatchId}/reopen`

OperationId:
- `Procurement_ReopenBatch`

Capability:
- `procurement.batch.lifecycle`

Request:
- route Procurement Batch ID
- `expectedRowVersion`
- optional `reasonText`
- required `Idempotency-Key`

Canonical command identity includes:
- command type
- route Procurement Batch ID
- expected row version
- reason text

## 3. Current-state semantics

Preconditions:
- target exists
- target is not soft-deleted
- expected Procurement Batch row version matches
- current `lifecycle_status = CLOSED`

Mutation:
- `lifecycle_status: CLOSED -> ACTIVE`
- `closed_at = NULL`
- `closed_by_account_id = NULL`
- `row_version = row_version + 1`

The close markers are current-state metadata. They must be cleared because the existing structural constraint `ck_procurement_batches_closing_state` requires:

```text
ACTIVE -> closed_at IS NULL and closed_by_account_id IS NULL
CLOSED -> closed_at IS NOT NULL and closed_by_account_id IS NOT NULL
```

The following are not changed by Reopen:
- `procurement_status`
- `completed_at`
- `completed_by_account_id`
- prior Procurement facts

## 4. Historical and inventory semantics

Reopen must not:
- delete prior Close Audit
- delete or reverse prior `BATCH_RECONCILIATION` Inventory Operations
- delete or reverse prior `BATCH_RECONCILIATION` Inventory Movements
- rewrite immutable Inventory Movement history
- reconstruct an imagined pre-close physical stock balance

A Procurement Batch that was closed after reconciliation may therefore reopen with its Inventory Position still at the post-Close value, including zero.

If physical stock is later found to differ from ERP stock, the discrepancy is registered explicitly through stocktake / `AdjustInventory`.

This is the required separation:

```text
Reopen lifecycle state
!= reverse historical inventory

physical inventory discrepancy
-> AdjustInventory
```

## 5. Authorization boundary

`procurement.batch.lifecycle` is target-specific technical authorization.

It is deliberately separate from:
- `procurement.confirm`
- `data-protection.hard-delete`

Permission to register or close ordinary Procurement work does not automatically imply permission to reopen a Closed Batch.

Permission to reopen does not imply:
- Hard Delete
- permission to rewrite Inventory history
- generic lifecycle authority for other Batch types

Capability grants remain deployment-configured by persistent ERP Account UUID under `docs/15-authn-authz-implementation-architecture-v0.1.md`.

## 6. Idempotency and concurrency

CommandId acquisition/replay occurs before current target lifecycle/version validation.

Same:
- actor
- command type
- canonical request hash

replays the committed result.

Changed actor/type/hash under the same CommandId:
- `409`
- `idempotency.key-reused`

Current-state conflicts:
- ACTIVE Batch → `procurement.batch-not-closed`
- deleted Batch → `procurement.batch-unavailable`
- stale row version → `procurement.concurrent-change`

Failed lifecycle/concurrency attempts roll back the newly acquired CommandExecution and do not leave Audit or Outbox residue.

## 7. Audit and Outbox

Audit Event:
- `event_kind = DATA_LIFECYCLE`
- `command_type = ReopenProcurementBatch`
- actor = authenticated server-resolved Account
- optional `reason_text` retained

Audit subject:
- `subject_kind = procurement.batch`
- `change_kind = UPDATE`
- before/after row versions retained
- `change_summary` records `CLOSED -> ACTIVE` and that current close markers were cleared

`UPDATE` is the existing structural Audit subject vocabulary; no new `REOPEN` subject change kind or schema change is introduced.

Outbox:
- `procurement.batch.reopened`

## 8. Persistence impact

V8-C9 introduces no:
- relation
- schema
- EF model mapping change
- model snapshot change
- migration

`InitialV01` remains unchanged.

## 9. Acceptance evidence

Implementation commit:
- `12e05a69b9365b09e39da48451f0428475c29289`
- `feat: add procurement batch reopen control`

Validation branch:
- `p6-v8-procurement-batch-reopen-validation`

Local hard gates:
- Domain: 29/29 PASS
- Architecture: 66/66 PASS
- API Contract: 83/83 PASS
- PostgreSQL Integration: 92/92 PASS
- total: **270/270 PASS**

Final Release solution build:
- 0 errors
- only existing solution custom-output `NETSDK1194`

Self-hosted validation:
- workflow: `dotnet.yml` / `dotnet-self-hosted`
- run: `33862502433`
- exact SHA: `12e05a69b9365b09e39da48451f0428475c29289`
- runner: `YowThi-ERP-V2`
- labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`

Promotion:
- ff-only local main
- non-force push
- fetch/read-back clean

Formal implementation baseline after promotion:
- `main = origin/main = 12e05a69b9365b09e39da48451f0428475c29289`

## 10. Scope boundary

V8-C9 completes **Procurement Batch Reopen only**.

It does not imply that another Batch type supports Reopen.

For example, Outsourced Supply Batch Reopen, if required, remains a separate target-specific ERP Control slice with its own:
- route/command
- current-state constraints
- capability decision
- acceptance evidence

Do not introduce a generic `(batchType,id)` reopen resolver.