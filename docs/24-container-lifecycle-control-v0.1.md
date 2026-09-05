# Container Lifecycle Control v0.1 — YowThi ERP V2

Status: **IMPLEMENTED / V8-C15 COMPLETE**

This document is the implementation supplement for Container Soft Delete / Restore. It records the target-specific ERP Control semantics implemented in C15 without redefining Processing business behavior and without inventing a YowThi Business Rule.

## 1. Classification

`SoftDeleteContainer` and `RestoreContainer` are **ERP Control Commands**.

They maintain lifecycle state of the Container master used by Processing configuration/current-use tare resolution. They do not register a real business event/result and therefore do not require a new Business Rule merely to permit the lifecycle mutation.

Authoritative classification:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

C15 does not change Processing configuration semantics or historical Processing facts.

## 2. Commands and routes

Commands:
- `SoftDeleteContainer`
- `RestoreContainer`

Routes:
- `POST /api/v1/infrastructure/containers/{containerId}/soft-delete`
- `POST /api/v1/infrastructure/containers/{containerId}/restore`

OperationIds:
- `Infrastructure_SoftDeleteContainer`
- `Infrastructure_RestoreContainer`

Capability:
- `infrastructure.container.lifecycle`

Request:
- route Container ID
- `expectedRowVersion`
- required `Idempotency-Key`

Canonical command identity includes:
- command type
- route Container ID
- expected row version

## 3. Structural dependency boundary

Direct typed configuration dependencies:
- `processing_config.route_input_configs.container_id -> infrastructure.containers.id`
- `processing_config.process_materials.container_id -> infrastructure.containers.id`
- both use Restrict delete behavior

Soft Delete preserves the `infrastructure.containers` row, so both typed configuration FKs remain intact.

C15 does not:
- null or rewrite `route_input_configs.container_id`
- null or rewrite `process_materials.container_id`
- modify Process Material `active`
- modify Process Material deletion markers
- modify Process Material `row_version`
- cascade lifecycle state into Processing configuration
- rewrite completed Processing executions

## 4. Historical Processing boundary

Completed Processing does not retain a Container FK in the Processing fact.

For SCALE_NET input, Processing stores the realized measurement snapshots including:
- `observed_scale_reading`
- `actual_container_count`
- `tare_weight_snapshot`
- `derived_net_quantity`
- `consumed_quantity`

Therefore later Container lifecycle maintenance does not alter the recorded result that actually occurred.

C15 acceptance proves that a previously committed `tare_weight_snapshot` remains unchanged after Container Soft Delete.

## 5. Current-use Processing behavior

`PostgreSqlConfirmProcessingExecutionExecutor.ResolveTareAsync` already required a referenced Container to satisfy:
- row exists
- `active = true`
- `deleted_at IS NULL`

C15 does not add a new Processing Business Rule. It preserves and verifies this pre-existing current-master-state guard.

Consequences:
- a configured Container can remain referenced by Route Input / Process Material after Soft Delete
- new Processing attempting to resolve that soft-deleted Container is rejected
- a restored active Container can again satisfy the existing guard
- a restored inactive Container remains inactive and is still not eligible for current use

Rejected new Processing rolls back its newly acquired CommandExecution and leaves no committed Audit or Outbox residue.

## 6. Soft Delete semantics

Soft Delete:
- keeps the `infrastructure.containers` row
- sets `deleted_at`
- sets `deleted_by_account_id` from the authenticated actor
- increments Container `row_version`
- preserves the existing Container `active` value
- leaves Processing configuration references unchanged
- leaves historical tare snapshots unchanged
- creates no Outbox message for the local master lifecycle transition

## 7. Restore semantics

Restore:
- clears Container `deleted_at`
- clears Container `deleted_by_account_id`
- increments Container `row_version`
- preserves the existing Container `active` value
- does not modify Route Input / Process Material references
- does not rewrite any historical Processing snapshot

Restore does **not** automatically reactivate an inactive Container.

Consequences:
- active Container -> Soft Delete -> Restore -> remains active
- inactive Container -> Soft Delete -> Restore -> remains inactive

## 8. Authorization boundary

`infrastructure.container.lifecycle` is target-specific technical authorization.

It is deliberately separate from:
- `processing.confirm`
- Product lifecycle capabilities
- Party lifecycle capabilities
- `data-protection.hard-delete`

Permission to confirm Processing does not imply permission to change Container lifecycle state.

Permission to change Container lifecycle state does not imply permission to rewrite Processing configuration, Processing history, or perform Hard Delete.

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
- Container not found -> `infrastructure.container-not-found`
- Soft Delete already deleted -> `infrastructure.container-already-deleted`
- Restore current/non-deleted -> `infrastructure.container-not-deleted`
- stale expected row version -> `concurrency.stale-row-version`

Invalid command identity/version input uses:
- `infrastructure.container-lifecycle-invalid`

Failed lifecycle/concurrency attempts roll back newly acquired CommandExecution and do not leave lifecycle Audit residue.

## 10. Audit / Outbox

Lifecycle Audit Event:
- `event_kind = DATA_LIFECYCLE`
- command type = target lifecycle command
- actor = authenticated server-resolved Account

Audit subject:
- `subject_kind = infrastructure.container`
- Soft Delete `change_kind = SOFT_DELETE`
- Restore `change_kind = RESTORE`
- before/after Container row versions retained

No Outbox message is emitted merely for this local master lifecycle transition.

## 11. Persistence impact

V8-C15 introduces no:
- relation
- schema
- EF model mapping change
- model snapshot change
- migration

`InitialV01` remains unchanged.

No Generic Repository, generic CRUD, or generic lifecycle resolver is introduced.

## 12. Acceptance evidence

Implementation commit:
- `a71c14c24c6317a9ce4cdb85eb5885ca684e4352`
- `feat: add container lifecycle`

Validation branch:
- `p6-v8-container-lifecycle-validation`

Local hard gates:
- Domain: 29/29 PASS
- Architecture: 66/66 PASS
- API Contract: 101/101 PASS
- PostgreSQL Integration: 113/113 PASS
- total: **309/309 PASS**

Final Release solution build:
- 0 errors
- only existing solution custom-output `NETSDK1194`

PostgreSQL acceptance endpoint during validation:
- PostgreSQL 18.6
- `yowthi_dev`
- `isInRecovery=false`

Self-hosted validation:
- workflow: `dotnet.yml` / `dotnet-self-hosted`
- run: `33941679116`
- exact SHA: `a71c14c24c6317a9ce4cdb85eb5885ca684e4352`
- runner: `YowThi-ERP-V2`
- labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`

Promotion:
- ff-only local main
- non-force push
- fetch/read-back clean
- Formal r16 and Bootstrap r2 read-back agree on the repository state used for C15

Formal implementation baseline after promotion:
- `main = origin/main = a71c14c24c6317a9ce4cdb85eb5885ca684e4352`

## 13. Business Rule boundary

V8-C15 does not introduce, resolve, or reinterpret a YowThi Business Rule.

Container lifecycle is ERP Data Control over a master record. Configuration FKs remain intact, historical Processing snapshots remain intact, and current-use eligibility continues to be enforced by the already-established Processing executor guard.

Existing unresolved Business Rule gaps remain unchanged. In particular:
- `ProcurementProduct` lifecycle remains a TO VERIFY / DEFERRED candidate where current Processing behavior for already-created active Batches is ambiguous
- `SalesProduct` lifecycle remains a TO VERIFY / DEFERRED candidate where already-entered Sales behavior is ambiguous
- `OUT-003` remains unresolved and does not authorize Outsourced Supply Batch Reopen
