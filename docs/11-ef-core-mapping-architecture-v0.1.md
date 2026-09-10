# EF Core Mapping Architecture v0.1 — YowThi ERP V2

Status: **DECISION / v0.1**
Revision basis: EF Core Mapping Architecture Revision 0.2 + Final Architecture Review, formally confirmed.

## 1. Purpose and precedence

This document defines how the consolidated relational model is mapped and operated through EF Core 10 / Npgsql in YowThi ERP V2.

Business Facts and Business Rules remain owned by Business Discovery, Command Contracts, the Gap Register, and owning-domain documents.

For relational shape, table/column identity, FK/UNIQUE/CHECK requirements, and relation count, `docs/10-relational-model-consolidation-v0.1.md` remains the DDL baseline.

This document is the implementation baseline for:
- `ErpDbContext` scope
- EF Core mapping organization
- value generation
- optimistic concurrency
- typed/composite relationships
- PostgreSQL-specific mapping
- transaction boundaries
- migrations
- persistence/query interfaces
- InitialV01 validation

EF model snapshots and generated migrations are implementation artifacts. They do not override `docs/10` or Business Facts.

## 2. Technology baseline

- .NET 10 / C#
- EF Core 10
- Npgsql EF Core provider
- PostgreSQL 18
- one PostgreSQL database
- module-separated PostgreSQL schemas
- one write `ErpDbContext` in v0.1

No additional write DbContext is introduced for individual modules.

## 3. Solution persistence boundary

Recommended project structure:

```text
src/
├─ YowThi.Erp.Domain
├─ YowThi.Erp.Application
├─ YowThi.Erp.Infrastructure
├─ YowThi.Erp.Infrastructure.Migrations
└─ YowThi.Erp.Api
```

Rules:
- Domain must not reference EF Core or Npgsql.
- Application must not depend on concrete EF Core types.
- EF Core/Npgsql runtime mapping belongs in Infrastructure.
- migrations live in the dedicated Infrastructure.Migrations project.
- Api does not own relational persistence rules.

## 4. One write `ErpDbContext`

Use one scoped write `ErpDbContext` as the v0.1 Unit of Work.

Rationale:
- `ConfirmProcurementEntry` writes Procurement + Inventory + Finance + Audit + Idempotency + Outbox atomically.
- `ConfirmSales` writes Sales + Allocation + Inventory + Finance + Audit + Idempotency + Outbox atomically.
- multiplying write DbContexts would weaken the single-database atomic boundary.

Module boundaries remain visible through:
- domain/application modules
- persistence contracts
- mapping folders
- PostgreSQL schemas

They are not represented by separate write DbContexts.

## 5. Mapping organization

Use Fluent API only.

Do not use EF mapping attributes such as:
- `[Table]`
- `[Column]`
- `[Index]`
- `[Timestamp]`

Default structure:

```text
Infrastructure/Persistence/
├─ ErpDbContext.cs
├─ Mapping/
│  ├─ System/
│  ├─ Party/
│  ├─ Infrastructure/
│  ├─ Product/
│  ├─ ProcessingConfig/
│  ├─ Procurement/
│  ├─ Processing/
│  ├─ Outsourced/
│  ├─ Sales/
│  ├─ Inventory/
│  ├─ SalesHandling/
│  ├─ Labor/
│  ├─ Finance/
│  └─ Audit/
├─ Concurrency/
├─ Transactions/
├─ Idempotency/
├─ Outbox/
└─ Queries/
```

Default rule:
- one `IEntityTypeConfiguration<T>` per mapped relation/entity
- small module registration extensions
- no giant multi-thousand-line `OnModelCreating`

## 6. Deterministic model registration

Register mappings by module explicitly, for example:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplySystemMappings();
    modelBuilder.ApplyPartyMappings();
    modelBuilder.ApplyInfrastructureMappings();
    modelBuilder.ApplyProductMappings();
    modelBuilder.ApplyProcessingConfigMappings();
    modelBuilder.ApplyProcurementMappings();
    modelBuilder.ApplyProcessingMappings();
    modelBuilder.ApplyOutsourcedMappings();
    modelBuilder.ApplySalesMappings();
    modelBuilder.ApplyInventoryMappings();
    modelBuilder.ApplySalesHandlingMappings();
    modelBuilder.ApplyLaborMappings();
    modelBuilder.ApplyFinanceMappings();
    modelBuilder.ApplyAuditMappings();
}
```

The order mirrors the consolidated relational dependency structure. It does not replace FK dependency validation by PostgreSQL.

## 7. Explicit database naming

Database identifiers are mapped explicitly to the established snake_case relational baseline.

Example:

```csharp
builder.ToTable("procurement_entries", "procurement");

