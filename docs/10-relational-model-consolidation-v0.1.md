# Full Relational Model Consolidation v0.1 — YowThi ERP V2

Status: **DECISION / v0.1**
Revision basis: Relational Model Consolidation Revision 0.2 + Final Relational Review, formally confirmed.

## 1. Purpose and precedence

This document consolidates PostgreSQL Schema Parts 1–6 into one DDL/EF-Core-ready relational baseline.

Business facts and business rules continue to come from Business Discovery, Command Contracts, Gap Register, and owning-domain documents. This document does not invent new YowThi Business Rules.

For relational implementation details only, when an earlier Part 1–6 schema note conflicts with this consolidation, this document is the later consolidation baseline. In particular it formalizes several persistence-level corrections discovered only after cross-module review.

Planned relation count: **56 relations** after the approved P8 Security Foundation revision.

## 2. Global PostgreSQL conventions

- PostgreSQL 18.
- Module-separated schemas in one database.
- Technical IDs use `uuid`; new internal IDs use UUID v7 generation.
- Externally supplied command IDs do not use a database default.
- Business dates use `date`.
- System timestamps use `timestamptz` and are persisted in UTC.
- Confirmed THB amounts use `bigint`.
- Prices, rates, weights, and quantities use exact `numeric`; no floating-point business values.
- Domain codes/status/discriminators use `text` plus explicit named CHECK constraints in v0.1 rather than PostgreSQL enum types.
- Core FKs default to `RESTRICT / NO ACTION`.
- Operational/core referencing FKs require an appropriate B-tree index unless already covered by PK/UNIQUE/index leading columns.
- `row_version bigint` is explicit optimistic concurrency only where the row owns a mutable invariant.
- Audit and Outbox do not use `row_version`.

Canonical concurrency column:
- `row_version bigint NOT NULL DEFAULT 1`
- CHECK `row_version >= 1`

## 3. Numeric integrity

Business numeric values must reject PostgreSQL special numeric values where inappropriate.

Non-negative numeric fields use finite + `>= 0` validation.
Signed inventory deltas use finite non-zero/sign-specific validation according to movement type.

Processing measurements governed by the confirmed maximum-one-decimal rule must be validated without silently rounding submitted data. Use explicit CHECK semantics equivalent to `value = round(value, 1)` rather than relying on a constrained numeric scale that may round input.

Precision/scale should not be invented where the business has not confirmed it. Unconstrained exact `numeric` remains valid for those values.

## 4. Common lifecycle / actor metadata

All `*_by_account_id` fields target `system.accounts(id)` with real FKs.

Paired lifecycle metadata must remain structurally consistent, including:
- `deleted_at` + `deleted_by_account_id`
- `confirmed_at` + `confirmed_by_account_id`
- closing/completion timestamp + operator pairs where applicable

Soft-delete identity constraints do not automatically release confirmed business identities unless an owning business rule explicitly says so.

Localized visible master names require at least one nonblank value between:
- `name_zh_tw`
- `name_th_th`

## 5. Constraint responsibility layers

### Single-row structural CHECK
Use for:
- discriminator + nullable-real-FK typed shape
- status/metadata-pair consistency
- sign / finite numeric rules
- conditional fields such as Sales Weight
- calculated row-local formulas where all source values are in the same row

### FK / UNIQUE structural integrity
Use for:
- source existence
- stable route/version membership
- business identities already confirmed
- line/revision sequence uniqueness
- source fact one-to-one lineage

### Transactional aggregate invariant
Use owning-domain command transactions for cross-row, cross-lifecycle, or aggregate rules such as:
- Sales Allocation sum = Sales Detail quantity
- Inventory Position = Movement ledger
- Employee Daily Wage component totals = wage header totals
- Company Pickup basis applied sum = transport obligation quantity
- Finance source/payable semantic alignment
- Outstanding update concurrency and currently blocked over-settlement cases
- Procurement Batch route binding at processing time
- route graph validation
- Hard Delete dependency assessment

