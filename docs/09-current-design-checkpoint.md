# Current Design Checkpoint — YowThi ERP V2

Checkpoint status: **v0.1 design baseline**
Purpose: recover the project design if conversational context is lost.

## 1. Highest-level rule

Business Rules come from real YowThi operations.
ERP Control Governance is separate from Business Rules.
AI/developers must not invent Business Rules for technical convenience.

Precedence for implementation recovery:
- Business Facts / Business Rules: Business Discovery, Command Contracts, Gap Register, owning-domain documents
- relational implementation: `docs/10-relational-model-consolidation-v0.1.md`
- EF Core/Npgsql implementation architecture: `docs/11-ef-core-mapping-architecture-v0.1.md`
- HTTP/REST implementation architecture: `docs/12-rest-api-architecture-v0.1.md`

Earlier Part 1–6 schema notes remain design history, but `docs/10` is the later consolidated relational baseline when relational implementation details conflict.

## 2. Legacy safety

`C:\yowthi-erp` is protected Legacy ERP and is read-only.
Legacy H01/H02/H03 and old schema are references only.
They do not drive the new Domain Model.

## 3. Completed design stages

Completed to v0.1:
- Project charter / technical baseline
- Business Discovery
- Ubiquitous Language
- Core Domain Map
- end-to-end business chains
- Aggregate Boundaries
- Transaction Boundaries
- Domain Events baseline
- Application Command Catalogue
- Major Command Contracts
- Correction Command Framework
- Business Rule Gap Register
- Persistence Architecture Principles
- PostgreSQL Schema Part 1: Party + Product + Infrastructure + Processing Configuration
- PostgreSQL Schema Part 2: Procurement
- PostgreSQL Schema Part 3: Processing + Inventory
- PostgreSQL Schema Part 4: Outsourced + Sales
- PostgreSQL Schema Part 5: Sales Handling + Labor + Finance
- PostgreSQL Schema Part 6: Audit + System
- ADR-005 Audit Reference Boundary
- Full Relational Model Consolidation v0.1
- EF Core Mapping Architecture v0.1
- REST/API Architecture v0.1

The core v0.1 architecture chain now reaches the public HTTP/API boundary.

## 4. Core domain modules

- Product & Processing Configuration
- Party
- Procurement
- Processing
- Inventory & Warehouse
- Outsourced Supply
- Sales
- Sales Packaging / Handling
- Labor
- Receivable / Payable
- Data Deletion Protection / Audit

Costing/margin and reporting remain DEFERRED until explicitly scheduled by later planning.

## 5. Core source-batch identities

IN_HOUSE:
- Procurement Batch = same Procurement Date + same Procurement Product

OUTSOURCED:
- Outsourced Supply Batch = same Supply Date + same Outsourced Vendor

Product is not part of Outsourced Supply Batch identity.

## 6. Core product architecture

- Procurement Product = real purchased product master
- Process Material = route-owned traceable intermediate virtual product
- Sales Product = real final sellable product master
- Sales Product Group = content/group identity, not inventory

Processing Route transforms Procurement Product through 0..N Process Materials to Sales Product.

## 7. Processing v0.1

One processing system, three behaviors:
- SOURCE_TRACKED
- POOLED_OUTPUT
- FINAL_PACKAGING

No H01/H02/H03 persistence model.
No fixed first/second/third-stage architecture.

SOURCE_TRACKED:
- Supplier or Farmers Combined
- input scale
- 1..N output scales
- supplier/farmer-group quality traceability

POOLED_OUTPUT:
- no Supplier/Farmer
- no input picked quantity
- source consumption = output quantity

FINAL_PACKAGING:
- completed Sales Product quantity
- source deduction = completed quantity × Packaging Weight
- source may go negative
- Packaging Weight and Sales Weight are distinct

Processing Route/Version/Material structural membership uses selected high-value composite FK integrity.
Batch bound Route Version equality at Processing confirmation remains a transaction invariant because Batch route binding is nullable before processing.

## 8. Inventory

