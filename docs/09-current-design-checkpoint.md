# Current Design Checkpoint — YowThi ERP V2

Checkpoint status: **v0.1 design baseline**
Purpose: recover the project design if conversational context is lost.

## 1. Highest-level rule

Business Rules come from real YowThi operations.
ERP Control Governance is separate from Business Rules.
AI/developers must not invent Business Rules for technical convenience.

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

## 8. Inventory

Inventory identity:
- Origin
- Source Batch
- Inventory Object
- Storage Location
- optional Raw Source Segment

Inventory Movement = truth.
Inventory Position = current projection.

No separate raw/semi/finished inventory systems.
No globally unrestricted negative inventory.

## 9. Sales

Sales:
- Date + Customer header
- 1..N Sales Details

Pricing:
- WEIGHT_BASED_UNIT
- UNIT_BASED

Allocation:
1. OUTSOURCED oldest → newest
2. IN_HOUSE oldest → newest
3. authorized manual override

Final allocation is official.
Allocation total must equal Sales Detail quantity.
Confirmed Sales atomically creates:
- final allocation
- SALES_ISSUE
- Receivable

## 10. Outsourced

Outsourced Vendor is separate from Supplier.
Confirmed detail immediately creates:
- OUTSOURCED sellable inventory
- Vendor Payable

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

Sales Packaging/Handling:
- Sales + Work Date + Employee + Work Item + Wage
- day-rate
- no quantity/kg/box fields in v0.1

## 12. Finance

Original Obligation
+ Adjustments
- Settlements
= Outstanding

Supplier quality/weight deduction:
- Payable Adjustment
- never rewrite Procurement

Partial settlements are supported.

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

## 14. Data lifecycle / correction

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
- dependency checked
- no silent cascade
- hard-delete audit retained

## 15. Persistence baseline

- PostgreSQL 18
- single database
- module schemas
- one write ErpDbContext v0.1
- UUID v7 technical IDs
- business unique constraints
- date for business dates
- timestamptz for system timestamps
- exact numeric prices
- integer THB amounts where confirmed
- typed real foreign keys instead of unconstrained type+id
- row_version optimistic concurrency
- persistent idempotency
- transactional outbox
- append-oriented audit
- RESTRICT/NO ACTION core FKs

## 16. PostgreSQL schema completed so far

Part 1:
- party masters
- containers
- warehouses / locations
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

## 17. Important unresolved business gaps

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

Deferred until real need:
- Multi-input Processing Module
- Sales negative inventory/presales
- closed batch reopen
- multi-active route selection
- additional finance adjustment types

## 18. Next step

Continue with:

**PostgreSQL Schema / Table Design v0.1 — Part 5: `sales_handling` + `labor` + `finance`**

Expected scope:
- Sales Packaging Item
- Sales Packaging Work records
- Employee Daily Wage
- Processing Wage Components
- Payable source typing
- Supplier/Farmer procurement payable
- Company pickup transport payable
- Outsourced Vendor payable
- Employee Wage payable
- Payable Adjustment
- Payment
- Receivable
- Receipt
- Outstanding transactional cache
- finance concurrency and settlement lineage

After Part 5:
- Audit/System persistence
- full relational-model consolidation
- EF Core mapping architecture
- REST/API architecture
