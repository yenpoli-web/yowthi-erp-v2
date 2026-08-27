# YowThi ERP V2

YowThi ERP V2 is the clean-slate replacement architecture and implementation for the YowThi ERP.

> IMPORTANT: `C:\yowthi-erp` is the protected Legacy ERP. It is read-only reference / migration / validation material only. It must never be modified, deleted, moved, reset, overwritten, or treated as the source schema for ERP V2.

## Current phase

Implementation P2 / M1 is complete — `system + party`, 8 of 55 formal relations. Next: P2 / M2 relational mapping — `infrastructure + product + processing_config`, cumulative 20 of 55 formal relations.

P0 is complete:
- .NET 10 solution/project scaffold
- architecture dependency guard
- React + TypeScript + Vite web scaffold
- React Router + TanStack Query wiring
- locked pnpm dependency graph
- .NET and web CI gates

P1 is complete:
- Domain/Application technical primitives
- EF Core / Npgsql persistence foundation
- PostgreSQL 18 provider configuration
- command transaction / idempotency / Outbox / Audit technical contracts
- one-shot explicit READ COMMITTED transaction runner

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

The implementation baseline contains:

```text
src/
  YowThi.Erp.Domain
  YowThi.Erp.Application
  YowThi.Erp.Infrastructure
  YowThi.Erp.Infrastructure.Migrations
  YowThi.Erp.Api
  YowThi.Erp.Web

tests/
  YowThi.Erp.Domain.Tests
  YowThi.Erp.ArchitectureTests
  YowThi.Erp.Api.ContractTests
  YowThi.Erp.IntegrationTests
```

Project dependency direction is guarded by architecture tests. The React web application is a separate Vite application and communicates with the backend only through the HTTP API contract.

## Shared technical foundation

P1 contains only cross-cutting technical foundations and does not encode YowThi Business Rules.

Application/Domain foundation:

- UUID v7 technical ID generation
- non-empty CommandId and persistent actor-account identity semantics
- actor-context contract
- locale-neutral application error/result primitives
- application command/handler/executor contracts
- stable command-type and SHA-256 request-hash value semantics
- opaque validated JSON payload primitives for technical persistence contracts
- CommandExecution acquire/replay/conflict store boundary
- Outbox writer and command-oriented Audit writer draft contracts
- explicit commit/rollback command-transaction decision contract
- cancellation-aware async signatures

Infrastructure foundation:

- EF Core 10.0.11
- Npgsql EF provider 10.0.3
- PostgreSQL 18 provider target
- one write `ErpDbContext`
- explicit `system.__ef_migrations_history` configuration
- `row_version` SaveChanges interceptor foundation using an explicit `long` marker, not PostgreSQL `xmin`
- runtime DI registration using the same provider configuration as design-time tooling
- separate migrations-project `IDesignTimeDbContextFactory<ErpDbContext>`
- SHA-256 canonical command-payload hasher
- one-shot explicit READ COMMITTED transaction runner; rollback/exception requires a fresh service scope and fresh `ErpDbContext`

## Relational mapping progress

P2 / M1 maps exactly eight formal relations:

```text
system.accounts
system.command_executions
system.outbox_messages

party.suppliers
party.farmers
party.employees
party.customers
party.outsourced_vendors
```

M1 mapping guards include:

- explicit schema/table/column names
- UUID IDs with `ValueGeneratedNever()`
- explicit `row_version bigint` concurrency for mutable M1 owners only
- real `system.accounts` actor FKs with `RESTRICT`
- bilingual-name and lifecycle-pair CHECK constraints for Party masters
- CommandExecution SHA-256/status/execution-state CHECK constraints
- Outbox `command_id` correlation without an FK
- no Party name/phone/bank uniqueness that has not been confirmed
- no global soft-delete query filters
- no PostgreSQL `xmin` concurrency

Architecture tests inspect the EF design-time model so relational metadata such as CHECK constraints is validated before migration generation.

No formal migration has been generated. `InitialV01` remains blocked until all M1–M7 mappings reach 55 of 55 relations.

## Local .NET validation

```powershell
dotnet tool restore
dotnet restore YowThi.Erp.slnx
dotnet build YowThi.Erp.slnx
dotnet test YowThi.Erp.slnx
```

The repository pins .NET SDK `10.0.400` in `global.json` and uses Microsoft Testing Platform through xUnit v3.

## React web baseline

Frontend baseline:

- Node 24 LTS
- pnpm 11.23.0
- React 19.2.8
- Vite 8.2.2
- React Router 8.3.0
- TanStack Query 5.102.7
- TypeScript 6.0.3

TypeScript remains on the latest stable compiler version currently supported by the selected `typescript-eslint` toolchain rather than moving to an unsupported parser/compiler combination.

The pnpm lockfile is committed. pnpm 11's default 24-hour minimum release-age protection remains enabled; the workspace file contains only the exact version exceptions generated for the selected TanStack Query release.

Local validation:

```powershell
cd src/YowThi.Erp.Web
pnpm install --frozen-lockfile
pnpm lint
pnpm typecheck
pnpm build
```

The P0 web shell wires React Router and TanStack Query only. It does not contain mock ERP entities, fake Business Rules, or a temporary replacement API model.

Docker Desktop / PostgreSQL are not required for P2 relational model mapping and metadata tests. They become required at the Build Plan P5 gate when `InitialV01` is first applied to PostgreSQL 18 and persistence integration/concurrency tests begin.

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