Inventory identity:
- Origin
- Source Batch
- Inventory Object
- Storage Location
- optional Raw Source Segment

Inventory Movement = append-oriented ledger truth.
Inventory Position = transactional rebuildable current projection.

The complete nullable typed Inventory Position identity uses PostgreSQL `UNIQUE NULLS NOT DISTINCT` so null typed dimensions cannot create duplicate logical positions.

No separate raw/semi/finished inventory systems.
No globally unrestricted negative inventory.

Allocation correction inventory effects use signed `SALES_ALLOCATION_ADJUSTMENT` movements under a `SALES_ALLOCATION_REVISION` operation.
Prior Inventory Movements are never rewritten.

## 9. Sales

Sales:
- Sales Date + Customer header
- 1..N Sales Details

Pricing:
- WEIGHT_BASED_UNIT
- UNIT_BASED

Allocation priority:
1. OUTSOURCED oldest → newest
2. IN_HOUSE oldest → newest
3. authorized manual override

Allocation total must equal Sales Detail quantity.

Confirmed Sales atomically creates:
- Allocation Revision 0 + immutable Revision Items
- current official Sales Allocation pointers
- SALES_ISSUE
- Receivable

Allocation persistence:
- `sales_allocation_revision_items` = immutable historical allocation truth
- `sales_allocations` = pointer-only current official projection
- Inventory Movement allocation lineage points to immutable Revision Items
- correction appends new Revision + Items, creates compensating Inventory Movements, and replaces current pointers

## 10. Outsourced

Outsourced Vendor is separate from Supplier.

Confirmed detail immediately creates:
- OUTSOURCED sellable inventory
- Vendor Payable obligation

Outsourced Batch identity remains Supply Date + Outsourced Vendor.

## 11. Labor

Processing Execution + Sales Packaging Work
→ Employee Daily Wage
→ Employee Payable
→ Payment

Processing wage:
- aggregate before multiply
- use configured wage snapshot
- Applied Wage Rate may be overridden at Daily Wage confirmation
- floor final THB amount
- Processing Wage Component keeps Processing Execution Output lineage

Sales Packaging/Handling:
- Sales + Work Date + Employee + Work Item + Wage
- day-rate
- no quantity/kg/box/hour fields in v0.1
- one confirmed Work Record can enter only one Employee Daily Wage

Employee Daily Wage identity:
- Work Date + Employee

Late work after confirmed Daily Wage remains TO VERIFY.

## 12. Finance

Original Obligation
+ Adjustments
- Settlements
= Outstanding

Finance truth:
- obligation items
- adjustments
- payments / receipts

Outstanding is only a rebuildable transactional projection.
Source transactions do not store paid/outstanding state.

Payable kinds:
- Procurement Supplier
- Procurement Farmer
- Company Pickup Transport
- Outsourced Vendor
- Employee Daily Wage

Supplier/Farmer procurement payable:
- Supplier aggregates by Procurement Batch + Supplier
- Farmer aggregates by Procurement Batch + Farmer
- confirmed Procurement Entries append obligation items

Relational consolidation:
- `finance.payable_obligation_items` is one flat typed-FK relation
- source shape / source existence / source uniqueness / Payable-kind compatibility are DB structural constraints
- cross-row semantic alignment between source facts and Payable owner is revalidated by owning command transaction
- original obligation amount is `>= 0`
- Payment/Receipt amounts remain `> 0`

Supplier quality/weight deduction:
- Payable Adjustment
- never rewrite Procurement

Company Pickup Transport:
- confirmed Company Pickup Procurement Entry creates per-entry transport obligation basis
- no Driver Master
- no Transport Charge aggregate in v0.1
- no Transport Rate Master is assumed
- applied THB/kg rate is stored as confirmed historical fact
- final amount rounding, grouping/confirmation boundary, and payee semantics remain TO VERIFY through FIN-007/008/009

Partial settlements are supported.
Payment / Receipt / Adjustment concurrency uses the applicable Outstanding Position `row_version` boundary.
Confirmed finance facts do not expose generic edit/delete.

