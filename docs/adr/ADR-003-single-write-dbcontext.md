# ADR-003 — Single Write DbContext

Status: Accepted baseline

## Decision

Use one write `ErpDbContext` in v0.1.

EF mappings remain separated by module.

## Rationale

Commands such as ConfirmSales and ConfirmProcurementEntry must atomically write multiple module schemas.

## Consequences

- one write Unit of Work
- module boundaries exist in domain/application/mapping structure, not by multiplying DbContexts
- read queries may use EF projections, Dapper, or native SQL where useful
