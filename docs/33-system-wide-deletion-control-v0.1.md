# System-wide Deletion Control v0.1 — YowThi ERP V2

Status: **CONFIRMED BUSINESS / SECURITY DECISION — IMPLEMENTATION IN PROGRESS**
Confirmed: 2026-09-09

## 1. Purpose

After the Procurement / Processing / Sales deletion lifecycle work in `docs/32-transaction-deletion-control-v0.1.md` is completed, the same controlled deletion model must be extended across all user-operable YowThi ERP V2 business, master-data, and transaction modules.

This requirement exists to ensure that erroneous or unwanted ERP records can be removed consistently instead of leaving individual modules without a lifecycle path.

## 2. Scope

All UI-exposed operational/business modules must ultimately support:
- Soft Delete
- Restore
- Hard Delete

Where a module contains user-addressable detail rows, those details must also support the same lifecycle operations where structurally representable.

The implementation is target-specific. Each target must define and test its own dependency/deletion closure; no generic `(entityType, id)` CRUD/delete architecture is introduced.

System technical evidence/infrastructure such as append-oriented Audit, persisted CommandExecution idempotency records, and other retained evidence required to prove a deletion are not treated as ordinary user-deletable business-module records. Hard Delete must continue to retain its Audit evidence.

## 3. Soft Delete authentication rule

Soft Delete is reversible but destructive from the normal operational view and therefore requires fresh user re-authentication before execution.

For a normal deployed account:
- the user must re-enter/confirm the credentials required by the configured login provider before Soft Delete executes
- when the provider uses a password, this means entering the user's login password in the provider-controlled re-authentication UI
- ERP must not keep a second plaintext or hashed copy of the external login password merely for deletion confirmation
- ERP stores only a short-lived successful re-authentication assertion/timestamp in the protected session

For the Development Test Admin:
- Development-only automatic re-authentication remains permitted
- the test credential must not be exposed in browser source, bundle, localStorage, sessionStorage, logs, or ordinary API payloads
- the automatic path must be impossible outside Development

Restore remains non-destructive and does not require deletion re-authentication unless later explicitly changed.

## 4. Hard Delete authority and authentication rule

Hard Delete is physical deletion and is restricted to the highest deletion authority.

Global baseline:
- the authenticated account must possess the explicit `data-protection.hard-delete` capability
- target/module lifecycle capability may also be required where applicable
- no ordinary create/update/confirm/lifecycle capability implies Hard Delete authority
- no job-title or implicit role grants Hard Delete
- the Development Test Admin receives this capability only through the existing Development-only explicit capability provisioning path

Before every Hard Delete execution, fresh re-authentication is mandatory under the same credential rule used for Soft Delete.

Therefore Hard Delete requires both:
1. highest explicit Hard Delete capability authorization
2. fresh authentication confirmation

## 5. Safety and technical guarantees

All Soft Delete and Hard Delete commands must preserve:
- authenticated server-resolved actor identity
- persistent `CommandId`
- canonical request hashing
- idempotent success replay
- explicit expected `row_version`
- stale-version conflict
- append-oriented Audit
- one database transaction for each physical deletion closure
- full rollback on closure failure

Hard Delete must remove only the selected target and its target-owned/target-derived closure. Unrelated sibling or independent data must not be removed accidentally.

## 6. Implementation order

1. complete `docs/32` Procurement / Processing / Sales master/detail deletion lifecycle and exact-SHA validation
2. perform a repository-wide operational module inventory
3. classify each remaining UI-operable module by master/detail targets and existing lifecycle support
4. add missing Soft Delete / Restore support
5. add target-specific Hard Delete closure and `data-protection.hard-delete` authorization
6. apply the shared deletion re-authentication guard to both Soft Delete and Hard Delete
7. add responsive UI controls and deleted/current views
8. prove each target with API contracts and PostgreSQL integration tests
9. validate each focused candidate on the YowThi ERP V2 self-hosted runner before main promotion

No remaining operational module may be considered deletion-complete merely because it has a UI delete button; API capability metadata, re-authentication, persistence lifecycle, Hard Delete closure, Audit, concurrency, idempotency, and actual PostgreSQL acceptance must all correspond.

## 7. Current implementation checkpoint — 2026-09-11

System-wide deletion implementation is active after transaction lifecycle closure.

Current evidence includes:
- Procurement / Processing / Sales transaction lifecycle and target-specific Hard Delete controls
- Outsourced Supply Batch / Detail lifecycle and target-specific Hard Delete controls
- fresh deletion re-authentication enforced consistently for existing master Soft Delete endpoints
- Storage Location Soft Delete / Restore candidate implemented with server re-authentication, idempotency, row-version concurrency, Audit, PostgreSQL integration acceptance, responsive Web controls, and deleted/current views

Storage Location Hard Delete is **not** included in that Soft Delete / Restore candidate and remains separate target-specific Data Protection work. The dependency closure must be inventoried before implementation because Storage Location participates in typed/default/current Inventory and operational references. No generic cascade or history rewrite may be introduced.
