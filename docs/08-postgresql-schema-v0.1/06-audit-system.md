# PostgreSQL Schema v0.1 — Part 6: Audit + System

Status: **DECISION / v0.1**
Revision basis: Part 6 Revision 0.2, formally confirmed.

## 1. Scope and principles

Part 6 supplies technical persistence required by Parts 1–5. It does not introduce a new business domain or invent authentication/authorization business rules.

Core decisions:
- `system.accounts` is a minimal persistent actor-identity anchor only.
- persistent command idempotency is transactional with the business command.
- transactional outbox is for reliable post-commit work only.
- audit is append-oriented and business-command-oriented, not a mirror of EF/database row changes.
- normal domain relationships continue to use typed real foreign keys.
- audit historical locators are intentionally non-FK metadata because audit must survive target lifecycle and physical deletion.
- Hard Delete remains an owning-domain controlled operation with retained audit and no silent cascade.
- idempotency, outbox, and audit retention lifecycles are not coupled through foreign keys.

## 2. `system.accounts`

`system.accounts` is the relational target for actor/operator fields already used throughout Parts 1–5, including:
- `created_by_account_id`
- `recorded_by_account_id`
- `confirmed_by_account_id`
- `deleted_by_account_id`

Fields:
- `id uuid` PK
- `display_name text`
- `active boolean`
- `row_version bigint`
- `created_at timestamptz`

Rules:
- this table is only a persistent actor identity anchor
- actor references use real FKs to `system.accounts(id)` with `RESTRICT / NO ACTION`
- inactive accounts remain available for historical references
- Part 6 does not define credentials, login identifiers, roles, permissions, MFA, sessions, external identity providers, or Employee linkage

Authentication and authorization persistence are deferred to their own architecture work.

## 3. `system.command_executions`

Provides persistent command idempotency.

Fields:
- `command_id uuid` PK
- `command_type text NOT NULL`
- `request_hash bytea NOT NULL`
- `status`
- `result_payload jsonb NULL`
- `actor_account_id uuid` FK NOT NULL
- `started_at timestamptz NOT NULL`
- `executed_at timestamptz NULL`

Status values:
- `IN_PROGRESS`
- `SUCCEEDED`

Constraints:
- `octet_length(request_hash) = 32`

Request hash:
- SHA-256
- calculated from a canonical business-command payload
- transport-only metadata must not affect the hash

`result_payload` is an idempotency response snapshot. It is not business source-of-truth and is intentionally not represented by a generic `(type,id)` reference.

## 4. Command idempotency transaction

Command execution pattern:

1. begin transaction
2. insert `system.command_executions` with `IN_PROGRESS`
3. execute owning-domain validation and business writes
4. perform required cross-domain atomic writes
5. append audit facts
6. enqueue transactional outbox messages
7. set command execution to `SUCCEEDED`, persist deterministic result snapshot and `executed_at`
8. commit

The `command_id` PK is the duplicate-command synchronization boundary.

Concurrent same `command_id` behavior:
- PostgreSQL unique-index conflict waits for the first transaction
- first transaction rolls back → later insert may proceed
- first transaction commits → later request loads the existing execution
- same CommandId + same CommandType + same RequestHash → return stored result
- same CommandId + different CommandType or RequestHash → reject

No global command lock table or distributed mutex is required for this invariant.

Normal command failure rolls back the command-execution row together with business writes, audit, and outbox. No durable `FAILED` idempotency state is required in v0.1.

## 5. Idempotency retention boundary

`system.command_executions` is operational idempotency history.

Other tables may copy `command_id` as immutable correlation metadata, but must not FK to `system.command_executions`.

This intentionally decouples:
- command-idempotency retention
- audit retention
- outbox retention

Operational retention duration is deferred; the relational model must not force these lifecycles to be identical.

## 6. `system.outbox_messages`

Fields:
- `id uuid` PK
- `message_type text NOT NULL`
- `message_version integer NOT NULL`
- `payload jsonb NOT NULL`
- `command_id uuid NULL` — correlation only, NO FK
- `occurred_at timestamptz NOT NULL`
- `available_at timestamptz NOT NULL`
- `published_at timestamptz NULL`
- `delivery_attempt_count integer NOT NULL`
- `next_attempt_at timestamptz NULL`
- `locked_until timestamptz NULL`
- `lock_token uuid NULL`
- `last_error_summary text NULL`

Constraints:
- `message_version > 0`
- `delivery_attempt_count >= 0`

