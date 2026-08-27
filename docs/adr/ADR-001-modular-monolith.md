# ADR-001 — Modular Monolith

Status: Accepted baseline

## Decision

YowThi ERP V2 uses a Modular Monolith.

Modules are domain-oriented and preserve explicit boundaries.
The application remains one deployable backend in v0.1.

## Rationale

Core commands require atomic transactions across modules, for example:
- Procurement + Inventory + Finance
- Sales + Inventory + Finance
- Labor + Finance

A Modular Monolith preserves transaction simplicity without collapsing Domain Boundaries.

## Consequences

- no microservice distribution in v0.1
- domain modules retain independent models
- cross-domain references use identities
- business commands, not generic CRUD, coordinate transactions
