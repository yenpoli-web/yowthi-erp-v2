# Current Design Checkpoint — YowThi ERP V2

Checkpoint status: **v0.1 implementation baseline through P3.5**
Purpose: recover the project design and current implementation state if conversational context is lost.

## 1. Highest-level rule

Business Rules come from real YowThi operations.
ERP Control Governance is separate from Business Rules.
AI/developers must not invent Business Rules for technical convenience.

Precedence for implementation recovery:
- Business Facts / Business Rules: Business Discovery, Command Contracts, Gap Register, owning-domain documents
- relational implementation: `docs/10-relational-model-consolidation-v0.1.md`
- EF Core/Npgsql implementation architecture: `docs/11-ef-core-mapping-architecture-v0.1.md`
- HTTP/REST implementation architecture: `docs/12-rest-api-architecture-v0.1.md`
- implementation order/readiness gates: `docs/13-implementation-sequencing-build-plan-v0.1.md`
- GitHub cost governance: `docs/14-github-cost-governance-v0.1.md`
- AuthN/AuthZ implementation/security architecture and auth-specific `system.accounts` correction: `docs/15-authn-authz-implementation-architecture-v0.1.md`

Earlier Part 1–6 schema notes remain design history. `docs/10` is the later consolidated relational baseline when relational details conflict.

For the auth-specific external identity binding only, `docs/15` is a later confirmed correction to older wording in `docs/10` section 6 and PostgreSQL Schema Part 6 section 2 that external identity mapping was not yet defined. All other `docs/10` relational rules remain authoritative.

## 2. Legacy safety

`C:\yowthi-erp` is protected Legacy ERP and is read-only.
Legacy H01/H02/H03 and old schema are references only.
They do not drive the new Domain Model.

Never modify, delete, move, reset, overwrite, or use Legacy schema as ERP V2 architecture.

## 3. Completed design and implementation stages

Completed architecture/design to v0.1:
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
- PostgreSQL Schema Parts 1–6
- ADR-005 Audit Reference Boundary
- Full Relational Model Consolidation v0.1
- EF Core Mapping Architecture v0.1
- REST/API Architecture v0.1
- Implementation Sequencing / Build Plan v0.1
- GitHub Cost Governance v0.1
- AuthN/AuthZ Implementation Architecture v0.1

Completed implementation phases:
- P0 repository / .NET / React scaffolding
- P1 shared technical foundation
- P2 full 55-relation EF model through M7
- P3 API technical shell
- P3.5 AuthN/AuthZ architecture hard gate and auth-specific `system.accounts` mapping revision

Current next phase:

**P4 — generate and statically review `InitialV01`.**

Docker Desktop remains OFF through P4 migration generation/static review. It is required at P5 first real PostgreSQL 18 apply/integration/concurrency acceptance.

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
Batch-bound Route Version equality at Processing confirmation remains a transaction invariant because Batch route binding is nullable before processing.

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

Procurement receipt location safe handling under PROC-002:
- `ConfirmProcurementEntry` accepts optional Receipt Storage Location / API `receiptStorageLocationId`
- explicit location is validated and used
- omitted location uses the unique applicable default when one can be resolved
- omitted location with no unique applicable default blocks confirmation and requires explicit choice

This is an existing safe v0.1 control, not a new Business Rule.

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
- 55 relations / 14 schemas
- UUID v7 technical IDs
- business unique constraints only where confirmed
- `date` for business dates
- `timestamptz` for system timestamps
- exact `numeric` prices/rates/quantities; no invented precision/scale
- integer THB (`bigint`) amounts where confirmed
- typed real foreign keys instead of unconstrained type+id
- selected stable composite FKs for high-value membership integrity
- cross-row/lifecycle aggregate invariants remain owning-command transaction responsibility
- explicit `row_version bigint` where a row owns a mutable invariant
- persistent idempotency through `system.command_executions`
- CommandId PK = duplicate-command concurrency boundary
- transactional outbox with at-least-once delivery
- outbox worker concurrency = PostgreSQL row locks + lease
- append-oriented audit
- Audit/Outbox CommandId = correlation snapshots, no FK to CommandExecution
- idempotency/outbox/audit retention lifecycles decoupled
- core FKs default RESTRICT/NO ACTION
- operational/core referencing FKs require appropriate indexes

`system.accounts` remains the actor identity FK anchor and now includes the confirmed P3.5 external authentication binding:
- `identity_issuer text NULL`
- `identity_subject text NULL`
- both NULL or both present
- present values nonblank
- partial unique `(identity_issuer, identity_subject)` when identity is present

This does not add relation 56+.

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
9. P3.5 auth-specific correction: add optional paired/unique OIDC `(identity_issuer, identity_subject)` to existing `system.accounts`; relation count unchanged.

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
- production API does not call `Database.Migrate()` at startup
- no operational business master seeding through `HasData()`

## 18. REST/API Architecture and P3 technical shell

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

Errors/validation:
- consistent Problem Details with stable machine `code`
- `400` = request/JSON/transport contract invalid
- `409` = current state/concurrency/idempotency conflict
- `422` = structurally valid but semantically invalid command input
- `429` = rate limit
- `500` = unexpected server failure; no SQL/stack details to client
- write DTOs reject unknown fields