No `row_version` is required for the outbox work queue.

## 7. Transactional outbox semantics

Outbox rows are inserted in the same transaction as the business facts that caused them.

Outbox is only for post-commit work. It must not delay core facts such as:
- Inventory Movement
- Inventory Position update
- Payable / Receivable
- Finance Outstanding projection update

Delivery semantics:
- at-least-once
- `outbox_messages.id` is the stable message identity and downstream deduplication key

Exactly-once external delivery is not claimed.

No `system.inbox_messages` table is introduced until a real inbound integration boundary requires durable consumer deduplication.

## 8. Outbox worker concurrency

Workers acquire eligible rows with PostgreSQL row locking, typically `FOR UPDATE SKIP LOCKED`, and record a lease through:
- `lock_token`
- `locked_until`

Successful delivery:
- set `published_at`
- clear lease fields

Failed delivery:
- increment `delivery_attempt_count`
- persist a sanitized `last_error_summary`
- set `next_attempt_at`
- clear or expire the lease according to dispatcher logic

Do not persist raw credential/request dumps or unrestricted exception data in `last_error_summary`.

## 9. Audit architecture

Audit is command-oriented and append-oriented.

Do not implement generic EF interception that serializes every inserted/updated/deleted entity. That would mix business facts with projection/cache maintenance, duplicate sensitive data, and couple audit to the EF persistence shape.

A successful controlled business command normally creates:
- one main Audit Event
- one or more Audit Event Subjects

This is an application convention, not a database uniqueness rule on `command_id`.

## 10. `audit.audit_events`

Fields:
- `id uuid` PK
- `command_id uuid NULL` — correlation only, NO FK
- `command_type text NULL`
- `event_kind`
- `actor_account_id uuid` FK NOT NULL
- `occurred_at timestamptz NOT NULL`
- `reason_text text NULL`

Event kinds:
- `BUSINESS_COMMAND`
- `CORRECTION`
- `DATA_LIFECYCLE`
- `HARD_DELETE`

Indexes:
- ordinary index on `command_id`
- indexes needed for actor/time investigation may be added during relational consolidation

Do not impose `UNIQUE(command_id)`.

## 11. `audit.audit_event_subjects`

Fields:
- `audit_event_id uuid` FK
- `sequence integer`
- `subject_kind text`
- `subject_key jsonb`
- `change_kind`
- `before_row_version bigint NULL`
- `after_row_version bigint NULL`
- `change_summary jsonb NULL`

Primary key:
- `(audit_event_id, sequence)`

Constraints:
- `sequence > 0`
- `jsonb_typeof(subject_key) = 'object'`

Change kinds:
- `CREATE`
- `UPDATE`
- `SOFT_DELETE`
- `RESTORE`
- `HARD_DELETE`

`subject_kind` uses a stable namespaced audit vocabulary, for example:
- `procurement.procurement_entry`
- `sales.sale`
- `inventory.inventory_movement`
- `finance.payable`

## 12. Audit subject locator boundary

`subject_kind + subject_key` is immutable historical locator metadata, not a foreign key and not a domain relationship.

This is necessary because:
- audit must survive target soft delete and hard delete
- some audited rows may use composite keys
- audit retention is independent of target retention

The locator must not be used to implement a generic repository or arbitrary entity resolver.

Allowed uses:
- display stable historical subject identity
- investigation tooling may use explicit adapters to offer navigation when the target still exists

If the target was hard-deleted, failed navigation is a valid state.

Normal business/domain relationships remain governed by the typed-real-FK principle.

## 13. Audit payload safety

`change_summary` is an explicit audit-safe summary supplied by the owning domain when useful.

Do not automatically serialize entire database entities.

The default audit payload must not contain unrestricted:
- credentials
- authentication/session secrets
- tokens
- complete deleted rows

Sensitive business fields such as bank accounts, phone numbers, and addresses require explicit audit-safe handling if a future requirement needs historical values.

`change_summary` may be NULL.

For Hard Delete, `subject_key` should default to the minimum technical identity needed for historical traceability rather than duplicating the deleted business record.

## 14. Projection audit boundary

Rebuildable projections are not normally separate audit truth.

Examples:
- `inventory.inventory_positions`
- `finance.payable_outstanding_positions`
- `finance.receivable_outstanding_positions`

Audit the underlying facts instead, including:
- Inventory Movements
- obligation items
- adjustments
- payments
- receipts

