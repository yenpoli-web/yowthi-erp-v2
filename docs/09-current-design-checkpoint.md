# Current Design Checkpoint — YowThi ERP V2

Checkpoint status: **v0.1 design baseline**
Purpose: recover the project design if conversational context is lost.

## 1. Highest-level rule

Business Rules come from real YowThi operations.
ERP Control Governance is separate from Business Rules.
AI/developers must not invent Business Rules for technical convenience.

For relational implementation details, `docs/10-relational-model-consolidation-v0.1.md` is the integrated DDL/EF mapping baseline after Parts 1–6. Earlier domain/business documents remain authoritative for Business Facts.

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
- End-to-end business chains
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

Costing/margin and reporting remain DEFERRED until architecture is complete.

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
- output quantity is source deduction quantity

FINAL_PACKAGING:
- completed Sales Product quantity
- source deduction = qty × Packaging Weight
- source may go negative
- Packaging Weight and Sales Weight are distinct

Processing Route/Version/Material structural membership uses selected high-value composite FK integrity, but Batch bound Route Version equality at Processing confirmation remains a transaction invariant because Batch route binding is nullable before processing.

## 8. Inventory

Inventory identity:
- Origin
- Source Batch
- Inventory Object
- Storage Location
- optional Raw Source Segment

Inventory Movement = append-oriented ledger truth.
Inventory Position = transactional rebuildable current projection.

The full nullable typed Inventory Position identity uses PostgreSQL `UNIQUE NULLS NOT DISTINCT` so null typed dimensions do not permit duplicate logical positions.

No separate raw/semi/finished inventory systems.
No globally unrestricted negative inventory.

Allocation correction inventory effects use signed `SALES_ALLOCATION_ADJUSTMENT` movements under a `SALES_ALLOCATION_REVISION` operation. Prior Inventory Movements are never rewritten.

## 9. Sales

Sales:
- Date + Customer header
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

Allocation persistence after consolidation:
- `sales_allocation_revision_items` = immutable historical allocation truth
- `sales_allocations` = pointer-only current official projection
- Inventory Movement allocation lineage points to immutable Revision Items
- correction appends a new Revision + Items, writes compensating inventory movements, and replaces current pointers

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
- `finance.payable_obligation_items` is one flat typed-FK relation rather than one subtype table per source
- local source shape / source existence / source uniqueness / Payable-kind compatibility are DB structural constraints
- cross-row semantic alignment between source facts and Payable owner is revalidated by owning command transaction
- original obligation amount is `>= 0`; Payment/Receipt settlement amounts remain `> 0`

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

Outstanding row formula may be constrained, but no permanent `outstanding >= 0` CHECK is introduced while FIN-001, FIN-002, and FIN-004 remain unresolved Business Rules.

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
- Audit subject locator is immutable historical non-FK metadata under ADR-005 and must not become a generic entity resolver

## 15. Persistence baseline

- PostgreSQL 18
- single database
- module schemas
- one write `ErpDbContext` v0.1
- UUID v7 technical IDs; internal generation can use PostgreSQL 18 UUID v7 support
- 55 relations in consolidated v0.1 relational baseline
- business unique constraints only where confirmed
- `date` for business dates
- `timestamptz` for system timestamps
- exact `numeric` prices/rates/quantities; do not invent precision/scale where unconfirmed
- processing max-one-decimal measurements are validated without silent DB rounding
- integer THB (`bigint`) amounts where confirmed
- typed real foreign keys instead of unconstrained type+id
- selected stable composite FKs for high-value membership integrity
- cross-row/lifecycle aggregate invariants remain owning-command transaction responsibility
- `row_version bigint` optimistic concurrency only where row owns a mutable invariant
- `system.accounts` is minimal actor identity FK anchor; AuthN/AuthZ persistence remains separate
- persistent idempotency through `system.command_executions`
- CommandId PK is duplicate-command concurrency boundary
- transactional outbox with at-least-once delivery
- outbox worker concurrency uses PostgreSQL row locks + lease, not row_version
- append-oriented audit
- Audit/Outbox CommandId values are correlation snapshots and do not FK to CommandExecution
- idempotency, outbox, and audit retention lifecycles are decoupled
- core FKs default to RESTRICT/NO ACTION
- core operational referencing FKs require appropriate indexes

## 16. PostgreSQL schema Parts 1–6

Part 1:
- party masters
- containers / warehouses / locations
- Procurement Product
- Sales Product Group / Sales Product
- Processing Route / Version / Module / Process Material / Outputs

Part 2:
- Procurement Batch
- Procurement Entry
- typed Supplier/Farmer source
- route version binding
- amount/unit snapshots
- company pickup fact

Part 3:
- Processing Execution
- input/output measurement facts
- container snapshots
- wage snapshots
- Inventory Operation
- Inventory Movement
- Inventory Position

Part 4:
- Outsourced Supply Batch / Detail
- Sales / Details
- Sales Allocation
- Allocation Revision boundary
- OUTSOURCED_RECEIPT
- SALES_ISSUE integration

Part 5:
- Sales Packaging Item / Work Record
- Employee Daily Wage
- Processing Wage Component + source lineage
- Sales Packaging Wage Component
- typed Payable / obligation items
- Company Pickup Transport Basis + obligation lineage
- Payable Adjustment / Payment
- Receivable / obligation items / Receipt
- Payable/Receivable Outstanding projections
- Finance concurrency boundary

Part 6:
- `system.accounts`
- `system.command_executions`
- `system.outbox_messages`
- `audit.audit_events`
- `audit.audit_event_subjects`
- `audit.correction_links`
- Hard Delete audit transaction pattern
- system concurrency/retention boundaries

## 17. Full relational consolidation v0.1

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

These are persistence/relational consistency corrections, not new Business Rules.

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

## 18. Important unresolved business gaps

Must be confirmed before relevant go-live:
- Completed Procurement Batch late entry policy
- Processing input location selection when multiple locations exist
- Sales issue location selection when multiple locations exist
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

Relational consolidation introduces no new Business Rule gaps.

## 19. Next step

Continue with:

**EF Core Mapping Architecture v0.1**

Expected scope:
- solution/project persistence boundaries
- one write `ErpDbContext`
- module schema/table mappings
- UUID v7 value generation strategy
- explicit `row_version` concurrency token mappings
- alternate/composite keys and composite FKs
- CHECK constraint / unique / partial-index migration strategy
- PostgreSQL-specific `UNIQUE NULLS NOT DISTINCT` migration support
- typed discriminator mapping/validation
- per-module `IEntityTypeConfiguration` structure
- query filters vs historical-reference requirements
- transaction / execution-strategy boundaries
- migration ordering for all 55 relations

Docker Desktop / PostgreSQL runtime is not required during this architecture-document phase. It becomes necessary when the first real EF Core migrations are implemented/executed and PostgreSQL integration/concurrency tests begin.

After EF Core Mapping Architecture:
- REST/API architecture
- then implementation/migrations/integration testing according to the approved architecture sequence
