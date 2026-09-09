# Transaction Deletion Control v0.1 — YowThi ERP V2

Status: **CONFIRMED BUSINESS / SECURITY DECISION — IMPLEMENTATION OPEN**
Confirmed: 2026-09-09

## 1. Purpose

This document records the confirmed YowThi requirement that Procurement, Processing, and Sales transaction data must support controlled deletion as an ERP maintenance operation.

This is a correction to any earlier interpretation that confirmed transaction data is categorically ineligible for deletion merely because the business event already occurred.

ERP remains a registration system. When an authorized operator deliberately enters a deletion flow, the operator is asserting that the selected record is erroneous/unwanted data that must be removed from operational use or physically removed.

## 2. Confirmed scope

The following operational transaction modules require deletion support:

- Procurement
- Processing
- Sales

Each module must support both master-level and detail-level deletion.

### 2.1 Procurement

Master:
- `procurement.procurement_batches`

Detail:
- `procurement.procurement_entries`

Required operations:
- Soft Delete master
- Restore master
- Hard Delete master
- Soft Delete individual detail
- Restore individual detail
- Hard Delete individual detail

### 2.2 Processing

Master:
- `processing.processing_executions`

Owned details:
- `processing.processing_execution_inputs`
- `processing.processing_execution_outputs`

Required operations:
- Soft Delete execution
- Restore execution
- Hard Delete execution
- delete/restore individual user-addressable detail where the UI exposes a processing detail row
- Hard Delete individual owned detail where structurally representable

### 2.3 Sales

Master:
- `sales.sales`

Detail:
- `sales.sales_details`

Owned allocation/revision rows remain technical children of the Sales transaction/detail tree.

Required operations:
- Soft Delete master
- Restore master
- Hard Delete master
- Soft Delete individual detail
- Restore individual detail
- Hard Delete individual detail

## 3. Deletion is not blocked merely because the event is confirmed

Deletion eligibility must not be rejected solely because a transaction is:

- CONFIRMED
- completed
- closed
- already used to create derived ERP records

Those states may affect the technical deletion closure, but they are not Business Rule prohibitions on deletion.

The user-confirmed intent is:

> Entering the deletion flow means the operator has already decided the selected data is data that must be removed.

Therefore dependency discovery is used to determine what must be cleaned up safely; it is not automatically converted into a business prohibition.

## 4. Soft Delete

Soft Delete is the ordinary reversible removal operation.

Baseline semantics:
- set `deleted_at`
- set `deleted_by_account_id`
- increment `row_version`
- append Audit
- keep the physical row and structural traceability
- normal operational lists exclude deleted rows by default
- deleted-data views can include them explicitly

Soft Delete of a master does not physically remove its details.

Restore clears the deletion markers, increments `row_version`, and appends Audit.

## 5. Detail deletion

A detail row is independently removable.

The UI must expose deletion at the detail-row level instead of requiring deletion of the entire master.

For a detail-row Soft Delete:
- only the selected detail becomes deleted
- the master remains present
- totals/read models must rebuild from non-deleted details according to the owning module projection

For a detail-row Hard Delete:
- remove the selected detail
- remove technical/derived rows that exist solely because of that detail
- retain Hard Delete Audit
- do not silently delete unrelated sibling details

## 6. Hard Delete closure

Hard Delete is a deliberate physical-removal operation.

A Hard Delete command must compute and execute a target-specific deletion closure in one database transaction.

The closure is technical persistence behavior, not a new Business Rule.

General order:
1. acquire persistent CommandId/idempotency
2. lock/read target and validate `row_version`
3. verify recent deletion re-authentication
4. identify target-owned and target-derived dependencies
5. remove target-derived rows in FK-safe order
6. remove target-owned detail rows where the master itself is being deleted
7. physically remove the target
8. append retained Hard Delete Audit in the same transaction
9. commit

No generic `(entityType, id)` delete resolver is introduced. Procurement, Processing, and Sales remain explicit target-specific commands.

## 7. Known technical dependency closure

Repository-wide typed reference scan currently identifies at least the following direct dependencies.

### 7.1 Procurement Entry

Direct references include:
- Finance Company Pickup Transport Basis
- Finance Payable Obligation Item
- Inventory Operation

