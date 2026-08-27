# Implementation Sequencing / Build Plan v0.1 — YowThi ERP V2

Status: **DECISION / v0.1**
Revision basis: Implementation Sequencing Revision 0.2 + Final Build Readiness Review, formally confirmed.

## 1. Purpose and precedence

This document defines the implementation order for YowThi ERP V2 after completion of the v0.1 architecture chain through REST/API.

It does not redefine Business Facts, Business Rules, relational shape, EF Core mapping architecture, or REST contracts.

Implementation precedence:
- Business Facts / Business Rules: Business Discovery, Command Contracts, Gap Register, owning-domain documents
- relational implementation: `docs/10-relational-model-consolidation-v0.1.md`
- EF Core/Npgsql implementation architecture: `docs/11-ef-core-mapping-architecture-v0.1.md`
- REST/API implementation architecture: `docs/12-rest-api-architecture-v0.1.md`
- implementation order / readiness gates: this document

If implementation convenience conflicts with those baselines, implementation must change. Coding must not silently redefine the architecture.

## 2. Starting repository state

At formal confirmation of this plan, the repository is still architecture/document-first:
- no `src/` implementation tree
- no .NET solution
- no backend project
- no frontend project
- no EF migration history
- no PostgreSQL runtime dependency for existing work

Implementation therefore begins from a clean scaffold rather than adapting an unfinished application.

Legacy rule remains unchanged:
- `C:\yowthi-erp` is read-only Legacy ERP reference
- never modify, move, reset, overwrite, or derive ERP V2 architecture from the Legacy schema

## 3. Overall implementation strategy

Use:

**complete relational model implementation first, then business vertical slices**.

Do not create a temporary simplified production schema for early features.
Do not generate a formal `InitialV01` migration before all 55 planned relations are represented in the EF model and model metadata tests pass.

High-level sequence:

```text
P0  Repository / solution scaffolding
P1  Shared technical foundation
P2  Domain + EF mapping for all 55 relations
P3  API technical shell
P3.5 AuthN/AuthZ implementation architecture hard gate
P4  InitialV01 migration generation + static review
P5  PostgreSQL 18 persistence acceptance
P6  Business vertical slices
P7  React UI vertical slices
P8  CI / production hardening
```

## 4. Docker Desktop activation point

Docker Desktop is not required for:
- solution scaffolding
- Domain/Application code
- EF entity/configuration code
- EF model metadata tests
- API contract/OpenAPI tests that do not require PostgreSQL
- `InitialV01` migration generation
- static migration review

Docker Desktop becomes required locally when the approved `InitialV01` is first applied to a real PostgreSQL 18 instance and PostgreSQL-specific integration/concurrency testing begins.

Formal activation boundary:

```text
InitialV01 generated
→ static review PASS
→ no pending model changes
→ Docker Desktop ON
→ PostgreSQL 18 up
→ apply InitialV01
→ integration/concurrency acceptance
```

CI PostgreSQL execution is independent of the developer workstation's Docker Desktop and uses CI runner container/service infrastructure.

## 5. P0 — .NET solution scaffolding

Initial .NET structure:

```text
YowThi.Erp.slnx
global.json
Directory.Build.props
Directory.Packages.props
.editorconfig
.config/
└─ dotnet-tools.json

src/
├─ YowThi.Erp.Domain/
├─ YowThi.Erp.Application/
├─ YowThi.Erp.Infrastructure/
├─ YowThi.Erp.Infrastructure.Migrations/
└─ YowThi.Erp.Api/

tests/
├─ YowThi.Erp.Domain.Tests/
├─ YowThi.Erp.ArchitectureTests/
├─ YowThi.Erp.Api.ContractTests/
└─ YowThi.Erp.IntegrationTests/
```

The solution uses the current .NET 10 toolchain baseline.
`global.json` pins the approved installed .NET 10 SDK feature band when scaffolding occurs.

Use repository-local .NET tool management for EF CLI rather than depending on a globally installed developer tool.

## 6. Project dependency direction

Allowed dependency direction:

```text
Domain
  ↑
Application
  ↑
Infrastructure
  ↑
Api

Infrastructure.Migrations
  → Infrastructure
```