Outstanding row formula may be constrained, but no permanent `outstanding >= 0` CHECK exists while FIN-001, FIN-002, and FIN-004 remain unresolved Business Rules.

## 13. Batch close

Procurement Batch:
- all sellable inventory for batch must be zero
- create BATCH_RECONCILIATION for every remaining positive/negative position
- validate whole batch inventory = zero
- mark Closed

Outsourced Batch:
- all sellable inventory = zero
- mark Closed
- no processing reconciliation

Whether close is automatic after sold-out or manually confirmed remains TO VERIFY.

## 14. Data lifecycle / correction / audit

Edit / Soft Delete / Restore / Hard Delete are not generic CRUD.

Confirmed transaction correction:
- dependency assessment
- direct amendment only when safe
- compensating facts when necessary
- block impossible history
- atomic commit
- audit
- idempotency

Hard Delete:
- highest authority only
- owning-domain dependency checked
- no silent cascade
- explicit physical delete
- hard-delete audit retained in same transaction
- audit does not automatically retain a full deleted-row copy

Audit:
- append-oriented and business-command-oriented
- not a generic EF/database row mirror
- rebuildable projections are not normally separate audit truth
- correction audit lineage uses `audit.correction_links`
- Audit subject locator is immutable historical non-FK metadata under ADR-005
- Audit locator must not become a generic entity resolver

## 15. Relational persistence baseline

- PostgreSQL 18
- single database
- module schemas
- 55 relations in consolidated v0.1 relational baseline
- UUID v7 technical IDs
- business unique constraints only where confirmed
- `date` for business dates
- `timestamptz` for system timestamps
- exact `numeric` prices/rates/quantities; no invented precision/scale
- processing max-one-decimal measurements validated without silent DB rounding
- integer THB (`bigint`) amounts where confirmed
- typed real foreign keys instead of unconstrained type+id
- selected stable composite FKs for high-value membership integrity
- cross-row/lifecycle aggregate invariants remain owning-command transaction responsibility
- explicit `row_version bigint` where a row owns a mutable invariant
- `system.accounts` = minimal actor identity FK anchor; AuthN/AuthZ persistence remains separate
- persistent idempotency through `system.command_executions`
- CommandId PK = duplicate-command concurrency boundary
- transactional outbox with at-least-once delivery
- outbox worker concurrency = PostgreSQL row locks + lease
- append-oriented audit
- Audit/Outbox CommandId = correlation snapshots, no FK to CommandExecution
- idempotency/outbox/audit retention lifecycles decoupled
- core FKs default RESTRICT/NO ACTION
- operational/core referencing FKs require appropriate indexes

## 16. Full relational consolidation v0.1

Integrated relation count: **55**.

Schemas / relation counts:
- system 3
- party 5
- infrastructure 3
- product 3
- processing_config 6
- procurement 2
- processing 3
- outsourced 2
- sales 5
- inventory 3
- sales_handling 2
- labor 4
- finance 11
- audit 3

Formal persistence corrections adopted during consolidation:
1. Finance original obligation `> 0` → `>= 0`.
2. Payable obligation source subtype tables collapse into flat typed-FK `payable_obligation_items`.
3. Sales Allocation Revision Items are immutable historical allocation truth.
4. Inventory Position typed identity uses `UNIQUE NULLS NOT DISTINCT`.
5. Add signed non-zero `SALES_ALLOCATION_ADJUSTMENT` movement vocabulary.
6. Use high-value stable Route/Version/Material composite FK membership integrity.
7. `sales_allocations` is pointer-only current official projection.
8. Do not use nullable Batch Route-Version as composite principal key; validate batch/execution Route Version equality transactionally.

DDL dependency order:
- schemas
- system
- party
- infrastructure
- product
- processing_config
- procurement
- processing
- outsourced
- sales
- inventory
- sales_handling
- labor
- finance
- audit
- secondary/partial indexes and final late constraints

No circular aggregate ownership currently requires a special two-phase FK workaround.

## 17. EF Core Mapping Architecture v0.1

