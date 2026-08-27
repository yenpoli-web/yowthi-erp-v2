# YowThi ERP V2

This is the formal design checkpoint for YowThi ERP V2.

> IMPORTANT: `C:\yowthi-erp` is the protected Legacy ERP. It is read-only reference / migration / validation material only. It must never be modified, deleted, moved, reset, overwritten, or treated as the source schema for ERP V2.

## Current phase

Business Discovery → Ubiquitous Language → Domain Map → Aggregate Boundary → Command Contracts → Persistence Architecture → PostgreSQL Schema Design.

The recovery baseline is:

- `docs/09-current-design-checkpoint.md`

## Information labels

Documents distinguish:

- `CURRENT BUSINESS FACT`
- `ERP CONTROL GOVERNANCE`
- `TECHNICAL BASELINE`
- `DECISION / v0.1`
- `TO VERIFY`
- `DEFERRED`
- `LEGACY REFERENCE`

Business rules must come from real YowThi operations. AI/developers must not invent business rules merely for technical convenience.