Rules:
- Domain does not reference EF Core, Npgsql, ASP.NET Core, or Infrastructure
- Application references Domain, not EF/Npgsql/HttpContext
- Infrastructure implements persistence/application contracts
- Migrations project references Infrastructure but is not a runtime Business API dependency
- API references Application/Infrastructure composition but does not own relational rules
- API does not reference the Migrations project as a runtime migration mechanism

Architecture tests enforce forbidden dependency directions from the first implementation phase.

## 7. P0 — React scaffold

Frontend structure is created as a separate focused commit after the .NET scaffold.

Baseline:
- React
- TypeScript
- Vite
- React Router
- TanStack Query
- pnpm
- Node 24 LTS baseline

Frontend package versions and the exact approved pnpm patch version are locked at actual scaffolding time using the repository lockfile/package-manager metadata.

The frontend is not allowed to define a parallel business/domain persistence model.
TanStack Query manages server state/query invalidation; it does not become a second ERP aggregate system in the browser.

## 8. Central package/version control

Use:
- `Directory.Packages.props` for central NuGet package management
- repository `global.json` for .NET SDK reproducibility
- `.config/dotnet-tools.json` for repository-local `dotnet ef`
- `pnpm-lock.yaml` and `packageManager` metadata for frontend reproducibility

Do not install speculative dependencies during scaffolding.

Domain should start without EF/Npgsql dependencies.
Application should not depend on ASP.NET transport types.
Infrastructure adds EF Core 10 / Npgsql where implementation begins.
Migrations adds EF design tooling in the dedicated migration project.

Dapper is not installed preemptively; add it only when an approved query/native-SQL use case requires it.
MediatR is not part of the v0.1 baseline.

## 9. P0 acceptance gate

The first implementation state must be database-free and green.

Required local/CI commands conceptually include:

```text
dotnet restore
dotnet build
dotnet test

pnpm install --frozen-lockfile
pnpm typecheck
pnpm build
```

Integration test project may exist, but tests that require PostgreSQL are not part of the default P0 acceptance until P5.

The first code commit must not depend on Docker Desktop or an available database.

## 10. Initial commit split

Recommended first commits:

```text
01 build: scaffold .NET solution
02 build: scaffold React web app
03 feat: add application and persistence technical foundation
```

A commit should compile/test and have one clear purpose.
Do not combine solution scaffold, 55-table persistence, API endpoints, and UI into one implementation commit.

## 11. P1 — Shared technical foundation

Build technical primitives that are required broadly but do not encode YowThi Business Rules.

Examples:
- UUID v7 creation/use
- CommandId value semantics
- time abstraction / `TimeProvider` use
- actor-context abstraction
- application result/error primitives
- command executor contract
- row-version technical marker/concurrency support
- command-execution store contract
- audit-writer contract
- outbox-writer contract
- cancellation propagation

Infrastructure foundation may include:
- `ErpDbContext` skeleton
- row-version SaveChanges interceptor
- transaction executor shell
- command-execution persistence shell
- outbox technical shell
- audit technical writer shell

Technical shells must not invent business audit/event meaning before owning commands exist.

## 12. Forbidden generic abstractions

The following are architecture regressions and should not be introduced:

```text
IRepository<TEntity>
CrudService<TEntity>
EntityService<TEntity>
GenericSoftDelete(...)
GenericHardDelete(type, id)
GenericCorrect(type, id, patch)
GenericCommandEndpoint
```

Commands and persistence capabilities remain domain/workflow-oriented.

## 13. Business vs technical persistence ownership

Business/domain facts such as Procurement Batch, Sales, Inventory Movement, Payable, Payment, etc. belong to the appropriate Domain/Application module and are mapped by Infrastructure.

Pure technical persistence records such as CommandExecution and OutboxMessage do not need to be forced into the Business Domain merely because EF maps them.

Application should depend on technical persistence abstractions such as:
- command execution store
- outbox writer/store
- audit writer

Infrastructure owns their provider-specific implementations.

## 14. P2 — Full 55-relation EF model

Implement all 55 planned relations before formal migration generation.