Write persistence:
- one scoped write `ErpDbContext`
- EF Core/Npgsql only in Infrastructure
- Fluent API mapping only
- mapping organized per module/relation
- explicit PostgreSQL snake_case table/column names
- no automatic naming-convention dependency

Value generation/types:
- internal ERP IDs generated application-side with UUID v7 before persistence
- externally supplied CommandId is not regenerated
- `Guid`/`DateOnly`/`DateTimeOffset`/`long`/`decimal` map to the relational baseline
- technical JSON payloads use `jsonb` with a technical CLR JSON representation

Concurrency:
- `row_version bigint` maps with `.IsConcurrencyToken()`
- do not use Npgsql `xmin` / `.IsRowVersion()`
- technical interceptor may increment row_version only
- business commands use tracked-first mutation
- detached graph `DbContext.Update()` is not a business update interface
- any write persistence failure invalidates the current `ErpDbContext` for retry
- whole-command retry uses fresh context + same CommandId

Relationships/query behavior:
- core relationships use Restrict/No Action
- high-value alternate/composite keys only where structurally useful
- no lazy loading
- no global soft-delete query filter
- no generic soft-delete interceptor
- no Generic Repository
- query services may use EF `AsNoTracking` projection, Dapper, or native SQL

PostgreSQL-specific operations allowed where the atomic primitive matters:
- CommandId acquisition: `ON CONFLICT DO NOTHING`
- Finance monetary CAS: expected row_version + current Outstanding predicate
- Outbox dequeue: row locking / `FOR UPDATE SKIP LOCKED` + lease

Transactions/retry:
- one explicit PostgreSQL transaction per persisted Business Command
- normally READ COMMITTED unless an owning design requires otherwise
- multiple successful SaveChanges within that transaction are allowed
- automatic write `EnableRetryOnFailure()` is OFF in v0.1

Migrations:
- dedicated `YowThi.Erp.Infrastructure.Migrations` project
- `IDesignTimeDbContextFactory<ErpDbContext>` for tooling
- one migration stream for one write context
- migration history at `system.__ef_migrations_history`
- initial implementation migration = `InitialV01` after all 55 relations are mapped
- production API does not call `Database.Migrate()` at startup
- no operational business master seeding through `HasData()`

InitialV01 acceptance requires:
- EF model metadata tests
- generated migration static review against `docs/10`
- no pending model/migration drift
- clean PostgreSQL 18 apply/schema inspection
- typed constraint tests
- row-version conflict tests
- Finance CAS concurrency test
- CommandId race test
- Inventory concurrency/identity tests
- Outbox `SKIP LOCKED` tests

## 18. REST/API Architecture v0.1

HTTP baseline:
- ASP.NET Core 10 Minimal APIs
- module `MapGroup` composition
- `/api/v1`
- `TypedResults`
- first-party validation / Problem Details / OpenAPI
- HTTP DTOs are separate from Application Commands, Domain entities, and EF entities
- explicit business-intent command endpoints; no generic command/entity mutation API

Idempotency:
- all Business Writes require `Idempotency-Key` UUID
- canonical hash includes Command Type, route business IDs, semantic body fields, and expected concurrency tokens
- Authorization, locale, trace metadata do not affect request hash
- replay check occurs before current-state/row-version lookup
- same actor + same type + same hash returns stored committed result
- different actor/type/hash with same key returns `409 idempotency.key-reused`
- persisted command results are locale-neutral
- a new semantic attempt/version requires a new Idempotency Key

Concurrency:
- versioned target mutations use explicit `expectedRowVersion`
- Finance settlement uses `expectedOutstandingVersion`
- no ETag/If-Match concurrency contract in v0.1
- internal Inventory concurrency remains internal to owning commands
- stale/current-state concurrency failures use stable `409` Problem Details codes

Errors/validation:
- consistent Problem Details with stable machine `code`
- `400` = request/JSON/transport contract invalid
- `409` = current state/concurrency/idempotency conflict
- `422` = structurally valid but semantically invalid command input
- `429` = rate limit
- `500` = unexpected server failure; no SQL/stack details to client
- write DTOs reject unknown fields rather than silently ignoring them

