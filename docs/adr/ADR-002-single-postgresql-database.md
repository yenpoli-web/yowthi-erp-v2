# ADR-002 — Single PostgreSQL Database

Status: Accepted baseline

## Decision

Use one PostgreSQL 18 database with module-separated schemas.

## Rationale

Critical ERP writes require ACID atomicity across Procurement, Processing, Inventory, Sales, Labor, and Finance.

## Consequences

- cross-schema writes participate in one DB transaction
- schemas preserve ownership boundaries
- no eventual-consistency gap is tolerated for critical core business facts