Do not introduce a generic trigger framework solely to force these aggregate invariants into row-level constraints.

## 6. System schema

### `system.accounts`
Persistent ERP actor/account anchor:
- `id uuid` PK
- `display_name text`
- `active boolean`
- `identity_issuer text NULL`
- `identity_subject text NULL`
- `row_version bigint`
- `created_at timestamptz`

`identity_issuer` and `identity_subject` are structurally paired: both null or both present and nonblank. When present, `(identity_issuer, identity_subject)` is unique and identifies the external login identity bound to the ERP account.

No password, password hash, MFA secret, durable session, generic role, or Employee linkage is stored in `system.accounts`.

### `system.account_capability_grants`
Persistent explicit authorization grants:
- `id uuid` PK
- `account_id uuid` FK -> `system.accounts(id)`
- `capability_name text NOT NULL`
- `active boolean NOT NULL`
- `row_version bigint`
- `created_at timestamptz NOT NULL`
- `created_by_account_id uuid` FK -> `system.accounts(id)`

Structural rules:
- unique `(account_id, capability_name)`
- capability name must be nonblank
- both Account FKs use Restrict / No Action semantics
- authorization uses explicit capability identifiers such as `finance.pay`, `party.customer.lifecycle`, and `data-protection.hard-delete`; no wildcard grant or generic administrator role is introduced

Capability grants are technical ERP authorization state, not YowThi Business Rules.

### `system.command_executions`
Persistent idempotency:
- `command_id uuid` PK
- `command_type text NOT NULL`
- `request_hash bytea NOT NULL`
- `status` = `IN_PROGRESS` / `SUCCEEDED`
- `result_payload jsonb NULL`
- `actor_account_id uuid` FK NOT NULL
- `started_at timestamptz NOT NULL`
- `executed_at timestamptz NULL`

Checks:
- SHA-256 request hash length = 32 bytes
- `SUCCEEDED` requires `executed_at`
- `IN_PROGRESS` has no `executed_at`

No durable normal `FAILED` state is required because failed commands roll back the idempotency row with business work.

### `system.outbox_messages`
- stable UUID message id
- type/version/payload
- optional `command_id` correlation only, no FK
- occurrence/availability/published timestamps
- retry count / next attempt
- lease token / lease expiration
- sanitized last-error summary

Delivery is at-least-once. Outbox worker concurrency uses PostgreSQL row locks + lease, not `row_version`.

## 7. Party schema

Relations:
- `party.suppliers`
- `party.farmers`
- `party.employees`
- `party.customers`
- `party.outsourced_vendors`

No Universal Party relation.

Supplier/Farmer/Employee/Outsourced Vendor retain bilingual name plus bank/phone/address attributes already confirmed. Customer retains bilingual name/phone. No unconfirmed name/phone/bank uniqueness is added.

## 8. Infrastructure schema

### `infrastructure.containers`
Bilingual master with finite non-negative `tare_weight`, active/lifecycle/concurrency metadata.

### `infrastructure.warehouses`
Optional code + bilingual master metadata.

### `infrastructure.storage_locations`
- `warehouse_id` FK
- optional code
- bilingual master metadata

No unconfirmed business uniqueness for optional warehouse/location codes is imposed in v0.1.

## 9. Product schema

### `product.procurement_products`
- bilingual name
- nonblank `unit_code`
- optional default storage location
- active/lifecycle/concurrency metadata

### `product.sales_product_groups`
Bilingual group master; not inventory.

### `product.sales_products`
- Sales Product Group FK
- bilingual name
- `pricing_basis`
- optional/required weight snapshots according to basis
- optional default storage location

Typed pricing CHECK:
- `WEIGHT_BASED_UNIT` -> `sales_weight` required
- `UNIT_BASED` -> `sales_weight` null

`packaging_weight`, when present, is finite and positive.
Sales Product does not store Origin.

## 10. Processing configuration schema

### `processing_config.processing_routes`
- Procurement Product FK
- bilingual name
- lifecycle/concurrency metadata

