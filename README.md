# YowThi ERP V2

YowThi ERP V2 is the clean-slate replacement architecture and implementation for the YowThi ERP.

> IMPORTANT: `C:\yowthi-erp` is the protected Legacy ERP. It is read-only reference / migration / validation material only. It must never be modified, deleted, moved, reset, overwritten, or treated as the source schema for ERP V2.

## Current phase

Implementation P2 / M6 is complete: 52 of 55 formal relations are mapped. Next: P2 / M7 — `audit`, cumulative 55 of 55 relations.

P0 is complete:
- .NET 10 solution/project scaffold
- architecture dependency guard
- React + TypeScript + Vite web scaffold
- React Router + TanStack Query wiring
- locked pnpm dependency graph
- self-hosted validation gates; GitHub-hosted automatic CI is disabled by cost governance

The architecture and implementation-order baseline is complete through:

- Business Discovery / Domain Model / Command Contracts
- PostgreSQL relational model consolidation
- EF Core Mapping Architecture
- REST/API Architecture
- Implementation Sequencing / Build Plan

The recovery baseline remains:

- `docs/09-current-design-checkpoint.md`
- `docs/13-implementation-sequencing-build-plan-v0.1.md`
- `docs/14-github-cost-governance-v0.1.md`

## GitHub cost governance

GitHub usage must remain on a no-unapproved-cost path. Do not depend on services that can continue into paid metered usage after a free quota is exhausted.

Routine CI on GitHub-hosted runners is disabled. Backend candidate branches matching `m*-validation` validate automatically only on the YowThi ERP V2 Windows self-hosted runner (`self-hosted`, `yowthi-erp-v2`); `main` does not trigger that workflow.

Do not reintroduce `ubuntu-latest`, `windows-latest`, `macos-latest`, paid/larger runners, Codespaces, or other metered GitHub infrastructure without first verifying zero-cost behavior and explicitly revising the governance decision.

See `docs/14-github-cost-governance-v0.1.md`.

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

P2 mappings are implemented in dependency order and validated through EF design-time metadata tests.

Completed:

```text
M1  system + party                                  8 / 55
M2  infrastructure + product + processing_config  20 / 55
M3  procurement + processing                      25 / 55
M4  outsourced + sales + inventory                35 / 55
M5  sales_handling + labor                        41 / 55
M6  finance                                       52 / 55
```

M6 includes:
- typed Payables with confirmed partial business uniques and `(id, payable_kind)` structural key
- flattened Payable Obligation Items with real source FKs and Payable-kind compatibility
- original obligation amounts permitting zero (`>= 0`) per the consolidated baseline
- Company Pickup per-entry transport basis plus M:N obligation lineage without inventing grouping, payee, or rounding rules
- confirmed Supplier quality/weight deduction adjustment as a negative append fact
- positive partial Payment / Receipt settlement facts
- one Receivable per confirmed Sales and same-Sale Receivable Obligation Item composite integrity
- Payable / Receivable Outstanding transactional projections with explicit row-version monetary concurrency ownership
- outstanding row formula without a permanent `outstanding >= 0` CHECK while FIN-001/002/004 remain unresolved

Only formally confirmed closed vocabularies are mapped as CLR enums to PostgreSQL text. Cross-row source/payable semantic alignment and FIN-001–FIN-009 behavior remain owning-command/business-gap responsibilities rather than invented relational rules.

No `InitialV01` migration is generated until all M1–M7 mappings reach 55 / 55.

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