Use seven mapping batches.
Each batch includes:
- CLR model/entity representation appropriate to ownership
- explicit Fluent API mapping
- schema/table/column names
- PK/alternate keys
- FKs/delete behavior
- unique/index rules
- CHECK constraints
- row-version mapping when applicable
- UUID behavior
- EF model metadata tests
- direct cross-check against `docs/10`

Do not generate a formal migration after each batch.

## 15. Mapping Batch M1 — system + party

Relations: **8 / 55**

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

Purpose:
- technical actor/idempotency/outbox anchors
- core party FK targets required by later modules

Special checks:
- `system.accounts` remains minimal actor identity anchor only
- no credential/role/session persistence is invented here
- CommandExecution request hash/status/result shape matches baseline
- Audit/Outbox CommandId retention boundaries remain decoupled from later audit

## 16. Mapping Batch M2 — infrastructure + product + processing configuration

Relations: **12**, cumulative **20 / 55**

```text
infrastructure.containers
infrastructure.warehouses
infrastructure.storage_locations
product.procurement_products
product.sales_product_groups
product.sales_products
processing_config.processing_routes
processing_config.processing_route_versions
processing_config.route_input_configs
processing_config.process_materials
processing_config.processing_modules
processing_config.processing_module_outputs
```

Special checks:
- bilingual name constraints
- storage-location relationships
- Sales Product pricing/weight shape
- one ACTIVE Route Version partial unique index
- high-value alternate/composite keys
- same-Route-Version Process Material membership
- no invented optional-code business uniqueness

## 17. Mapping Batch M3 — procurement + processing

Relations: **5**, cumulative **25 / 55**

```text
procurement.procurement_batches
procurement.procurement_entries
processing.processing_executions
processing.processing_execution_inputs
processing.processing_execution_outputs
```

Special checks:
- Procurement Batch date+product business identity
- typed Supplier/Farmer source CHECK
- optional route/version binding shape
- processing execution mode/source shape
- one input per execution
- execution-output identity
- measurement decimal checks
- Final Packaging row-local source-consumption formula

Do not add the deliberately rejected nullable composite principal FK from Processing Execution to Procurement Batch route version.
Batch-bound Route Version equality remains owning-command transactional validation.

## 18. Mapping Batch M4 — outsourced + sales + inventory

Relations: **10**, cumulative **35 / 55**

```text
outsourced.outsourced_supply_batches
outsourced.outsourced_supply_details
sales.sales
sales.sales_details
sales.sales_allocation_revisions
sales.sales_allocation_revision_items
sales.sales_allocations
inventory.inventory_operations
inventory.inventory_movements
inventory.inventory_positions
```

This is a high-risk mapping batch.

Special checks:
- Outsourced Batch identity = Supply Date + Outsourced Vendor
- Sales Detail same-Sale structural integrity
- immutable Allocation Revision/Items
- pointer-only current `sales_allocations`
- typed source batches in Revision Items
- immutable movement ledger lineage
- `SALES_ALLOCATION_ADJUSTMENT` signed movement semantics
- full Inventory Position identity
- PostgreSQL `UNIQUE NULLS NOT DISTINCT`

## 19. Mapping Batch M5 — sales handling + labor

Relations: **6**, cumulative **41 / 55**

```text
sales_handling.sales_packaging_items
sales_handling.sales_packaging_work_records
labor.employee_daily_wages
labor.processing_wage_components
labor.processing_wage_component_sources
labor.sales_packaging_wage_components
```

Special checks:
- do not create a business unique constraint for HANDLING-002
- Daily Wage identity = Work Date + Employee
- one Processing Execution Output cannot be included in multiple confirmed wage components
- one Work Record enters only one Daily Wage
- wage calculation row formulas

## 20. Mapping Batch M6 — finance

Relations: **11**, cumulative **52 / 55**

```text
finance.payables
finance.payable_obligation_items
finance.company_pickup_transport_bases
finance.company_pickup_transport_obligation_basis_items
finance.payable_adjustments
finance.payments
finance.receivables
finance.receivable_obligation_items
finance.receipts
finance.payable_outstanding_positions
finance.receivable_outstanding_positions
```