### `processing_config.processing_route_versions`
- Route FK
- positive `version_number`
- status `DRAFT / VALIDATED / ACTIVE / RETIRED`
- unique `(processing_route_id, version_number)`
- one ACTIVE version per Route using partial unique index
- alternate structural key `(id, processing_route_id)` where needed by composite FK

### `processing_config.route_input_configs`
One-to-one PK/FK with Route Version.
Container fields are conditional on `uses_container`.

### `processing_config.process_materials`
Route-Version-owned intermediate inventory object.
Use alternate structural key `(id, processing_route_version_id)` for same-version membership FKs.

### `processing_config.processing_modules`
- Route Version FK
- execution mode
- optional input Process Material
- negative inventory policy

If input Process Material is present, composite membership FK requires it to belong to the same Route Version.

### `processing_config.processing_module_outputs`
- Processing Module FK
- Route Version membership
- positive output sequence
- output kind `PROCESS_MATERIAL / SALES_PRODUCT`
- nullable typed output FKs
- default wage rate
- unique `(processing_module_id, output_sequence)`

Process Material outputs use same-Route-Version composite membership integrity. Sales Product outputs use normal Sales Product FK.

## 11. Procurement schema

### `procurement.procurement_batches`
Business identity:
- unique `(procurement_date, procurement_product_id)`
- identity is not released by soft delete

Core state:
- Procurement status `OPEN / COMPLETED`
- lifecycle `ACTIVE / CLOSED`
- optional Route + Route Version binding, both null or both present
- Route Version must belong to selected Route using composite FK
- completion/closing actor-time pairs
- row version + lifecycle metadata

The rule that route binding becomes fixed once processing begins remains a transaction/lifecycle invariant.

### `procurement.procurement_entries`
- Batch FK
- typed source `SUPPLIER / FARMER`
- real Supplier/Farmer FK shape
- finite non-negative `net_quantity` and `unit_price`
- `unit_code_snapshot`
- `amount_thb`
- `company_pickup`
- recorded actor/time
- correction/lifecycle concurrency support

Row-local formula:
- `amount_thb = floor(net_quantity * unit_price)`

No inventory balance, payable/paid/outstanding, driver, arrival sequence, or Farmers Combined identity is stored here.

## 12. Processing schema

### `processing.processing_executions`
- Work Date / Employee / Procurement Batch
- Route Version / Processing Module
- execution-mode and negative-policy snapshots
- optional SOURCE_TRACKED source kind / Supplier
- recorded actor/time
- row version/lifecycle

Module + Route Version same-membership uses composite structural FK.

Do not use a composite principal FK from Processing Execution to nullable `procurement_batches.processing_route_version_id`. At command time, `ConfirmProcessingExecution` must verify that the batch's bound Route Version equals the execution Route Version.

Typed source rules:
- SOURCE_TRACKED + SUPPLIER -> Supplier present
- SOURCE_TRACKED + FARMERS_COMBINED -> Supplier null
- POOLED_OUTPUT / FINAL_PACKAGING -> no processing-source fields

### `processing.processing_execution_inputs`
One row per Execution (PK/FK).
Consumption basis:
- `SCALE_NET`
- `OUTPUT_QUANTITY`
- `PACKAGING_WEIGHT`

Scale/container snapshot fields are conditional by basis. For `SCALE_NET`, consumed quantity equals derived net quantity.

### `processing.processing_execution_outputs`
- Execution FK
- Processing Module Output FK
- output-kind snapshot
- configured wage-rate snapshot
- Process Material measurement shape or Sales Product completion shape
- unique `(processing_execution_id, processing_module_output_id)`
- one Sales Product output per Final Packaging execution

Final Packaging row formula:
- `source_consumption_quantity = completed_quantity * packaging_weight_snapshot`

## 13. Outsourced schema

### `outsourced.outsourced_supply_batches`
Business identity:
- unique `(supply_date, outsourced_vendor_id)`

Product is not part of batch identity.