builder.Property(x => x.ProcurementBatchId)
    .HasColumnName("procurement_batch_id");
```

Do not make schema correctness depend on an automatic naming-convention package.

CLR property/type names may use normal C# naming while PostgreSQL names remain explicit and stable.

## 8. CLR/PostgreSQL type baseline

Preferred mappings:

| PostgreSQL | CLR |
|---|---|
| `uuid` | `Guid` |
| `date` | `DateOnly` |
| `timestamptz` | `DateTimeOffset` |
| `bigint` | `long` |
| `numeric` | `decimal` |
| `boolean` | `bool` |
| `text` | `string` |
| `bytea` | `byte[]` |
| `jsonb` | `JsonElement` / nullable `JsonElement` representation |

Persisted system timestamps must represent UTC.

Application command code should use a single logical current time per command where practical, supplied through a time abstraction such as `TimeProvider`.

## 9. UUID v7 generation

Internal ERP technical entity IDs are generated client/application-side as UUID v7 before persistence.

Preferred .NET mechanism:

```csharp
Guid.CreateVersion7()
```

Mapping:

```csharp
builder.Property(x => x.Id)
    .HasColumnName("id")
    .ValueGeneratedNever();
```

Reasons:
- IDs are available before `SaveChanges`.
- cross-module atomic graphs can reference stable IDs before persistence.
- Audit and Outbox subjects/messages can be constructed before commit.
- whole-command retry has stable identity semantics.

PostgreSQL 18 `uuidv7()` support may be used for explicitly database-owned technical rows if a later design requires it, but ordinary ERP entity identity is client-side in v0.1.

Externally supplied `system.command_executions.command_id` is never regenerated by EF/DB.

## 10. Explicit `row_version bigint`

YowThi uses an explicit technical concurrency column:

```text
row_version bigint NOT NULL DEFAULT 1
```

Mapping:

```csharp
builder.Property(x => x.RowVersion)
    .HasColumnName("row_version")
    .HasDefaultValue(1L)
    .IsConcurrencyToken();
```

Do not use `.IsRowVersion()` for this design.
Do not map concurrency to PostgreSQL `xmin`.

The relational source of truth remains the explicit `row_version bigint` defined in `docs/10`.

## 11. Row-version increment interceptor

A technical SaveChanges interceptor may increment row versions for entities explicitly participating in optimistic concurrency.

Allowed responsibility:
- Added concurrency entity -> initialize version 1 if not already initialized.
- Modified concurrency entity -> current version becomes original version + 1.

It must not implement:
- Business Rules
- Soft Delete
- Audit meaning
- Inventory movements
- Finance obligations
- Outbox business-event decisions

Only entities intentionally mapped as mutable invariant owners participate.

## 12. Tracked-first business write rule

Mutation commands must not accept a disconnected entity graph and call `DbContext.Update()` as a business update mechanism.

Forbidden pattern:

```text
API payload
→ construct detached entity graph
→ context.Update(graph)
→ SaveChanges
```

Required pattern:

```text
Command intent
→ load current aggregate/entity with the current ErpDbContext
→ validate expected row_version and dependencies
→ apply command intent to tracked state
→ write facts/projections
→ SaveChanges inside the command transaction
```

This keeps:
- concurrency original values trustworthy
- dependency checks current
- navigation/state changes explicit
- API contracts separate from persistence graphs

## 13. DbContext invalidation after write failure

Any business-write persistence failure invalidates that `ErpDbContext` for command retry.

After any failed write/SaveChanges:
1. rollback the transaction
2. dispose the `ErpDbContext`
3. create a fresh application scope/context for retry
4. retry the whole command using the same CommandId when retry is allowed

This applies to:
- `DbUpdateConcurrencyException`
- PostgreSQL constraint errors
- connection failures
- timeouts
- cancellation during persistence
- other write persistence exceptions

Do not catch a failed SaveChanges, mutate the same tracked graph, and retry the same command in the same context.

Multiple successful SaveChanges calls inside the same explicit transaction are allowed when required.

## 14. Core FK delete behavior

Core relationships map to non-cascading delete behavior:

```csharp
.OnDelete(DeleteBehavior.Restrict)
```

The goal is to produce PostgreSQL `RESTRICT / NO ACTION` semantics consistent with the relational baseline.

Do not use EF cascade delete as the Data Deletion Protection implementation.

Hard Delete remains:
- explicit
- owning-domain controlled
- dependency validated
- audited in the same transaction

## 15. Alternate/composite keys

Use alternate keys only where the relationship itself requires a composite principal key.

Examples retained from `docs/10` include:
- Processing Route Version `(id, processing_route_id)`
- Process Material `(id, processing_route_version_id)`
- Processing Module `(id, processing_route_version_id)`
- Sales Detail `(id, sales_id)`
- Allocation Revision `(id, sales_id)`
- Allocation Revision Item `(id, sales_detail_id)`
- Receivable `(id, sales_id)`
- Payable `(id, payable_kind)`

Example:

```csharp
builder.HasOne(x => x.InputProcessMaterial)
    .WithMany()
    .HasForeignKey(x => new
    {
        x.InputProcessMaterialId,
        x.ProcessingRouteVersionId
    })
    .HasPrincipalKey(x => new
    {
        x.Id,
        x.ProcessingRouteVersionId
    })
    .OnDelete(DeleteBehavior.Restrict);