Special checks:
- flat typed-FK Payable obligation model
- Payable `(id, payable_kind)` structural alternate key
- original obligation amount permits zero (`>= 0`)
- settlement amounts remain positive
- Outstanding row formula/concurrency token
- do not add permanent `outstanding >= 0` DB rule while FIN-001/002/004 remain unresolved

Company Pickup Transport implementation at this stage is restricted to the approved persistence shape:
- per-entry transport basis
- obligation typed snapshot fields
- basis lineage relation

Do not invent:
- Driver Master
- Transport Rate Master
- Transport Charge aggregate
- automatic transport Payable grouping
- payee semantics

FIN-007/008/009 remain business gaps before affected functionality goes live.

## 21. Mapping Batch M7 — audit

Relations: **3**, cumulative **55 / 55**

```text
audit.audit_events
audit.audit_event_subjects
audit.correction_links
```

Special checks:
- append-oriented command audit
- `subject_kind + subject_key` is historical JSON locator metadata, intentionally non-FK
- Audit locator must not become generic Domain resolver
- Audit `command_id` and Outbox `command_id` are correlation snapshots, no FK to CommandExecution
- correction-links structure matches ADR-005 / correction framework

## 22. Mapping-batch commit sequence

Recommended focused commits:

```text
04 feat: map system and party
05 feat: map infrastructure product and processing configuration
06 feat: map procurement and processing
07 feat: map outsourced sales and inventory
08 feat: map sales handling and labor
09 feat: map finance
10 feat: map audit
```

Each commit must build and pass the relevant metadata tests before the next mapping batch begins.

## 23. Full EF model acceptance gate

Before `InitialV01` generation, ArchitectureTests must verify at minimum:
- expected relation count = 55
- expected PostgreSQL schema count = 14
- no unexpected mapped tables
- explicit schema/table identity
- expected PK/alternate/composite keys
- expected FKs
- no unintended Cascade on core relationships
- all applicable `*_by_account_id` FKs target `system.accounts`
- expected row-version properties are concurrency tokens
- no Npgsql `xmin` concurrency substitution
- Audit/Outbox append-oriented rows are not turned into row-version owners unless baseline says otherwise
- no global soft-delete query filter
- Inventory Position uniqueness uses NULLS NOT DISTINCT semantics
- Audit historical locator has no generic Domain FK

## 24. P3 — API technical shell

After or alongside model completion, build the transport shell without prematurely implementing every business command.

Include:
- ASP.NET Core 10 Minimal API host
- `/api/v1` route group
- module endpoint-registration structure
- Problem Details infrastructure
- first-party validation
- first-party OpenAPI
- locale resolution
- Idempotency-Key parsing/transport infrastructure
- explicit authorization policy naming infrastructure
- rate-limit configuration structure
- request-body-size configuration structure
- API contract tests

Do not put Business Rule calculations or database transaction ownership in endpoint code.

## 25. P3 API-shell acceptance

Contract/architecture tests should verify:
- `/api/v1` business boundary requires authentication by design/test setup
- stable Problem Details machine code convention
- write DTO unknown-property rejection configuration
- stable explicit OperationId/name convention
- business-write infrastructure requires Idempotency-Key
- endpoint layer does not directly execute `ErpDbContext` business writes
- OpenAPI document can be generated
- production OpenAPI exposure is disabled by default/configured appropriately

## 26. P3.5 — AuthN/AuthZ Implementation Architecture hard gate

AuthN/AuthZ architecture is a **hard gate before `InitialV01`**.

REST baseline already requires:
- authenticated business endpoints
- capability-style authorization policies
- server-side actor resolution to `system.accounts`

Still unresolved at implementation level:
- authentication mechanism (for example Cookie/Bearer/external IdP choice)
- principal identity format
- principal-to-`system.accounts` resolution
- account provisioning
- credential/IdP ownership
- capability-policy assignment/resolution
- session/token lifecycle

This is technical/security architecture, not a YowThi Business Rule Gap.

## 27. Auth persistence revision rule

If AuthN/AuthZ design requires persistence beyond the approved 55-relation baseline, implementation must stop and formally revise:
- relational architecture
- EF mapping architecture
- affected API/security documentation

