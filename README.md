# YowThi ERP V2

YowThi ERP V2 is the clean-slate replacement architecture and implementation for the YowThi ERP.

> IMPORTANT: `C:\yowthi-erp` is the protected Legacy ERP. It is read-only reference / migration / validation material only. It must never be modified, deleted, moved, reset, overwritten, or treated as the source schema for ERP V2.

## Current phase

Implementation P0 — .NET solution scaffolding.

The architecture and implementation-order baseline is complete through:

- Business Discovery / Domain Model / Command Contracts
- PostgreSQL relational model consolidation
- EF Core Mapping Architecture
- REST/API Architecture
- Implementation Sequencing / Build Plan

The recovery baseline remains:

- `docs/09-current-design-checkpoint.md`
- `docs/13-implementation-sequencing-build-plan-v0.1.md`

## .NET solution

The first implementation baseline contains:

```text
src/
  YowThi.Erp.Domain
  YowThi.Erp.Application
  YowThi.Erp.Infrastructure
  YowThi.Erp.Infrastructure.Migrations
  YowThi.Erp.Api

tests/
  YowThi.Erp.Domain.Tests
  YowThi.Erp.ArchitectureTests
  YowThi.Erp.Api.ContractTests
  YowThi.Erp.IntegrationTests
```

Project dependency direction is guarded by architecture tests.

## Local .NET validation

```powershell
dotnet tool restore
dotnet restore YowThi.Erp.slnx
dotnet build YowThi.Erp.slnx
dotnet test YowThi.Erp.slnx
```

The repository pins .NET SDK `10.0.400` in `global.json` and uses Microsoft Testing Platform through xUnit v3.

Docker Desktop / PostgreSQL are not required for this P0 scaffold. They become required at the Build Plan P5 gate when `InitialV01` is first applied to PostgreSQL 18 and persistence integration/concurrency tests begin.

## Information labels

Design documents distinguish:

- `CURRENT BUSINESS FACT`
- `ERP CONTROL GOVERNANCE`
- `TECHNICAL BASELINE`
- `DECISION / v0.1`
- `TO VERIFY`
- `DEFERRED`
- `LEGACY REFERENCE`

Business Rules must come from real YowThi operations. Implementation must not invent Business Rules for technical convenience.
