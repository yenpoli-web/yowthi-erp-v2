# Persistence Architecture Principles v0.1

## DECISION / v0.1

1. Single PostgreSQL 18 database.
2. Module-separated PostgreSQL schemas.
3. Single write `ErpDbContext` in v0.1 for one atomic Unit of Work.
4. UUID v7 technical IDs.
5. Business identities protected by explicit unique constraints.
6. PostgreSQL `date` for business dates.
7. PostgreSQL `timestamptz` for system timestamps, persisted in UTC.
8. No floating-point money.
9. Price uses exact `numeric`.
10. Final THB amounts use integer THB (`bigint`) where confirmed.
11. Configuration that affects historical calculations is snapshotted or version-referenced.
12. Inventory Movement = ledger truth.
13. Inventory Position = transactional, rebuildable current-state projection.
14. Finance Outstanding = rebuildable current state derived from obligations, adjustments, settlements.
15. Explicit optimistic concurrency `row_version bigint`.
16. Persistent command idempotency.
17. Transactional Outbox for post-commit work.
18. Append-oriented audit.
19. No unconstrained `(type,id)` polymorphic foreign keys.
20. Core traceability FKs default to `RESTRICT / NO ACTION`.
21. No generic EF CRUD as the business interface.
22. Historical queries must remain able to see inactive/deleted references.
23. Legacy schema does not drive the new persistence model.

## Proposed PostgreSQL schemas

- `party`
- `product`
- `processing_config`
- `procurement`
- `processing`
- `inventory`
- `outsourced`
- `sales`
- `sales_handling`
- `labor`
- `finance`
- `audit`
- `system`

## Typed references

Inventory Source Batch:
- IN_HOUSE → Procurement Batch FK
- OUTSOURCED → Outsourced Supply Batch FK

Inventory Object:
- Procurement Product FK
- Process Material FK
- Sales Product FK

Use discriminator + nullable real FKs + CHECK constraints.

## Soft delete

Master/config entities may use:
- `deleted_at`
- `deleted_by_account_id`

Soft delete does not imply delete is always allowed.
Owning domain policies still validate dependencies.

Inventory ledger facts should not use ordinary soft delete.

## Concurrency

Use explicit technical `row_version bigint`.
Do not confuse it with Processing Route business version.

## Idempotency

Persist:
- CommandId
- CommandType
- RequestHash
- status/result reference
- ExecutedAt

Same CommandId + same request → return previous result.
Same CommandId + different request → reject.

## Outbox

Critical cross-domain business writes occur in the same database transaction.
Outbox is for reliable post-commit event handling, not for delaying Inventory/Finance core facts.
