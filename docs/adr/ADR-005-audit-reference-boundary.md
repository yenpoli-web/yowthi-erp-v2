# ADR-005 — Audit Reference Boundary

Status: **Accepted / v0.1**

## Context

YowThi ERP V2 requires both strict relational traceability and audit history that survives entity lifecycle changes, including Hard Delete.

The persistence baseline already requires typed real foreign keys for normal domain relationships and rejects unconstrained polymorphic `(type,id)` foreign keys.

Audit has a different requirement: an audit record must remain valid even when its subject is inactive, soft-deleted, physically deleted, or represented by a composite key.

## Decision

### Normal domain relationships

Normal business and traceability relationships must use typed real foreign keys with explicit integrity constraints.

Do not use generic `(type,id)` references as a substitute for domain modeling.

### Audit historical references

`audit.audit_event_subjects` uses:
- stable `subject_kind`
- immutable `subject_key jsonb`

These values are historical locator metadata only.

They intentionally do **not** form a foreign key to the audited target.

This allows Audit to:
- survive target Soft Delete and Hard Delete
- represent subjects with composite keys
- retain history independently of the target row lifecycle

### Navigation boundary

Audit locator metadata must not become a generic domain-navigation or repository mechanism.

Do not implement a generic resolver that accepts `subject_kind + subject_key` and loads arbitrary domain entities.

Investigation/UI tooling may use explicit subject-specific adapters to offer navigation when the target still exists. Failure to navigate after Hard Delete is a valid state.

### Payload boundary

Audit locators and change summaries are not database-row backups.

Hard Delete audit should retain minimum technical identity and controlled audit-safe metadata, not automatically serialize a complete deleted row.

## Consequences

- Relational integrity remains explicit and typed inside business domains.
- Audit remains durable across physical deletion without weakening core FK rules.
- Audit cannot be used as a hidden generic entity framework.
- Audit payloads require explicit owning-domain decisions when sensitive before/after data is needed.
- Audit and target retention lifecycles remain decoupled.
