# ADR-004 — Inventory Ledger + Position

Status: Accepted baseline

## Decision

Inventory Movement is the append-oriented source of truth.
Inventory Position is a transactionally maintained, rebuildable current-state projection and concurrency boundary.

## Rationale

ERP must retain complete movement history while supporting fast current-balance and concurrency checks.

## Consequences

- never directly rewrite balance without Movement
- Transfer = TRANSFER_OUT + TRANSFER_IN
- Adjustment = explicit ADJUSTMENT movement
- Batch closing = explicit BATCH_RECONCILIATION movements
- negative balance is structurally allowed because Final Packaging can legally create negative source material
- command policy decides whether a specific operation may create negative balance