### `outsourced.outsourced_supply_details`
- Batch FK
- Sales Product FK
- quantity/pricing snapshots/unit price/amount
- recorded actor/time
- lifecycle/concurrency

Pricing amount follows the same basis semantics as Sales:
- weighted -> floor(quantity * sales_weight_snapshot * unit_price)
- unit-based -> floor(quantity * unit_price)

## 14. Sales schema

### `sales.sales`
- Sales Date + Customer
- status `DRAFT / CONFIRMED`
- confirmation actor/time pair
- row version/lifecycle metadata

Draft has no inventory issue and no Receivable. Confirmed Sales atomically creates official allocation, SALES_ISSUE, and Receivable.

### `sales.sales_details`
- Sales FK
- positive line number
- Sales Product
- quantity / pricing snapshots / amount
- row version/lifecycle metadata
- unique `(sales_id, line_number)`
- alternate key `(id, sales_id)` where needed for same-Sale composite integrity

No uniqueness on `(sales_id, sales_product_id)`; repeated product lines remain valid.

### `sales.sales_allocation_revisions`
Immutable allocation-version header:
- Sale FK
- `revision_number >= 0`
- created actor/time
- optional reason
- unique `(sales_id, revision_number)`
- revision 0 = initial Sales confirmation allocation

### `sales.sales_allocation_revision_items`
Immutable historical allocation truth:
- Revision FK
- Sale FK
- Sales Detail FK
- sequence
- Origin
- typed Procurement Batch / Outsourced Supply Batch
- allocated quantity
- manual override flag

Composite FKs ensure Revision and Detail belong to the same Sale.
Typed Origin CHECK ensures exactly the applicable source-batch FK.
Unique `(revision_id, sales_detail_id, sequence)`.

### `sales.sales_allocations`
Current official allocation is a pointer projection, not duplicated allocation truth.

Columns:
- `sales_detail_id`
- `sequence`
- `sales_allocation_revision_item_id`
- `row_version`

Primary/current identity:
- PK `(sales_detail_id, sequence)`
- unique `sales_allocation_revision_item_id`
- composite FK requires pointed Revision Item to belong to the same Sales Detail

Origin, source batch, allocated quantity, and manual-override values are read from the immutable Revision Item, not duplicated here.

Allocation correction:
- append new immutable Revision + Items
- create compensating Inventory Movements
- replace current `sales_allocations` pointers
- never rewrite prior Inventory Movement history

## 15. Inventory schema

### `inventory.inventory_operations`
Business inventory-operation header.
Operation types:
- `PROCUREMENT_RECEIPT`
- `PROCESSING`
- `TRANSFER`
- `ADJUSTMENT`
- `BATCH_RECONCILIATION`
- `OUTSOURCED_RECEIPT`
- `SALES_ISSUE`
- `SALES_ALLOCATION_REVISION`

Typed source FKs are used where a specific source fact owns the operation. Partial unique indexes protect one-to-one source operations where appropriate.

### `inventory.inventory_movements`
Append-oriented ledger truth.

Columns include:
- Operation + sequence
- movement type
- typed Origin / Source Batch
- typed Inventory Object
- Storage Location
- optional typed Raw Source Segment
- signed `quantity_delta`
- optional `sales_allocation_revision_item_id`
- recorded timestamp

Unique `(inventory_operation_id, sequence)`.

Movement vocabulary:
- `PURCHASE_RECEIPT`
- `PROCESS_CONSUME`
- `PROCESS_PRODUCE`
- `FINAL_PACKAGE_CONSUME`
- `FINAL_PACKAGE_PRODUCE`
- `OUTSOURCED_RECEIPT`
- `TRANSFER_OUT`
- `TRANSFER_IN`
- `SALES_ISSUE`
- `SALES_ALLOCATION_ADJUSTMENT`
- `ADJUSTMENT`
- `BATCH_RECONCILIATION`

Signs:
- receipt/produce/transfer-in > 0
- consume/sales-issue/transfer-out < 0
- allocation-adjustment/adjustment/reconciliation are signed non-zero