Implementation must not silently add relation 56+ such as:

```text
roles
permissions
account_roles
external_identities
sessions
password_credentials
```

without formal design approval.

This gate occurs before `InitialV01` so that the first formal migration does not immediately become obsolete because of an unreviewed security schema.

## 28. Test authentication boundary

Before production authentication is finalized, API contract/integration tests may use a dedicated test authentication handler.

It must be restricted to test assembly/configuration.
It must not become a staging/production no-password path.
Tailscale does not replace application authentication.

## 29. P4 — `InitialV01` readiness

Only after all of the following pass may the formal initial migration be generated:
- all mapping batches M1–M7 complete
- 55 relation count verified
- full EF metadata architecture tests pass
- AuthN/AuthZ hard gate completed
- any Auth-required relational revision incorporated first

Then generate a single formal initial migration:

```text
InitialV01
```

It belongs to the dedicated `YowThi.Erp.Infrastructure.Migrations` project and one migration stream for the single write `ErpDbContext`.

## 30. Design-time/runtime provider consistency

Runtime and design-time `ErpDbContext` configuration must share the same PostgreSQL/Npgsql model assumptions.

The migration design-time factory must not accidentally produce a different provider model from runtime configuration.

Target PostgreSQL major version is explicitly PostgreSQL 18 in the provider configuration/model assumptions rather than relying on an accidental design-time server probe.

## 31. InitialV01 focused commit

`InitialV01` must have its own focused commit, for example:

```text
13 db: add InitialV01 migration
```

Do not mix InitialV01 with Sales UI, Auth feature code, or unrelated business handlers.

## 32. InitialV01 static acceptance

Before PostgreSQL runtime execution, review the generated migration/model snapshot for:
- 14 schemas
- 55 relations unless a formally approved Auth revision changed the baseline before generation
- all expected PK/alternate keys
- all expected FKs
- Restrict/No Action core delete behavior
- named CHECK constraints
- business unique/partial indexes
- NULLS NOT DISTINCT index
- explicit `row_version bigint`
- exact numeric strategy without invented silent scale rounding
- no `xmin` concurrency model
- no unintended PostgreSQL enum types
- no accidental `varchar(n)` restrictions
- migration history configured to `system.__ef_migrations_history`

If generated migration conflicts with `docs/10`, first treat it as a mapping bug.
Fix Fluent mapping and regenerate.
Do not habitually patch generated migration SQL while leaving the model snapshot inconsistent.

Provider-specific deliberate migration SQL is allowed only when the approved model cannot be faithfully expressed by the provider mapping API.

## 33. Migration drift gate

Before P5:

```text
EF model metadata PASS
→ generated migration reviewed
→ generated SQL reviewed as needed
→ no pending model changes
```

CI adds `dotnet ef migrations has-pending-model-changes` or the current equivalent supported by the pinned EF Core toolchain.

The build must fail if mapping changes are committed without the required migration after migration history begins.

## 34. P5 — PostgreSQL 18 activation

At this phase Docker Desktop becomes required locally.

Add the approved development PostgreSQL runtime setup, such as:

```text
compose.yaml
PostgreSQL 18 service
local development database settings
```

Then:

```text
docker compose up -d
→ clean PostgreSQL 18
→ apply InitialV01
→ schema inspection
→ integration tests
```

Do not use `EnsureCreated()` as a substitute for migration-based schema creation.

## 35. PostgreSQL acceptance group DB1 — schema

Verify on a clean PostgreSQL 18 database:
- all expected schemas
- all expected relations
- all expected constraints
- all expected indexes
- migration history location
- migration applies successfully from empty database

The database itself must match the approved relational baseline, not only the EF metadata model.

## 36. PostgreSQL acceptance group DB2 — structural rejection

Integration tests intentionally submit invalid relational states and confirm PostgreSQL rejects them.

Examples:
- Procurement Entry Supplier/Farmer shape mismatch
- cross-Route-Version Process Material membership
- invalid Sales Product pricing/weight shape
- duplicate logical Inventory Position with nullable identity dimensions
- invalid Inventory Movement sign
- invalid paired lifecycle metadata
- invalid source typed-FK combinations