Lifecycle/correction:
- explicit Soft Delete / Restore command routes
- explicit target-specific Data Protection Hard Delete routes
- no generic `(type,id)` hard-delete resolver
- confirmed transaction correction only through owning-domain correction commands
- no generic JSON Patch / Undo / reversal endpoint
- unresolved FIN-003/FIN-005 do not receive invented APIs

Queries/localization:
- dedicated read projections
- opaque cursor pagination + endpoint-defined filter/sort allowlists
- no OData/generic expression DSL
- `Accept-Language` supports `zh-TW` and `th-TH`
- deployment-configured default locale handles unsupported/absent preference

P3 implemented:
- Minimal API composition root
- authenticated-by-default `/api/v1` metadata boundary
- module route vocabulary
- Problem Details
- first-party OpenAPI / validation
- strict camelCase JSON with unknown-property rejection
- Idempotency-Key transport filter
- locale resolver
- capability policy-name constants
- rate-limit/request-size infrastructure
- minimal anonymous liveness endpoint
- API contract tests

## 19. AuthN/AuthZ Implementation Architecture v0.1

P3.5 is formally confirmed.

Authentication:
- provider-neutral external OpenID Connect
- interactive Authorization Code + PKCE
- ASP.NET Core encrypted Cookie session
- provider/authority/client configuration is deployment-specific
- ERP does not store passwords
- React does not store access/refresh tokens
- no `offline_access` / refresh-token persistence for ordinary ERP login

API behavior:
- unauthenticated `/api/v1` request returns 401 rather than redirecting the business request
- interactive login uses explicit auth route such as `/auth/login`
- OIDC callback remains protocol infrastructure

Persistent identity:
- validated exact `(issuer, subject)` maps to `system.accounts(identity_issuer, identity_subject)`
- email/display name/phone/Employee are not authentication keys
- clients never supply authoritative actor account IDs
- account must exist and remain `active = true`

Provisioning:
- pre-provision only in v0.1
- first successful OIDC login does not auto-create ERP accounts
- initial account uses explicit host-side maintenance/bootstrap mechanism, never an anonymous deployed HTTP bypass

Authorization:
- explicit capability policies, e.g. `sales.confirm`, `finance.pay`, `inventory.adjust`, `data-protection.hard-delete`
- v0.1 grants are deployment-configured by persistent Account UUID
- no `Admin`, `Manager`, `SuperAdmin` business-role invention
- no roles/permissions/account-role tables in v0.1

CSRF:
- cookie-authenticated unsafe browser requests require ASP.NET Core antiforgery
- React request header: `X-CSRF-TOKEN`
- `Idempotency-Key` does not replace antiforgery

Test auth:
- test-only scheme allowed in contract/integration test composition
- never staging/production no-password authentication

Tailscale remains network transport only, not ERP authentication.

See `docs/15-authn-authz-implementation-architecture-v0.1.md`.

## 20. Implementation Sequencing / Build Plan v0.1

Formal implementation phases:

```text
P0  Repository / solution scaffolding                 COMPLETE
P1  Shared technical foundation                       COMPLETE
P2  Full 55-relation Domain + EF model               COMPLETE
P3  API technical shell                               COMPLETE
P3.5 AuthN/AuthZ implementation architecture hard gate COMPLETE
P4  InitialV01 generation/static review               NEXT
P5  PostgreSQL 18 persistence acceptance
P6  Business vertical slices
P7  React UI vertical slices
P8  CI / production hardening
```

55-relation mapping batches:
- M1 system + party = 8
- M2 infrastructure + product + processing_config = cumulative 20
- M3 procurement + processing = cumulative 25
- M4 outsourced + sales + inventory = cumulative 35
- M5 sales_handling + labor = cumulative 41
- M6 finance = cumulative 52
- M7 audit = 55

P4 rules:
- generate one formal migration named `InitialV01`
- dedicated `YowThi.Erp.Infrastructure.Migrations` project
- static review generated migration against `docs/10` plus auth-specific `docs/15` correction
- verify no pending EF model changes
- keep migration as focused commit
- do not execute it against PostgreSQL until P5

P5 activation:
- Docker Desktop ON only after P4 static/model-drift gate passes
- PostgreSQL 18 clean apply
- schema introspection
- typed constraint tests
- row-version tests
- Finance CAS concurrency
- CommandId race
- Inventory identity/concurrency
- Outbox SKIP LOCKED/lease tests

First full Business Command vertical slice after persistence acceptance remains `ConfirmProcurementEntry`.

## 21. Important unresolved business gaps

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

AuthN/AuthZ P3.5 introduces no new Business Rule gaps; its decisions are technical/security architecture.

## 22. GitHub validation / cost governance

Routine GitHub-hosted runners are disabled.

Validation branches matching:

```text
m*-validation
```

run automatically only on:

```text
self-hosted
yowthi-erp-v2
```

Formal `main` advances only after actual restore/build/test success.

Do not require GitHub-hosted runners, paid/larger runners, Codespaces, or other metered services that may create cost after quota exhaustion.

## 23. Next step

Proceed with:

**P4 — generate `InitialV01` and perform static migration/model-drift review.**

Required sequence:

```text
P3.5 validated
→ generate InitialV01
→ inspect migration against docs/10 + docs/15
→ verify 55 relations / 14 schemas and expected constraints/indexes
→ verify no pending model changes
→ self-hosted restore/build/test
→ fast-forward main
```

Do not start PostgreSQL runtime acceptance yet.
Docker Desktop remains stopped until P5.