`SALES_ISSUE` movements point to the initial allocation Revision Item.
`SALES_ALLOCATION_ADJUSTMENT` movements point to the old Revision Item when restoring old source inventory and to the new Revision Item when applying new source inventory.

### `inventory.inventory_positions`
Transactional rebuildable projection with the same full inventory identity as Movement.

Use `UNIQUE NULLS NOT DISTINCT` across the complete typed identity dimensions:
- Origin
- Procurement/Outsourced Source Batch FKs
- Inventory Object kind + typed object FKs
- Storage Location
- optional Raw Source Segment + Supplier

This prevents duplicate logical positions when nullable typed identity columns are null.

`balance_quantity` may be negative because Final Packaging can legally make its source negative. That does not establish a global negative-inventory rule.

## 16. Sales Handling schema

### `sales_handling.sales_packaging_items`
Bilingual day-rate work-item master. No wage rate on master.

### `sales_handling.sales_packaging_work_records`
- Sales / Work Date / Employee / Work Item
- confirmed wage THB
- recorded actor/time
- row version/lifecycle metadata

No quantity, weight, box count, hours, or unit-rate columns.
No business unique `(sale,date,employee,item)` while HANDLING-002 remains unresolved.

## 17. Labor schema

### `labor.employee_daily_wages`
Business identity:
- unique `(work_date, employee_id)`

Header totals:
- Processing wage total
- Sales Packaging wage total
- total wage
- confirmed actor/time
- row version

Row formula:
- total = processing total + packaging total

### `labor.processing_wage_components`
- Daily Wage FK
- Processing Module Output FK
- configured/applied wage-rate snapshots
- override flag
- aggregated quantity
- amount THB

Row formula:
- `amount_thb = floor(aggregated_quantity * applied_wage_rate)`

### `labor.processing_wage_component_sources`
- Component + Processing Execution Output
- quantity snapshot
- composite PK
- unique Processing Execution Output to prevent double wage inclusion

### `labor.sales_packaging_wage_components`
- Daily Wage
- unique Sales Packaging Work Record
- wage amount

A Work Record can enter only one confirmed Daily Wage.

## 18. Finance schema — Payable

### `finance.payables`
Kinds:
- `PROCUREMENT_SUPPLIER`
- `PROCUREMENT_FARMER`
- `COMPANY_PICKUP_TRANSPORT`
- `OUTSOURCED_VENDOR`
- `EMPLOYEE_DAILY_WAGE`

Nullable real source FKs + typed CHECK enforce the local Payable shape.

Partial business uniques:
- Supplier -> `(procurement_batch_id, supplier_id)`
- Farmer -> `(procurement_batch_id, farmer_id)`
- Outsourced Vendor -> `outsourced_supply_detail_id`
- Employee Wage -> `employee_daily_wage_id`
- Transport -> no permanent business unique while FIN-008 is unresolved

Use alternate structural key `(id, payable_kind)` for obligation-kind compatibility.

### `finance.payable_obligation_items`
Revision consolidation correction: source subtypes are flattened into one typed-FK relation.

Core:
- Payable + Payable Kind
- Obligation Kind
- authoritative `amount_thb`
- typed nullable source FKs
- Transport snapshot fields where applicable
- recorded timestamp

Obligation kinds:
- `PROCUREMENT_ENTRY`
- `OUTSOURCED_SUPPLY_DETAIL`
- `EMPLOYEE_DAILY_WAGE`
- `COMPANY_PICKUP_TRANSPORT`

`amount_thb >= 0`.
This replaces the earlier `> 0` rule because valid zero-valued source/wage calculations must not be made relationally impossible without a confirmed Business Rule.

DB responsibilities:
- local typed shape
- source existence
- source one-to-one uniqueness where applicable
- Payable-kind compatibility

Command-transaction responsibilities:
- Procurement Entry batch/party must align with selected Procurement Payable
- other source facts must semantically align with their Payable owner
- Transport grouping follows FIN-008 when confirmed

Do not duplicate large sets of source identity columns merely to force all cross-row semantic alignment into composite FKs.