```

For uniqueness that is not an FK principal target, use a unique index rather than an alternate key.

## 16. Nullable Batch Route Version remains transactional

Do not create a composite principal relationship from Processing Execution to `(ProcurementBatch.Id, ProcurementBatch.ProcessingRouteVersionId)`.

Batch Route Version is legitimately nullable before processing.

Map normal FKs:
- Processing Execution -> Procurement Batch
- Processing Execution -> Processing Route Version

`ConfirmProcessingExecution` must transactionally validate:

```text
batch.processing_route_version_id
=
execution.processing_route_version_id
```

This is an owning-command lifecycle invariant, not an EF composite relationship.

## 17. Typed nullable-FK shapes

Typed relationships such as Procurement source, Inventory identity, Sales Allocation origin, and Finance Payable source are mapped as:
- scalar discriminator/code
- nullable real FKs
- normal EF relationships for every real FK
- named PostgreSQL CHECK constraints for the valid shape

Do not model these as EF inheritance hierarchies merely because they are discriminated shapes.

Example Procurement relationships:

```csharp
builder.HasOne(x => x.Supplier)
    .WithMany()
    .HasForeignKey(x => x.SupplierId)
    .OnDelete(DeleteBehavior.Restrict);

builder.HasOne(x => x.Farmer)
    .WithMany()
    .HasForeignKey(x => x.FarmerId)
    .OnDelete(DeleteBehavior.Restrict);
```

The CHECK enforces SUPPLIER/FARMER exactly-one-FK shape.

### Procurement Batch receipt destination mapping

The confirmed 2026-09-10 Procurement receipt-destination ownership is mapped on `ProcurementBatch`, not on `ProcurementEntry` request state.

Required mapping:
- `ProcurementBatch.ReceiptStorageLocationId` -> `procurement.procurement_batches.receipt_storage_location_id`
- real FK to `infrastructure.storage_locations(id)`
- `DeleteBehavior.Restrict`
- B-tree FK index
- nullable in the relational shape only to preserve/identify legacy ambiguous rows during forward migration; new confirmable Batch state must resolve/capture a valid destination at the command boundary

Command/persistence behavior:
- the first confirmed Entry for a new date + product Batch captures the Product's current valid default Storage Location on the Batch
- all later Entries for the same Batch use that captured value
- Product default changes do not rewrite an already-created Batch destination
- the HTTP/Application command does not accept a detail-level receipt-location override
- an older Batch whose header destination is null may adopt an existing PURCHASE_RECEIPT location only when historical ledger evidence is unambiguous
- multiple historical receipt locations remain an explicit data ambiguity; persistence must not guess one

Inventory Movement remains the immutable receipt ledger and still records the concrete Storage Location. It does not replace the Batch-owned receipt-destination relationship.

## 18. Sales Allocation mapping

Historical allocation truth:
- `sales.sales_allocation_revisions`
- `sales.sales_allocation_revision_items`

Current official allocation:
- `sales.sales_allocations` pointer-only projection

Current relation columns:
- `sales_detail_id`
- `sequence`
- `sales_allocation_revision_item_id`
- `row_version`

Mapping pattern:

```csharp
builder.HasKey(x => new { x.SalesDetailId, x.Sequence });