Lifecycle/correction:
- explicit Soft Delete / Restore command routes
- explicit target-specific Data Protection Hard Delete routes
- no generic `(type,id)` hard-delete resolver
- confirmed transaction correction only through owning-domain correction commands
- no generic JSON Patch / Undo / reversal endpoint
- unresolved FIN-003/FIN-005 do not receive invented APIs

Queries/localization:
- read endpoints return dedicated projections
- opaque cursor pagination + endpoint-defined filter/sort allowlists
- no OData/generic expression DSL in v0.1
- `Accept-Language` supports `zh-TW` and `th-TH` operational display resolution
- missing requested localized name falls back to the other existing name
- deployment-configured default locale handles unsupported/absent preference
- master/edit DTOs expose both bilingual source fields where needed

Security/authorization:
- `/api/v1` business endpoints require authentication by default
- actor is resolved server-side to `system.accounts`; clients never select persisted actor identity
- operation capability policies such as `sales.confirm` / `finance.pay` / `data-protection.hard-delete`
- Role/Permission persistence and JWT/Cookie/IdP choice remain deferred to AuthN/AuthZ implementation architecture
- development-only no-password access is forbidden in staging/production

Transactions/cancellation:
- endpoint does not own PostgreSQL transaction
- Application Command Executor owns new-command transaction flow
- external side effects occur only after commit through Outbox
- client disconnect is not proof of rollback
- unknown commit result -> dispose context and replay whole command with same Idempotency Key

Production controls:
- no plaintext HTTP Business API
- trusted forwarded headers only from configured proxy/network
- CORS disabled unless deployment requires it; then explicit origin allowlist
- rate-limiting infrastructure enabled with deployment/load-tested thresholds
- finite request-body limit
- production request/response body logging off by default
- OpenAPI not publicly exposed in production by default
- health endpoints expose minimum liveness/readiness only

OpenAPI/testing:
- stable explicit endpoint OperationIds
- endpoint metadata tests
- semantic generated OpenAPI tests
- business-write tests cover idempotency, replay ordering, concurrency, Problem Details, lifecycle, locale, and authorization contracts

## 19. Important unresolved business gaps

Must be confirmed before affected functionality goes live:
- Completed Procurement Batch late entry policy
- Processing input location selection when multiple locations exist
- Sales issue location selection when stock spans locations
- automatic vs manual batch close
- Sales Handling allowed Sales lifecycle state
- late work after Employee Daily Wage confirmation
- overpayment
- over-collection
- settlement correction/reversal
- payable deduction causing negative Outstanding
- payable adjustment correction/reversal
- Company Pickup Transport final THB rounding rule
- Company Pickup Transport payable grouping / confirmation boundary
- Company Pickup Transport payee recording semantics

Safe/deferred items retained in Gap Register include:
- multiple same day-rate Handling records with no business unique constraint yet
- additional finance adjustment types
- Multi-input Processing Module
- Sales negative inventory/presales
- closed batch reopen
- multi-active route selection

Relational consolidation, EF Core Mapping Architecture, and REST/API Architecture introduce no new Business Rule gaps.

## 20. Next step

Continue with:

**Implementation Sequencing / Build Plan v0.1**

Expected scope:
- solution/project scaffolding order
- package/dependency setup
- shared technical primitives
- Domain/Application implementation sequence
- persistence mapping sequence for all 55 relations
- API vertical-slice sequence
- architecture/model/contract test sequence
- initial Business Command slices
- `InitialV01` migration readiness gate
- PostgreSQL 18 integration/concurrency testing sequence
- CI/build/test gates
- Docker Desktop activation point

After implementation planning:
- scaffold actual .NET/React solution as scheduled
- implement EF Core entities/configurations
- generate/review `InitialV01`
- start PostgreSQL 18 via Docker Desktop for real migration/integration/concurrency tests

Docker Desktop may remain stopped until the implementation/migration testing stage actually requires PostgreSQL runtime.