### `finance.company_pickup_transport_bases`
- one per qualifying Procurement Entry
- applicable quantity
- recorded timestamp
- no rate, final amount, payee, or Driver Master

### `finance.company_pickup_transport_obligation_basis_items`
M:N lineage between Transport Payable Obligation Item and per-entry Transport Basis.
Applied quantities are non-negative.
At confirmation the aggregate applied quantity must equal the Transport obligation quantity; this is a transaction invariant.

### `finance.payable_adjustments`
v0.1 type:
- `SUPPLIER_QUALITY_WEIGHT_DEDUCTION`
- negative amount delta

No rewrite of Procurement, Inventory, or previous Payments.

### `finance.payments`
- Payable FK
- positive THB amount
- confirmed actor/time
- partial settlement supported

Overpayment remains blocked by v0.1 control while FIN-001 is unresolved; do not encode a permanent nonnegative-Outstanding Business Rule in schema.

## 19. Finance schema — Receivable

### `finance.receivables`
- unique Sales FK
- created timestamp
- row version
- alternate `(id, sales_id)` structural key

### `finance.receivable_obligation_items`
- Receivable + Sale + Sales Detail
- Sales Detail unique
- `amount_thb >= 0`

Composite FKs ensure the Sales Detail belongs to the same Sale as the Receivable.

### `finance.receipts`
- Receivable FK
- positive THB amount
- confirmed actor/time
- partial collection supported

### Outstanding projections
Relations:
- `finance.payable_outstanding_positions`
- `finance.receivable_outstanding_positions`

Each has owner PK/FK, original obligation total, adjustment total, settlement total, outstanding amount, row version, updated timestamp.

Row formula:
- `outstanding = original_obligation + adjustment_total - settlement_total`

Do not add `outstanding >= 0` as a permanent DB CHECK while FIN-001, FIN-002, and FIN-004 remain unresolved Business Rules. Current commands block those cases as safe v0.1 handling.

Outstanding Position row is the monetary concurrency boundary.

## 20. Audit schema

Relations:
- `audit.audit_events`
- `audit.audit_event_subjects`
- `audit.correction_links`

Audit remains append-oriented and command-oriented.
`command_id` on Audit/Outbox is correlation metadata only and does not FK to CommandExecution.

Audit subject `subject_kind + subject_key` is intentionally non-FK historical locator metadata under ADR-005, not a polymorphic business relationship or generic entity-navigation mechanism.

Suggested investigation indexes include:
- Audit Event `command_id`
- `(actor_account_id, occurred_at)`
- `(event_kind, occurred_at)`
- Correction Link by corrected event

## 21. Relation catalogue

### `system` — 4
- accounts
- account_capability_grants
- command_executions
- outbox_messages

### `party` — 5
- suppliers
- farmers
- employees
- customers
- outsourced_vendors

### `infrastructure` — 3
- containers
- warehouses
- storage_locations

### `product` — 3
- procurement_products
- sales_product_groups
- sales_products

### `processing_config` — 6
- processing_routes
- processing_route_versions
- route_input_configs
- process_materials
- processing_modules
- processing_module_outputs

### `procurement` — 2
- procurement_batches
- procurement_entries

### `processing` — 3
- processing_executions
- processing_execution_inputs
- processing_execution_outputs

### `outsourced` — 2
- outsourced_supply_batches
- outsourced_supply_details

### `sales` — 5
- sales
- sales_details
- sales_allocations
- sales_allocation_revisions
- sales_allocation_revision_items

### `inventory` — 3
- inventory_operations
- inventory_movements
- inventory_positions

### `sales_handling` — 2
- sales_packaging_items
- sales_packaging_work_records

### `labor` — 4
- employee_daily_wages
- processing_wage_components
- processing_wage_component_sources
- sales_packaging_wage_components

### `finance` — 11
- payables
- payable_obligation_items
- company_pickup_transport_bases
- company_pickup_transport_obligation_basis_items
- payable_adjustments
- payments
- receivables
- receivable_obligation_items
- receipts
- payable_outstanding_positions
- receivable_outstanding_positions