builder.HasIndex(x => x.SalesAllocationRevisionItemId)
    .IsUnique();

builder.HasOne(x => x.RevisionItem)
    .WithMany()
    .HasForeignKey(x => new
    {
        x.SalesAllocationRevisionItemId,
        x.SalesDetailId
    })
    .HasPrincipalKey(x => new
    {
        x.Id,
        x.SalesDetailId
    })
    .OnDelete(DeleteBehavior.Restrict);
```

Do not duplicate Origin/source batch/quantity/manual override onto the current pointer entity.

Inventory Movement allocation lineage points to immutable Revision Items.

## 19. Inventory `NULLS NOT DISTINCT`

The full typed Inventory Position identity is mapped with a unique Npgsql index whose null values are not distinct.

Pattern:

```csharp
builder.HasIndex(x => new
{
    x.Origin,
    x.ProcurementBatchId,
    x.OutsourcedSupplyBatchId,
    x.InventoryObjectKind,
    x.ProcurementProductId,
    x.ProcessMaterialId,
    x.SalesProductId,
    x.StorageLocationId,
    x.RawSourceKind,
    x.SupplierId
})
.IsUnique()
.AreNullsDistinct(false);
```

This mapping is part of the EF model and migration snapshot; raw migration SQL is not required solely for this index.

## 20. Payable typed mapping

`finance.payables` remains one typed relation.

Real FK properties are mapped normally. Business identities are protected by partial unique indexes.

Examples:
- Procurement Supplier: unique `(procurement_batch_id, supplier_id)` where kind = PROCUREMENT_SUPPLIER
- Procurement Farmer: unique `(procurement_batch_id, farmer_id)` where kind = PROCUREMENT_FARMER
- Outsourced Vendor: unique `outsourced_supply_detail_id` where kind = OUTSOURCED_VENDOR
- Employee Daily Wage: unique `employee_daily_wage_id` where kind = EMPLOYEE_DAILY_WAGE
- Company Pickup Transport: no permanent business unique grouping while FIN-008 remains unresolved

`payable_obligation_items` also carries `payable_kind` so a composite FK can structurally require the obligation kind-owner relationship to point at a Payable of the same kind.

The alternate key `(Payable.Id, Payable.PayableKind)` is intentionally retained for this typed integrity even though `Id` alone is unique.

Cross-row semantic alignment between Procurement Entry source identity and its Payable owner remains an owning-command transaction invariant.

## 21. Enum/discriminator mapping

Closed, confirmed code sets may use CLR enums mapped to PostgreSQL text:

```csharp
builder.Property(x => x.Origin)
    .HasConversion<string>();