This prevents business history from being mixed with cache/projection maintenance.

## 15. `audit.correction_links`

Links controlled correction operations to the earlier audited operations they correct.

Fields:
- `correction_audit_event_id uuid` FK
- `corrected_audit_event_id uuid` FK
- `correction_mode`

Primary key:
- `(correction_audit_event_id, corrected_audit_event_id)`

Correction modes:
- `DIRECT_AMENDMENT`
- `COMPENSATION`

Constraint:
- `correction_audit_event_id <> corrected_audit_event_id`

The composite key allows:
- one correction operation to address multiple prior audited operations
- one historical operation to be corrected by multiple later controlled operations

`BLOCK` is not persisted as a correction link because no correction transaction commits.

Audit correction links do not replace owning-domain compensation/reversal lineage.

## 16. Correction transaction requirements

Confirmed-transaction correction continues to use the owning-domain decision:
- `ALLOW_DIRECT_AMENDMENT`
- `ALLOW_WITH_COMPENSATION`
- `BLOCK`

A committed correction must still include:
- dependency assessment
- revalidation inside the transaction
- compensating facts when required
- audit
- idempotency
- atomic commit

No generic Update Any Entity, generic Undo, or Hard Delete-as-correction is introduced.

## 17. Hard Delete persistence

Do not create a generic deletion aggregate, dependency graph, or cascade engine.

Hard Delete remains a controlled entry point; the owning domain decides whether physical deletion is valid.

Typical transaction:
1. authorize highest authority
2. perform owning-domain dependency assessment
3. revalidate dependencies inside the transaction
4. append `HARD_DELETE` Audit Event and subjects
5. explicitly delete only the rows approved by the owning-domain operation
6. finalize idempotency and enqueue any required outbox messages
7. commit

Core traceability continues to use `RESTRICT / NO ACTION`; no silent cascade across core business history.

If physical deletion fails, the audit/idempotency/outbox work in the same transaction rolls back as well.

## 18. Hard Delete audit retention

Hard Delete audit normally retains:
- audit event id
- actor identity
- occurred timestamp
- command correlation where available
- subject kind
- minimal technical subject key
- reason when supplied/required by the controlled operation

It does not automatically retain a full copy of the deleted row.

Any future legal/operational requirement to retain selected deleted business attributes must define an explicit audit-safe retention policy rather than relying on generic entity serialization.

## 19. Soft Delete and Restore

Soft Delete and Restore remain owning-domain commands.

The transaction updates the owning-domain lifecycle fields and appends the applicable audit facts, idempotency state, and outbox messages atomically.

No central `deleted_entities` table becomes the lifecycle authority for all modules.

## 20. System-level concurrency matrix

Concurrency boundaries:
- normal Aggregate mutation → aggregate `row_version`
- Inventory current quantity → `inventory.inventory_positions.row_version`
- Finance monetary writes → applicable Outstanding Position `row_version`
- duplicate command → `system.command_executions.command_id` PK
- competing Outbox worker → PostgreSQL row lock + lease
- Audit → append-oriented; no mutable audit `row_version`

Do not introduce:
- `system.global_locks`
- global entity-version registry
- Audit `row_version`
- Outbox `row_version`

Concurrency belongs to the row that owns the invariant.

## 21. Atomic command write pattern

The v0.1 persistence pattern is:

`CommandExecution(IN_PROGRESS)`
→ owning-domain validation
→ business facts
→ required cross-domain atomic facts
→ Audit Event + Subjects
→ Outbox rows
→ `CommandExecution(SUCCEEDED)`
→ COMMIT

Only after commit does an Outbox dispatcher perform external/post-commit work.

## 22. Part 6 table set

`system`:
- `system.accounts`
- `system.command_executions`
- `system.outbox_messages`

`audit`:
- `audit.audit_events`
- `audit.audit_event_subjects`
- `audit.correction_links`

No additional generic system/deletion/locking tables are required in v0.1.

## 23. Related ADR

ADR-005 records the Audit reference boundary:
- typed real FKs remain mandatory for normal domain relationships
- Audit historical subject locators are intentionally non-FK immutable metadata
- Audit locators must not become a generic domain-navigation mechanism

## 24. Completion state

With Part 6 confirmed, PostgreSQL Schema v0.1 Parts 1–6 cover the planned module persistence baseline.

Next architecture step:
- full relational-model consolidation
- then EF Core mapping architecture
- then REST/API architecture