## 37. PostgreSQL acceptance group DB3 — concurrency

Test real PostgreSQL concurrency semantics:
- stale explicit row-version writer
- Inventory Position race
- Finance Outstanding compare-and-set race
- simultaneous same CommandId acquisition
- Outbox workers with `FOR UPDATE SKIP LOCKED`/lease behavior

Do not use EF InMemory or SQLite as evidence that PostgreSQL-specific concurrency/constraints pass.

## 38. PostgreSQL acceptance group DB4 — transaction rollback

Force a failure late in a multi-module transaction.

Example concept:

```text
Procurement fact written
+ Inventory work written
+ Finance step fails
```

Expected result:

```text
no Procurement commit
no Inventory commit
no Finance commit
no committed Audit/Outbox/CommandExecution success
```

This validates the single write Unit of Work and explicit command transaction boundary.

## 39. P6 — Vertical business slices

After persistence acceptance, implement complete business commands vertically rather than completing every backend module in isolation before any working flow exists.

Recommended order:

```text
V0  Master/config prerequisites needed by next slice
V1  ConfirmProcurementEntry
V2  ConfirmOutsourcedSupplyDetail
V3  ConfirmProcessingExecution
V4  ConfirmSales
V5  RecordSalesPackagingWork + ConfirmEmployeeDailyWage
V6  AddPayableAdjustment + PayPayable + ReceiveReceivable
V7  TransferInventory + AdjustInventory + Batch Close
V8  Correction / Lifecycle / Hard Delete commands
```

`V0 Master/config prerequisites` means only the master/config capability needed by the next business slice, not building the entire administration product before transaction flows.

## 40. Why Procurement is the first full command slice

`ConfirmProcurementEntry` is the first full vertical slice because it exercises many core architecture boundaries without the full complexity of Sales Allocation.

It validates:
- Business-intent command
- Idempotency Key / CommandExecution
- authenticated actor
- UUID v7 facts
- Procurement Batch resolution
- Procurement Entry
- Inventory receipt operation/movement/position
- Procurement Payable obligation
- Company Pickup transport basis when applicable
- Audit
- Outbox
- Problem Details / API contract
- one atomic PostgreSQL transaction

## 41. ConfirmProcurementEntry receipt location refinement

The confirmed safe handling for PROC-002 must be implemented end-to-end.

`ConfirmProcurementEntry` accepts an optional:

```text
Receipt Storage Location
```

REST field:

```text
receiptStorageLocationId
```

Resolution semantics:
- if explicitly supplied: validate and use it
- if omitted: use the unique applicable default when one can be resolved
- if omitted and there is no unique applicable default: block confirmation and require explicit choice

This is not a new Business Rule. It is the command/API representation of the already approved PROC-002 safe v0.1 handling.

The Procurement UI/query layer therefore needs a location-selection query when receipt location cannot be resolved uniquely.

## 42. Procurement concurrency prerequisites

The first Procurement slice must explicitly test concurrent Procurement Batch resolution for the same:

```text
Procurement Date + Procurement Product
```

The database business unique constraint remains final structural protection.
The command implementation must handle the race safely rather than assuming two concurrent requests cannot resolve/create the same Batch.

## 43. Procurement vertical work packages

Split the first full slice into focused packages/commits.

### V1-C1 — Domain/Application

Implement:
- `ConfirmProcurementEntryCommand`
- command result
- batch resolution semantics
- amount calculation
- receipt-location resolution contract
- state/business validation

### V1-C2 — Infrastructure transaction

Implement the atomic persistence path:
- CommandId acquire/replay
- Procurement Batch/Entry
- Inventory receipt facts/position update
- Payable + obligation item
- Company Pickup transport basis when applicable
- Audit
- Outbox
- CommandExecution success
- one transaction

### V1-C3 — API

Implement:

```text
POST /api/v1/procurement/entries
```

including:
- request/response DTO
- `receiptStorageLocationId`
- Idempotency-Key
- Problem Details
- stable OperationId
- OpenAPI contract

### V1-C4 — PostgreSQL integration/concurrency tests