```

PostgreSQL CHECK constraints remain the relational vocabulary guard.

Do not create PostgreSQL enum types in v0.1.

Do not invent CLR enum members for business/configuration vocabularies that are not yet formally confirmed.

Renaming a persisted enum string is a schema/data compatibility change, not a harmless CLR refactor.

## 22. CHECK constraints

Row-local structural constraints belong in the EF model/migrations using named table CHECK constraints.

Use CHECK constraints for:
- typed discriminator + nullable-FK shape
- metadata pairs
- finite/sign constraints
- conditional Sales Weight
- local amount formulas
- local Outstanding formula
- movement sign vocabulary
- max-one-decimal validation where confirmed

Do not replace transactional aggregate invariants with a generic database trigger framework.

## 23. Partial indexes

Use filtered unique indexes for confirmed conditional uniqueness.

Examples:
- one ACTIVE Route Version per Route
- Payable source identities by kind
- one source-owned Inventory Operation per source fact where established

Filter SQL uses stable PostgreSQL column/value representation.

## 24. FK index review

EF conventions usually create indexes for FK properties, but generated migrations must still be reviewed.

Every operational/core FK must have an appropriate B-tree index unless an existing PK/alternate key/unique/composite index already provides the required leading columns.

Do not create redundant indexes solely because a generic checklist says every FK needs a second index.

## 25. JSONB mapping

Technical JSON fields remain intentionally opaque to the business domain:
- `system.command_executions.result_payload`
- `system.outbox_messages.payload`
- `audit.audit_event_subjects.subject_key`
- `audit.audit_event_subjects.change_summary`

Use a technical CLR JSON representation such as `JsonElement` mapped to `jsonb`.

Do not create generic business data storage through JSONB.

Audit `subject_kind + subject_key` remains non-FK historical metadata under ADR-005 and must not acquire a generic EF entity navigation/resolver.

## 26. No lazy loading

Do not install/use EF lazy-loading proxies in v0.1.

All database access must be visible through:
- explicit tracked loads for commands
- explicit Includes when appropriate
- projections
- query services

Do not allow property access to trigger hidden database I/O.

## 27. No global soft-delete query filter

Do not define a global `HasQueryFilter(x => x.DeletedAt == null)` on the write model.

Reasons:
- historical/correction/protection queries must see deleted references
- required relationships combined with global filters can unexpectedly remove related roots from query results
- lifecycle visibility must be explicit by use case

Normal current-use queries explicitly filter active/non-deleted rows.
Historical/protection queries deliberately do not.

## 28. No generic soft-delete interceptor

Do not intercept `EntityState.Deleted` globally and transform it into Soft Delete.

Soft Delete is an owning-domain command with:
- dependency validation
- lifecycle mutation
- actor/time metadata
- row-version handling
- audit
- idempotency
- outbox where required

Not every entity supports ordinary soft delete.

## 29. Persistence behavior classes

Mapped relations fall into three implementation behavior classes.

### A. Tracked mutable/invariant owners
Examples:
- masters/configuration
- Procurement Batch
- Sales
- Inventory Position
- Payable/Receivable
- Outstanding Positions

Use:
- tracked EF entities
- explicit concurrency token where applicable
- normal relationship mapping

### B. Append-oriented facts
Examples:
- Inventory Movement
- Payment
- Receipt
- Audit Event/Subject
- Outbox Message

Use:
- append/insert semantics
- no generic update API
- no row version unless relational baseline explicitly says otherwise

### C. Specialized atomic SQL paths
Examples:
- CommandId acquisition
- Finance Outstanding monetary CAS
- Outbox worker dequeue/lease

Use provider-specific SQL where the PostgreSQL atomic primitive is the requirement.

EF Core is the default mapper, not a mandate that every persistence operation must be a tracked `SaveChanges` update.

## 30. Command transaction boundary

Business commands requiring persistence use one explicit PostgreSQL transaction, normally READ COMMITTED unless an owning operation explicitly requires otherwise.

Canonical flow:

```text
fresh ErpDbContext
→ Begin transaction
→ acquire CommandId
→ load/validate owning aggregate state
→ write business facts
→ write required cross-domain facts/projections
→ append Audit
→ enqueue Outbox
→ finalize CommandExecution SUCCEEDED
→ Commit
```

The invariant is one transaction / one Unit of Work / one commit, not necessarily exactly one `SaveChanges` call.

## 31. CommandId acquisition

Command acquisition is a justified PostgreSQL-specific infrastructure SQL operation.

Preferred semantic:

```sql
INSERT INTO system.command_executions (...)
VALUES (...)
ON CONFLICT (command_id) DO NOTHING;
```

Affected rows = 1:
- this transaction owns a new command execution

Affected rows = 0:
- read existing committed command execution
- same CommandType + same RequestHash + SUCCEEDED -> return stored result
- different CommandType or RequestHash -> reject

A committed `IN_PROGRESS` record is not a normal v0.1 business lifecycle state; if observed, treat it as unexpected persistence/integrity state rather than inventing stale-command recovery rules.

## 32. Automatic execution retry

Do not enable automatic write retry (`EnableRetryOnFailure`) in v0.1.

Whole-command retry policy:
- surface persistence/transient failure
- rollback/dispose context
- retry with fresh context
- reuse the same CommandId

If automatic transient retry is introduced later, it must replay the whole explicit transaction with a fresh context and same CommandId. It requires a separate reviewed design/ADR.

## 33. Finance specialized CAS

EF concurrency tokens handle ordinary optimistic concurrency.

Finance settlement additionally requires an atomic current-Outstanding predicate.

A specialized SQL update may enforce, in one statement:
- expected row_version
- requested settlement <= current Outstanding under current v0.1 control
- increment settlement total
- recompute/decrement Outstanding
- increment row_version

Zero affected rows means concurrency conflict or current-state precondition failure.

Do not load stale Outstanding, validate only in application memory, and then issue an unconditional update.

## 34. Outbox worker SQL

Outbox workers may use PostgreSQL-native queue semantics such as:

```sql
FOR UPDATE SKIP LOCKED
```

with lease columns already defined in the relational baseline.

This is a technical work-queue operation and does not require tracked aggregate semantics.

Outbox remains at-least-once; downstream behavior must tolerate duplicate delivery by stable message identity.

## 35. Repository/persistence boundary

Do not create a generic `IRepository<TEntity>` CRUD abstraction.

Application-facing persistence contracts are capability/workflow oriented, for example:
- Procurement persistence
- Sales persistence
- Processing persistence
- Inventory persistence
- Finance persistence

They expose data access required by domain/application commands, not table CRUD.

Technical stores/services may be separate:
- CommandExecution store
- Audit writer
- Outbox store/dispatcher

Do not create one repository per table merely because the relational model has 55 relations.

## 36. Read/query boundary

Reads may use:
- EF Core `AsNoTracking()` projections
- Dapper
- native SQL

according to query complexity.

Read services return purpose-specific DTOs/projections rather than complete tracked aggregates by default.

Do not create a second duplicated full ReadDbContext model in v0.1.

Costing/reporting remain deferred and do not justify a reporting schema at this stage.

## 37. `ExecuteUpdate` / `ExecuteDelete`

Do not use `ExecuteUpdate` or `ExecuteDelete` as ordinary business aggregate write APIs.

They may be used only for explicit infrastructure/maintenance/CAS/projection operations that independently handle:
- transaction
- affected-row validation
- concurrency
- audit requirement
- owning-domain restrictions

They must not silently bypass row-version or Business Command semantics.

## 38. Migrations project

Use one dedicated project:

```text
YowThi.Erp.Infrastructure.Migrations
```

It references Infrastructure and owns:
- migration classes
- model snapshot
- design-time factory

Runtime business logic remains in Infrastructure/Application/Domain, not in the migration assembly.

Use one migration stream for the one write `ErpDbContext`.

Do not create a migration stream per PostgreSQL schema.

## 39. Design-time factory

Provide `IDesignTimeDbContextFactory<ErpDbContext>` in the migrations project.

The design-time factory configures:
- Npgsql connection
- migrations assembly
- migrations history location

It must not require bootstrapping the full API application to generate migrations.

Design-time configuration must not embed production secrets in source control.

## 40. Migration history

Use one EF migrations history table:

```text
system.__ef_migrations_history
```

This keeps technical migration state in the `system` schema rather than the PostgreSQL `public` schema.

## 41. Initial migration strategy

Once all 55 relations are actually mapped, generate one initial migration:

```text
InitialV01
```

Do not manufacture a long sequence of historical design-time migrations before any deployed database exists.

After InitialV01 becomes a real implementation/deployment baseline, future schema changes use normal incremental migrations.

## 42. Production migration policy

The production API must not automatically call `Database.Migrate()` during startup.

Database schema upgrades are deployment actions.

Development may later use EF tooling against the Docker PostgreSQL 18 environment.

## 43. Business-data seeding

Do not use EF `HasData()` to create operational business masters such as:
- Suppliers
- Farmers
- Employees
- Customers
- Products
- Warehouses
- Processing Routes
- wage values

These are business/operational data, not schema metadata.

Account/Auth bootstrap data remains deferred to the AuthN/AuthZ architecture.

## 44. Constraint naming

Use stable readable names.

Recommended prefixes:
- `pk_` primary keys
- `fk_` foreign keys
- `ak_` alternate keys
- `ux_` unique indexes
- `ix_` normal indexes
- `ck_` check constraints

Generated migrations should be reviewed for deterministic, understandable constraint naming.

## 45. InitialV01 acceptance gates

InitialV01 is not accepted merely because `dotnet ef migrations add` succeeds.

### Gate A — EF model metadata tests
Validate without a running PostgreSQL database:
- expected 55 relations
- exact schemas/table names
- PK/alternate keys
- FK targets
- core non-cascade delete behavior
- expected concurrency tokens
- expected indexes
- expected CHECK constraints
- no global query filters
- no unintended shadow FKs
- Audit/Outbox not accidentally mapped as mutable concurrency entities

### Gate B — migration static review
Review the generated InitialV01 against `docs/10` and this document:
- 55 mapped relations
- expected schemas
- key/FK shapes
- partial indexes
- `NULLS NOT DISTINCT`
- actor FKs
- CHECK SQL
- no accidental cascade
- no unintended PostgreSQL enum types
- no accidental varchar precision assumptions
- no accidental constrained numeric rounding
- no accidental `xmin`

### Gate C — PostgreSQL 18 integration
Apply InitialV01 to an empty PostgreSQL 18 database and inspect the resulting schema.

This is the first stage where Docker Desktop/PostgreSQL runtime becomes required.

### Gate D — behavioral persistence tests
At minimum test:
- invalid typed FK shapes fail
- wrong Route Version membership fails where structurally constrained
- duplicate nullable Inventory Position identity fails
- two-writer row-version conflict
- concurrent Finance settlement CAS
- concurrent same-CommandId acquisition
- Outbox worker `SKIP LOCKED` behavior
- clean migration creation/recreation

## 46. Migration drift gate

CI/architecture tests must detect pending EF model changes that are not represented in migrations once InitialV01 exists.

However:
- no pending model change only proves mapping/migration synchronization
- it does not prove Business Rule correctness
- it does not override `docs/10`

Generated migrations must still be reviewed against the approved relational baseline.

## 47. Testing DbContext lifecycle

Persistence/concurrency tests must follow production lifecycle semantics.

After an expected persistence exception:
- dispose the context
- do not keep using its ChangeTracker for another business attempt

Parallel tests should use separate contexts/transactions per simulated request.

## 48. No Docker requirement during architecture phase

Docker Desktop / PostgreSQL runtime is not required to maintain or review this architecture document.

Docker/PostgreSQL becomes required when implementation reaches:
- actual EF entity/configuration code
- `InitialV01` migration generation/execution
- PostgreSQL schema introspection
- provider-specific constraint verification
- concurrency/integration tests

Until that implementation stage, Docker Desktop may remain stopped.

## 49. Final v0.1 decisions

EF Core Mapping Architecture v0.1 formally establishes:

1. one write `ErpDbContext`
2. EF/Npgsql only in Infrastructure
3. Fluent API mapping only
4. explicit database snake_case names
5. no dependency on automatic naming conventions
6. application/client-side UUID v7 for ERP technical entity IDs
7. explicit `row_version bigint` + `IsConcurrencyToken()`
8. no Npgsql `xmin` concurrency mapping
9. technical row-version increment interceptor only
10. tracked-first business mutation; no detached graph `Update()`
11. any write persistence failure invalidates the current DbContext for retry
12. whole-command retry = fresh context + same CommandId
13. core relationships use Restrict/No Action
14. selected high-value alternate/composite keys only
15. nullable Batch Route Version equality remains transactional
16. typed references = discriminator + nullable real FKs + CHECK
17. Allocation current state = pointer projection to immutable revision items
18. Inventory `NULLS NOT DISTINCT` mapped through Npgsql provider support
19. Payable typed shape remains flat, not inheritance/subtype-table proliferation
20. PostgreSQL text + CHECK for persisted code vocabularies
21. no lazy loading
22. no global soft-delete query filter
23. no generic soft-delete interceptor
24. no generic repository
25. EF is default mapper; specialized PostgreSQL SQL allowed for atomic primitives
26. explicit command transaction, normally READ COMMITTED
27. CommandId acquire via `ON CONFLICT DO NOTHING`
28. automatic write execution retry off in v0.1
29. Finance settlement uses specialized atomic CAS when required
30. Outbox dequeue uses PostgreSQL locking/lease semantics
31. dedicated Infrastructure.Migrations project
32. design-time DbContext factory
33. one migration history at `system.__ef_migrations_history`
34. one InitialV01 baseline migration after complete mapping
35. production API does not auto-migrate
36. no operational business seeding through `HasData()`
37. InitialV01 requires model/static/PostgreSQL/behavioral acceptance gates

## 50. Next architecture step

After this architecture baseline:

**REST/API Architecture v0.1**

Expected scope includes:
- API route/module conventions
- command/query HTTP boundaries
- request/response DTOs
- CommandId/idempotency HTTP contract
- row-version/concurrency HTTP contract
- validation/error/problem-details conventions
- authorization boundary without inventing AuthN/AuthZ persistence
- pagination/filter/sort conventions
- localization contract
- correction/data-lifecycle endpoints
- transaction-safe command execution orchestration

Actual EF mapping/migration implementation remains after the architecture sequence reaches implementation. Docker Desktop is still not required until PostgreSQL-backed implementation/testing begins.