### `audit` — 3
- audit_events
- audit_event_subjects
- correction_links

Total: **56 relations**.

## 22. Formal consolidation corrections

The consolidation formally adopts these eight persistence-level corrections over earlier partial schema notes:

1. Finance original obligation constraint changes from `> 0` to `>= 0`.
2. Payable obligation source subtype relations collapse into one flat typed-FK `finance.payable_obligation_items` relation; Transport basis lineage remains separate M:N lineage.
3. Sales Allocation Revision Items are immutable historical allocation truth.
4. Inventory Position full typed identity uses PostgreSQL `UNIQUE NULLS NOT DISTINCT`.
5. Inventory Movement vocabulary adds signed non-zero `SALES_ALLOCATION_ADJUSTMENT` for allocation-revision compensation.
6. High-value stable Route/Version/Material membership uses composite FKs/alternate keys.
7. `sales.sales_allocations` becomes pointer-only current official projection to immutable Revision Items.
8. The low-value nullable Batch Route-Version composite principal relationship is not used; Processing confirmation validates Batch bound Route Version transactionally.

These are relational consistency corrections, not new YowThi Business Rules. They add no Business Rule Gap Register entries.

## 23. Composite FK policy for EF Core feasibility

Composite FKs are retained only when:
1. the membership itself is structural integrity,
2. principal columns form a stable/effectively non-null key,
3. the relationship prevents a materially impossible relational state.

Retained examples:
- Route Version `(id, route_id)`
- Process Material `(id, route_version_id)`
- Processing Module `(id, route_version_id)`
- same-version Module -> Process Material membership
- Sales Detail `(id, sales_id)`
- Allocation Revision `(id, sales_id)`
- Allocation Revision Item `(id, sales_detail_id)`
- Receivable `(id, sales_id)`
- Payable `(id, payable_kind)`

Do not create composite alternate keys merely because columns can technically be compared.

## 24. DDL dependency order

Initial relational creation order:

1. schemas
2. `system`
3. `party`
4. `infrastructure`
5. `product`
6. `processing_config`
7. `procurement`
8. `processing`
9. `outsourced`
10. `sales`
11. `inventory`
12. `sales_handling`
13. `labor`
14. `finance`
15. `audit`
16. secondary/partial indexes and any final late constraints

No circular aggregate ownership currently requires a special two-phase FK workaround.

## 25. Unresolved Business Rule boundary

The existing Gap Register remains authoritative and unchanged.

Important examples that must not be silently hardened into permanent relational rules include:
- HANDLING-001 / HANDLING-002
- LABOR-001
- FIN-001 through FIN-009 as applicable
- PROCESS / SALES / Batch gaps already registered

In particular, no permanent DB rule for nonnegative Finance Outstanding is introduced while overpayment, over-collection, and negative Payable Outstanding behavior remain unresolved.

## 26. Completion and next architecture step

With this consolidation confirmed:
- PostgreSQL Schema Parts 1–6 remain the domain-by-domain design history.
- This document is the integrated relational baseline for subsequent DDL/EF mapping work.
- Planned v0.1 relation set is 56 relations after the approved P8 Security Foundation authorization revision.
- Core relational contradictions identified during cross-module review are resolved.

Next architecture stage:

**EF Core Mapping Architecture v0.1**

Expected next topics:
- solution/project persistence boundaries
- one write `ErpDbContext`
- schema/table mappings
- UUID v7 value generation
- explicit `row_version` concurrency tokens
- alternate/composite keys and composite FK mapping
- CHECK/unique/partial-index migration strategy
- PostgreSQL-specific `NULLS NOT DISTINCT` migration support
- typed discriminator conversions/validation
- entity configurations per module
- query filters and historical-reference behavior
- transaction / execution strategy boundaries

Docker Desktop / PostgreSQL runtime is not required for the architecture-document phase. It becomes necessary when implementing and executing the first real EF Core migrations and PostgreSQL integration tests.