Cover:
- normal supplier path
- normal farmer path
- same-key replay
- same key + different hash rejection
- same key + different actor rejection
- receipt default location path
- explicit receipt-location path
- ambiguous location requires choice
- concurrent same Procurement Batch resolution
- transaction rollback on later failure
- Company Pickup basis path without inventing transport Payable grouping

### V1-C5 — React Procurement Entry UI

Start only after C1–C4 backend contract is stable.

## 44. Subsequent vertical slice principles

Every persisted Business Command is Done only when the applicable layers are complete:
- Domain behavior
- Application command/query contracts
- persistence
- idempotency
- Audit
- Outbox
- API DTO/endpoint
- Problem Details mapping
- OpenAPI contract
- unit tests
- PostgreSQL integration tests
- concurrency tests where applicable

Do not postpone Audit/Idempotency/Outbox as optional polish after the command is considered complete.

## 45. Gap-aware implementation gate

Before each vertical slice, review `docs/06-business-rule-gap-register-v0.1.md` for affected gaps.

Rules:
- Class A unresolved cases use only the explicitly approved safe v0.1 handling and remain blocked from unsupported behavior
- Class B safe controls may be implemented without relabeling them as Business Rules
- Class C extensions remain unimplemented until real need is confirmed

Examples:
- PROC-001/002 before Procurement
- PROCESS location/mode gaps before Processing
- SALES-001 before Sales
- HANDLING/LABOR gaps before related flows
- FIN-001–FIN-009 before affected Finance/Transport behaviors

Do not harden a temporary safe control into a permanent relational rule unless Business Rule confirmation occurs.

## 46. Finance Transport implementation boundary

Procurement Company Pickup can create the approved per-entry transport basis during the Procurement slice.

Do not complete Transport Payable grouping/confirmation/settlement until FIN-007/008/009 are confirmed.

No implementation may infer:
- final rounding rule
- Payable grouping boundary
- payee storage stage

from convenience.

## 47. P7 — React UI sequencing

Frontend transaction screens follow stable backend vertical slices.

Pattern:

```text
Procurement backend slice stable
→ Procurement UI
Processing backend slice stable
→ Processing UI
Sales backend slice stable
→ Sales UI
```

Do not build the full operational UI against long-lived mock APIs and reconcile it with real command contracts later.

Frontend error handling uses HTTP status + stable Problem Details `code`, not localized detail parsing.
Frontend concurrency flows refresh/retry using new command identity where required by the REST architecture.

## 48. OpenAPI client code-generation gate

Do not select a TypeScript OpenAPI code generator during P0 solely for convenience.

After the first real API vertical slice stabilizes OperationIds, request/response DTOs, idempotency headers, and Problem Details contracts, evaluate:
- manually maintained typed API client
- generated OpenAPI TypeScript client

Tooling choice must not redefine the public REST contract.

## 49. File ownership convention

Organize by domain/module vocabulary across layers.

Example Procurement:

```text
YowThi.Erp.Domain/
└─ Procurement/

YowThi.Erp.Application/
└─ Procurement/
   ├─ Commands/
   ├─ Queries/
   └─ Contracts/

YowThi.Erp.Infrastructure/
└─ Persistence/
   └─ Mapping/
      └─ Procurement/

YowThi.Erp.Api/
├─ Endpoints/
│  └─ Procurement/
└─ Contracts/
   └─ Procurement/

YowThi.Erp.Web/
└─ src/features/procurement/
```

Avoid generic top-level dumping grounds such as `Services/`, `Managers/`, `Helpers/`, or `Repositories/` that erase module boundaries.

## 50. Test project ownership

### Domain.Tests

Use for:
- pure calculations
- Business Rule invariants
- domain state transitions

No database required.

### ArchitectureTests

Use for:
- project dependency direction
- forbidden technical references
- EF model metadata
- mapping constraints/index/concurrency expectations

Database not required for model metadata tests.

### Api.ContractTests

Use for:
- routes/methods
- auth policy metadata
- Problem Details
- JSON binding/validation
- idempotency header contract
- OpenAPI semantic contract

Use test auth infrastructure where necessary.

### IntegrationTests

