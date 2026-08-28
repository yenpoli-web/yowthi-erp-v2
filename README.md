# YowThi ERP V2

YowThi ERP V2 is the clean-slate replacement architecture and implementation for the YowThi ERP.

> IMPORTANT: `C:\yowthi-erp` is the protected Legacy ERP. It is read-only reference / migration / validation material only. It must never be modified, deleted, moved, reset, overwritten, or treated as the source schema for ERP V2.

## Current phase

Implementation P4 is complete: the formal `InitialV01` migration has been generated, statically reviewed, drift-checked, and validated. Next: **P5 - PostgreSQL 18 persistence acceptance**.

P0 is complete:
- .NET 10 solution/project scaffold
- architecture dependency guard
- React + TypeScript + Vite web scaffold
- React Router + TanStack Query wiring
- locked pnpm dependency graph
- self-hosted validation gates; GitHub-hosted automatic CI is disabled by cost governance

The formal architecture/recovery baseline now includes:

- Business Discovery / Domain Model / Command Contracts
- PostgreSQL relational model consolidation
- EF Core Mapping Architecture
- REST/API Architecture
- Implementation Sequencing / Build Plan
- GitHub Cost Governance
- AuthN/AuthZ Implementation Architecture

The recovery baseline is:

- `docs/09-current-design-checkpoint.md`
- `docs/10-relational-model-consolidation-v0.1.md`
- `docs/11-ef-core-mapping-architecture-v0.1.md`
- `docs/12-rest-api-architecture-v0.1.md`
- `docs/13-implementation-sequencing-build-plan-v0.1.md`
- `docs/14-github-cost-governance-v0.1.md`
- `docs/15-authn-authz-implementation-architecture-v0.1.md`

For the auth-specific `system.accounts` identity binding only, `docs/15` is the later confirmed correction to older wording in `docs/10` / PostgreSQL Schema Part 6 that external identity mapping was not yet defined. Relation count remains 55.

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

P2 mappings were implemented in dependency order and validated through EF design-time metadata tests.

Completed:

```text
M1  system + party                                  8 / 55
M2  infrastructure + product + processing_config  20 / 55
M3  procurement + processing                      25 / 55
M4  outsourced + sales + inventory                35 / 55
M5  sales_handling + labor                        41 / 55
M6  finance                                       52 / 55
M7  audit                                         55 / 55
```

All 55 formal relations are represented in the EF model.

P3.5 adds no relation 56+. It revises the existing `system.accounts` relation with an optional, paired, unique external OIDC identity mapping:

```text
identity_issuer  text NULL
identity_subject text NULL
```

The pair is either both NULL or both present/nonblank. When present, `(identity_issuer, identity_subject)` is unique.

## API technical shell

P3 establishes transport/hosting infrastructure only; it does not implement Business Commands.

Current API shell includes:
- Minimal API composition root
- authenticated-by-default `/api/v1` route-group helper
- confirmed v1 module route vocabulary
- first-party Problem Details and stable error-code helpers
- first-party OpenAPI registration; Development-only OpenAPI endpoint
- first-party validation registration
- camelCase JSON with unknown write properties rejected
- `Idempotency-Key` transport filter that parses a UUID into the existing Application `CommandId` feature without acquiring/replaying persistence itself
- `Accept-Language` resolver for `zh-TW` / `th-TH` with deployment-configured default locale
- operation/capability policy-name constants
- rate-limiter infrastructure with stable 429 Problem Details handling; numeric thresholds remain deployment controls
- optional finite request-body limit configuration hook
- minimal anonymous `/health/live` probe
- API contract tests for routing/auth metadata, OpenAPI exposure, JSON strictness, locale resolution, and Idempotency-Key transport behavior

## AuthN/AuthZ hard-gate baseline

P3.5 formally confirms:
- provider-neutral external OpenID Connect authentication
- Authorization Code + PKCE for interactive browser login
- ASP.NET Core encrypted Cookie session
- `/api/v1` returns `401` for unauthenticated API requests rather than redirecting business requests
- stable authenticated identity = validated `(issuer, subject)`
- exact external identity maps server-side to `system.accounts`
- ERP accounts are pre-provisioned; first login does not auto-create accounts
- account must exist and remain `active = true` for ActorContext resolution
- clients never select actor account IDs
- authorization uses explicit capability policies such as `sales.confirm`, `finance.pay`, `inventory.adjust`, `data-protection.hard-delete`
- v0.1 capability grants are deployment-configured by persistent Account UUID
- no `Admin` / `Manager` / `SuperAdmin` business-role invention
- no ERP password storage
- no roles/permissions/session/password/refresh-token relations
- React does not store access or refresh tokens
- cookie-authenticated unsafe browser writes require antiforgery protection; confirmed request header is `X-CSRF-TOKEN`
- test authentication remains test-only and cannot become a staging/production bypass
- Tailscale remains network transport only, not ERP authentication

See `docs/15-authn-authz-implementation-architecture-v0.1.md`.

## InitialV01 migration baseline

P4 is complete. Formal migration:

```text
src/YowThi.Erp.Infrastructure.Migrations/Migrations/20260828033151_InitialV01.cs
```

P4 acceptance:
- 55 `CreateTable` operations
- 14 PostgreSQL schemas
- no pending EF model changes after migration generation
- explicit lower snake_case database index names; convention/truncation fallback is guarded by ArchitectureTests
- P3.5 `system.accounts` OIDC identity columns, constraints, and partial unique index are included
- Inventory Position `NULLS NOT DISTINCT` identity is included
- Finance Outstanding formula is retained without inventing a permanent `outstanding >= 0` rule
- Audit/Outbox CommandId reference and retention boundaries are preserved
- self-hosted restore/build/test passed

`InitialV01` has **not** yet been applied to a real PostgreSQL database.

P5 now requires Docker Desktop / PostgreSQL 18:

```text
approved InitialV01
-> Docker Desktop ON
-> PostgreSQL 18 clean database
-> apply InitialV01
-> schema / migration-history introspection
-> PostgreSQL integration and concurrency acceptance
```
## Local .NET validation

```powershell
dotnet tool restore
dotnet restore YowThi.Erp.slnx
dotnet build YowThi.Erp.slnx
dotnet test YowThi.Erp.slnx
```

The repository pins .NET SDK `10.0.400` in `global.json` and uses Microsoft Testing Platform through xUnit v3. Candidate branches matching `m*-validation` use the Windows self-hosted runner automatically.

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