A Procurement Entry Hard Delete must clean its dependent technical/derived rows before removing the Entry.

### 7.2 Procurement Batch

Direct references include:
- Procurement Entries
- Processing Executions
- Finance Payable / Payable Obligation Items
- Inventory Movements
- Inventory Operations
- Inventory Positions
- Sales Allocation Revision Items carrying Procurement Batch provenance

A Procurement Batch Hard Delete therefore requires a transaction-tree closure rather than a single-row delete.

### 7.3 Processing Execution

Direct references include:
- Processing Execution Input
- Processing Execution Outputs
- Inventory Operation

Additional indirect inventory/audit effects must be closed through the generated operation/movement tree.

### 7.4 Sales Detail

Direct references include:
- Sales Allocations
- Sales Allocation Revision Items
- Finance Receivable Obligation Items

### 7.5 Sale

Direct references include:
- Sales Details
- Sales Allocation Revisions / Revision Items
- Finance Receivable / Receivable Obligation Items
- Inventory Operations
- Sales Packaging Work Records

The exact deletion order must be proven by integration tests against PostgreSQL 18.

## 8. Mandatory deletion re-authentication

All delete operations require recent authentication confirmation before execution.

This applies to:
- Soft Delete master
- Hard Delete master
- Soft Delete detail
- Hard Delete detail

Restore does not require deletion re-authentication unless a later security decision explicitly changes this.

Re-authentication is a security control against accidental deletion; it is not a Business Rule about whether the transaction may be deleted.

## 9. Deployed-account re-authentication

Deployed authentication remains external IdP/OIDC based.

The ERP must not add a second ERP-owned copy of a user's login password merely to support deletion confirmation.

For deployed accounts, deletion re-authentication is performed through the configured authentication provider using a fresh/recent interactive authentication event.

The resulting ERP session records only a short-lived re-authentication assertion/timestamp; it does not store the user's password.

## 10. Development Test Admin

The current Development Test Admin remains passwordless for normal login.

For deletion acceptance work:
- the UI must not ask the operator to type a test password
- the browser must not contain a plaintext test password in source/bundle/localStorage/sessionStorage
- a Development-only server endpoint may automatically issue the same short-lived deletion re-authentication assertion for the persistent Development Test Admin account
- this automatic path must be impossible outside the Development environment

Functionally this is the built-in test-account deletion password behavior requested for the current test account, implemented without exposing a reusable plaintext credential.

## 11. Re-authentication freshness

Deletion authorization requires a recent re-authentication assertion.

The exact freshness duration is a technical security setting, not a Business Rule.

Initial implementation should use a short finite window and must not turn one re-authentication into an unbounded delete bypass.

## 12. Authorization

Deletion still requires explicit capability authorization in addition to re-authentication.

Target-specific capabilities may be introduced for transaction lifecycle/hard-delete operations.

No job-title Role Master or generic super-user bypass is implied.

Development Test Admin continues to receive all known explicit capabilities through the existing Development-only provisioning path.

## 13. Concurrency and idempotency

Every deletion write command must preserve existing technical guarantees:
- persistent `CommandId`
- canonical request hashing
- idempotent success replay
- explicit expected `row_version`
- stale-version conflict
- actor identity from authenticated Account
- append-oriented Audit

A failed deletion closure must roll back the newly acquired command execution and leave no partial deletion residue.

## 14. UI behavior

Desktop/tablet/mobile transaction workspaces must expose:
- master delete action
- detail-row delete action
- deleted-state filtering/view
- restore action for soft-deleted records
- separate permanent-delete action

Before a delete action executes:
- normal account: require fresh login-provider re-authentication
- Development Test Admin: perform Development-only automatic re-authentication

Hard Delete confirmation must clearly distinguish physical deletion from reversible Soft Delete.

## 15. Implementation sequence

Implementation order:
1. shared deletion re-authentication security foundation
2. Procurement master/detail lifecycle + Hard Delete closure
3. Processing execution/detail lifecycle + Hard Delete closure
4. Sales master/detail lifecycle + Hard Delete closure
5. unified responsive UI behavior
6. PostgreSQL closure/concurrency/idempotency integration acceptance
7. exact-SHA self-hosted validation before main promotion

No candidate is formal until local hard gates and the YowThi ERP V2 self-hosted runner pass.