Use real PostgreSQL for:
- migrations/schema
- constraints
- transaction rollback
- idempotency races
- optimistic concurrency
- Finance CAS
- Inventory identity/concurrency
- Outbox worker concurrency

## 51. CI activation sequence

### From P0

CI runs:
- .NET restore/build/test
- pnpm frozen install/typecheck/build

### From P2

Add:
- EF model metadata architecture tests

### From P3

Add:
- API contract tests
- OpenAPI semantic tests

### From P4

Add:
- migration/model drift check

### From P5

Add:
- PostgreSQL 18 integration job
- migration apply tests
- concurrency tests

CI PostgreSQL runtime is managed by CI infrastructure, not the developer's Docker Desktop.

## 52. Local tool preflight

Before first code scaffolding, verify the workstation has the approved development tools available:

```text
dotnet --info
dotnet tool restore
node --version
pnpm --version
git --version
```

Docker is not part of the P0 preflight acceptance unless the workflow reaches P5.

## 53. Recommended early implementation commit sequence

Logical sequence:

```text
01 build: scaffold .NET solution
02 build: scaffold React web app
03 feat: add application and persistence technical foundation
04 feat: map system and party
05 feat: map infrastructure product and processing configuration
06 feat: map procurement and processing
07 feat: map outsourced sales and inventory
08 feat: map sales handling and labor
09 feat: map finance
10 feat: map audit
11 feat: add API host and contract infrastructure
12 docs/arch: finalize AuthN/AuthZ implementation architecture / relational revision if required
13 db: add InitialV01 migration
14 test: validate PostgreSQL v0.1 persistence baseline
15+ feat: implement Procurement vertical slice packages
```

Exact commit text can vary, but the sequencing and focused-commit principle remain.

## 54. Definition of Done — mapping batch

A mapping batch is Done only when:
- code compiles
- relevant EF metadata tests pass
- mapping matches `docs/10`
- no known temporary relational contradiction is introduced
- previous mapping batches remain green

## 55. Definition of Done — business command

A persisted Business Command is Done only when the applicable implementation includes:
- Domain/Application behavior
- persistence transaction
- idempotency
- Audit
- Outbox
- API contract
- Problem Details/error mapping
- OpenAPI
- unit/architecture tests
- PostgreSQL integration tests
- concurrency/race tests where relevant

## 56. Definition of Done — UI slice

A UI slice is Done only when:
- its backend contract is stable
- server-state query/mutation integration works
- loading/error/validation states are handled
- Problem Details codes drive client flow where appropriate
- locale behavior is handled
- concurrency refresh/retry behavior follows the API contract

## 57. Production migration rule

The production API does not call `Database.Migrate()` during startup.

Migration execution is a deployment action.
`InitialV01` and subsequent migration artifacts are reviewed/tested before deployment.

## 58. No new Business Rule gaps

This Build Plan introduces no new YowThi Business Rules.

The `receiptStorageLocationId` refinement is the implementation representation of existing PROC-002 safe handling.
The AuthN/AuthZ hard gate is ERP Control Governance / security architecture.

Existing Business Rule gaps remain governed by the Gap Register.

## 59. Build readiness result

Final Build Readiness Review passed all five implementation gates:
1. first code commit can be green without PostgreSQL
2. seven mapping batches have no blocking circular relational dependency
3. `InitialV01` has explicit model/migration/drift gates
4. first Procurement slice has complete prerequisites after receipt-location contract reconciliation
5. local/CI tooling and security sequencing are sufficient to start coding

## 60. Immediate next step

Architecture/planning review is complete for starting implementation.

Next action:

**Implementation P0 — first code commit: scaffold the .NET solution.**

Initial scope:
- `YowThi.Erp.slnx`
- `global.json`
- `Directory.Build.props`
- `Directory.Packages.props`
- repository-local .NET tool manifest
- Domain/Application/Infrastructure/Migrations/Api projects
- Domain/Architecture/API Contract/Integration test projects
- project references and architecture dependency guard
- README/current-phase update as needed
- basic CI .NET restore/build/test gate

Do not add Business Rules or PostgreSQL-dependent implementation to this first code commit.
Docker Desktop remains OFF until P5 first real PostgreSQL 18 migration apply/integration testing.
