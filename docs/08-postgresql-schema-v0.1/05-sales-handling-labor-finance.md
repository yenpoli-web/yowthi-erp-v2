# PostgreSQL Schema v0.1 — Part 5: Sales Handling + Labor + Finance

Status: **DECISION / v0.1**
Revision basis: Part 5 Revision 0.2, formally confirmed.

## 1. Design principles

- Source transactions do not store paid/outstanding state.
- Finance owns obligation, adjustment, settlement facts.
- Outstanding is a transactional, rebuildable projection.
- Core traceability uses typed real foreign keys; no unconstrained `(type,id)` polymorphism.
- Confirmed finance facts do not expose generic edit/delete.
- `row_version bigint` is the optimistic concurrency mechanism.
- Core cross-domain writes occur atomically in the single write `ErpDbContext` transaction.

## 2. `sales_handling.sales_packaging_items`

Sales Packaging / Handling work-item master.

Fields:
- `id uuid`
- `name_zh_tw`
- `name_th_th`
- `active boolean`
- `row_version bigint`
- creation metadata
- controlled soft-delete metadata

Rules:
- at least one localized name must exist
- no Wage Rate is stored on the master
- latest confirmed wage for same Employee + Work Item may be used only as UI prefill

## 3. `sales_handling.sales_packaging_work_records`

Fields:
- `id uuid`
- `sales_id uuid` FK
- `work_date date`
- `employee_id uuid` FK
- `sales_packaging_item_id uuid` FK
- `confirmed_wage_thb bigint`
- `recorded_at timestamptz`
- `recorded_by_account_id uuid`
- `row_version bigint`
- controlled soft-delete metadata

Constraints:
- `confirmed_wage_thb >= 0`

Do not store in v0.1:
- quantity
- kg / weight
- box count
- hours
- unit rate

No business unique constraint is imposed on `(sales_id, work_date, employee_id, sales_packaging_item_id)` because HANDLING-002 remains unresolved.
Sales lifecycle eligibility is HANDLING-001.
Late work after a confirmed Daily Wage is LABOR-001; normal late work is blocked until the rule is confirmed.

## 4. `labor.employee_daily_wages`

Business identity:
- `work_date + employee_id`

Fields:
- `id uuid`
- `work_date date`
- `employee_id uuid` FK
- `processing_wage_total_thb bigint`
- `sales_packaging_wage_total_thb bigint`
- `total_wage_thb bigint`
- `confirmed_at timestamptz`
- `confirmed_by_account_id uuid`
- `row_version bigint`

Constraints:
- unique `(work_date, employee_id)`
- wage totals are non-negative
- `total_wage_thb = processing_wage_total_thb + sales_packaging_wage_total_thb`

## 5. `labor.processing_wage_components`

One row represents one Processing Wage aggregation group inside an Employee Daily Wage.

Fields:
- `id uuid`
- `employee_daily_wage_id uuid` FK
- `processing_module_output_id uuid` FK
- `configured_wage_rate_snapshot numeric`
- `applied_wage_rate numeric`
- `rate_overridden boolean`
- `aggregated_quantity numeric`
- `amount_thb bigint`

Rules:
- aggregate quantity before multiplication
- `amount_thb = floor(aggregated_quantity × applied_wage_rate)`
- do not floor each Processing Execution detail separately
- Daily Wage confirmation may override `applied_wage_rate`
- override never rewrites Processing Configuration

Constraints:
- `aggregated_quantity >= 0`
- wage rates are non-negative
- `amount_thb >= 0`

## 6. `labor.processing_wage_component_sources`

Preserves Processing Execution lineage used by a wage component.

Fields:
- `processing_wage_component_id uuid` FK
- `processing_execution_output_id uuid` FK
- `quantity_snapshot numeric`

Constraints:
- primary key `(processing_wage_component_id, processing_execution_output_id)`
- unique `processing_execution_output_id`

A Processing Execution Output can therefore be included in only one confirmed Employee Daily Wage.

## 7. `labor.sales_packaging_wage_components`

Fields:
- `id uuid`
- `employee_daily_wage_id uuid` FK
- `sales_packaging_work_record_id uuid` FK
- `wage_amount_thb bigint`

Constraints:
- unique `sales_packaging_work_record_id`
- `wage_amount_thb >= 0`

A confirmed Sales Packaging Work Record can be included in only one Employee Daily Wage.

## 8. `ConfirmEmployeeDailyWage` atomic effects

Within one transaction:
1. validate Work Date + Employee
2. collect eligible Processing Execution Outputs
3. collect eligible Sales Packaging Work Records
4. aggregate Processing wage groups
5. apply configured/default rates and any confirmed overrides
6. create Processing Wage Components and source lineage
7. create Sales Packaging Wage Components
8. create Employee Daily Wage totals
9. create Employee Wage Payable and obligation item
10. create/update Payable Outstanding projection
11. audit, idempotency, outbox

## 9. Finance obligation model

Truth model:

`Original Obligation Facts + Adjustment Facts - Settlement Facts = Outstanding`

Outstanding positions are projections, not historical truth.

`Payable Kind` and `Obligation Item Kind` are separate concepts.

## 10. `finance.payables`

Fields:
- `id uuid`
- `payable_kind`
- nullable typed source FKs as applicable
- `created_at timestamptz`
- `row_version bigint`

Payable kinds:
- `PROCUREMENT_SUPPLIER`
- `PROCUREMENT_FARMER`
- `COMPANY_PICKUP_TRANSPORT`
- `OUTSOURCED_VENDOR`
- `EMPLOYEE_DAILY_WAGE`

Typed shapes:

### PROCUREMENT_SUPPLIER
- `procurement_batch_id` present
- `supplier_id` present
- other source FKs absent
- business identity: `(procurement_batch_id, supplier_id)`

### PROCUREMENT_FARMER
- `procurement_batch_id` present
- `farmer_id` present
- other source FKs absent
- business identity: `(procurement_batch_id, farmer_id)`

### OUTSOURCED_VENDOR
- `outsourced_supply_detail_id` present and unique
- other source FKs absent

### EMPLOYEE_DAILY_WAGE
- `employee_daily_wage_id` present and unique
- other source FKs absent

### COMPANY_PICKUP_TRANSPORT
- no permanent business unique grouping is imposed in v0.1
- grouping/confirmation boundary remains FIN-008

Use CHECK constraints and partial unique indexes to enforce only the confirmed typed shapes.

## 11. `finance.payable_obligation_items`

Authoritative original-obligation amount lives here.

Fields:
- `id uuid`
- `payable_id uuid` FK
- `obligation_kind`
- `amount_thb bigint`
- `recorded_at timestamptz`

Constraint:
- `amount_thb > 0`

Obligation kinds:
- `PROCUREMENT_ENTRY`
- `OUTSOURCED_SUPPLY_DETAIL`
- `EMPLOYEE_DAILY_WAGE`
- `COMPANY_PICKUP_TRANSPORT`

Typed lineage is represented through real subtype PK/FKs, not a generic source id.

## 12. Procurement payable obligation lineage

Supplier/Farmer obligation subtype:
- `payable_obligation_item_id uuid` PK/FK
- `procurement_entry_id uuid` FK UNIQUE

A confirmed Procurement Entry appends one obligation item to the applicable Batch + Supplier/Farmer Payable and updates the Outstanding projection atomically.

## 13. Outsourced Vendor obligation lineage

Subtype:
- `payable_obligation_item_id uuid` PK/FK
- `outsourced_supply_detail_id uuid` FK UNIQUE

A confirmed Outsourced Supply Detail creates its Vendor Payable source immediately.

## 14. Employee Wage obligation lineage

Subtype:
- `payable_obligation_item_id uuid` PK/FK
- `employee_daily_wage_id uuid` FK UNIQUE

Employee Daily Wage and Employee Payable are created atomically.

## 15. `finance.company_pickup_transport_bases`

A Company Pickup Procurement Entry creates transport obligation basis, not an immediate transport payable amount.

Fields:
- `id uuid`
- `procurement_entry_id uuid` FK UNIQUE
- `applicable_quantity numeric`
- `recorded_at timestamptz`

Rules:
- only exists for confirmed Procurement Entries with `company_pickup = true`
- `applicable_quantity` is the historical basis quantity
- Procurement Batch lineage is obtained through the Procurement Entry; batch id is not duplicated here
- no rate, amount, payee, or Driver Master is stored on the basis

## 16. Company Pickup Transport obligation subtype

No separate Transport Charge aggregate and no Transport Rate Master are introduced in v0.1.

Subtype fields:
- `payable_obligation_item_id uuid` PK/FK
- `procurement_batch_id uuid` FK
- `aggregated_applicable_quantity numeric`
- `applied_rate_per_kg numeric`

The authoritative final THB amount remains `finance.payable_obligation_items.amount_thb`; it is not duplicated in this subtype.

Constraints:
- `aggregated_applicable_quantity >= 0`
- `applied_rate_per_kg >= 0`

The applied THB/kg rate is persisted as a confirmed historical fact. The schema does not invent its selection mechanism. The rate must not be hard-coded in application logic.

FIN-007 remains the unresolved final THB rounding rule.
FIN-008 remains the unresolved Payable grouping/confirmation boundary.
FIN-009 remains the unresolved payee-recording semantics.

Therefore v0.1 must not impose a business unique constraint such as one Transport Payable per Procurement Batch, must not assume floor/round behavior, and must not place payee on Procurement or transport-basis facts.

## 17. `finance.company_pickup_transport_obligation_basis_items`

Fields:
- `transport_obligation_item_id uuid` FK
- `transport_basis_id uuid` FK
- `applied_quantity numeric`

Constraints:
- primary key `(transport_obligation_item_id, transport_basis_id)`
- `applied_quantity >= 0`

At confirmation, validate:
- sum of applied quantities equals the transport obligation item's `aggregated_applicable_quantity`

This table provides transport obligation lineage without deciding FIN-008.

## 18. `finance.payable_adjustments`

Fields:
- `id uuid`
- `payable_id uuid` FK
- `adjustment_type`
- `amount_delta_thb bigint`
- `reason_text`
- `recorded_at timestamptz`
- `recorded_by_account_id uuid`

v0.1 confirmed adjustment type:
- `SUPPLIER_QUALITY_WEIGHT_DEDUCTION`

Rules:
- deduction delta is negative
- do not rewrite Procurement
- do not rewrite Inventory
- do not rewrite previous Payments
- FIN-004 blocks a deduction that would make Payable Outstanding negative
- FIN-005 leaves confirmed adjustment correction/reversal unresolved
- FIN-006 defers other adjustment types

## 19. `finance.payments`

Fields:
- `id uuid`
- `payable_id uuid` FK
- `amount_thb bigint`
- `confirmed_at timestamptz`
- `confirmed_by_account_id uuid`

Constraints:
- `amount_thb > 0`

Rules:
- partial payment supported
- default settlement amount may be current Outstanding
- user may reduce the amount for partial settlement
- FIN-001 blocks Payment > Outstanding in v0.1
- FIN-003 prohibits generic edit/delete of confirmed settlements while correction semantics remain unresolved

## 20. `finance.receivables`

Fields:
- `id uuid`
- `sales_id uuid` FK UNIQUE
- `created_at timestamptz`
- `row_version bigint`

One confirmed Sales creates one Receivable atomically with final Sales Allocation and SALES_ISSUE.

## 21. `finance.receivable_obligation_items`

Fields:
- `id uuid`
- `receivable_id uuid` FK
- `sales_detail_id uuid` FK UNIQUE
- `amount_thb bigint`
- `recorded_at timestamptz`

Constraint:
- `amount_thb > 0`

Receivable original obligation is reconstructed from these Sales Detail lineage facts.

No receivable adjustment type is implemented in v0.1; its Outstanding adjustment total remains zero until a real business rule is confirmed.

## 22. `finance.receipts`

Fields:
- `id uuid`
- `receivable_id uuid` FK
- `amount_thb bigint`
- `confirmed_at timestamptz`
- `confirmed_by_account_id uuid`

Constraints:
- `amount_thb > 0`

Rules:
- partial collection supported
- FIN-002 blocks Receipt > Outstanding in v0.1
- FIN-003 prohibits generic edit/delete of confirmed settlements while correction semantics remain unresolved

## 23. Outstanding transactional projections

### `finance.payable_outstanding_positions`

Fields:
- `payable_id uuid` PK/FK
- `original_obligation_thb bigint`
- `adjustment_total_thb bigint`
- `settlement_total_thb bigint`
- `outstanding_thb bigint`
- `row_version bigint`
- `updated_at timestamptz`

### `finance.receivable_outstanding_positions`

Fields:
- `receivable_id uuid` PK/FK
- `original_obligation_thb bigint`
- `adjustment_total_thb bigint`
- `settlement_total_thb bigint`
- `outstanding_thb bigint`
- `row_version bigint`
- `updated_at timestamptz`

Formula:
- `outstanding = original_obligation + adjustment_total - settlement_total`

Truth:
- obligation items
- adjustments where implemented
- payments / receipts

Projection:
- Outstanding Position

The projections must be rebuildable from the facts.

## 24. Finance concurrency boundary

The Outstanding Position row is the monetary concurrency boundary.

Payment, Receipt, Payable Adjustment, and any obligation change must update the applicable Outstanding Position in the same transaction and compete on `row_version`.

For settlement, the transaction must atomically ensure the requested amount does not exceed current Outstanding. A failed version/current-balance predicate is treated as concurrency conflict or insufficient current Outstanding, not silently retried against stale assumptions.

## 25. Correction boundary

Do not add generic finance correction flags or generic Undo semantics in v0.1.

Until FIN-003 and FIN-005 are confirmed:
- no generic UPDATE/DELETE of confirmed Payments/Receipts
- no generic UPDATE of confirmed Adjustments
- correction must go through the owning Correction Framework after dependency assessment

## 26. Cross-domain atomic matrix

| Command / action | Source fact | Labor | Finance | Outstanding |
|---|---|---|---|---|
| `ConfirmProcurementEntry` | Procurement Entry | — | Supplier/Farmer obligation item | update Payable |
| Company Pickup Procurement Entry | Transport Basis | — | no transport payable amount yet | — |
| Transport obligation confirmation | — | — | Transport Payable obligation + basis lineage | create/update Payable |
| `ConfirmOutsourcedSupplyDetail` | Outsourced Detail | — | Vendor Payable obligation | update Payable |
| `ConfirmEmployeeDailyWage` | — | Daily Wage + components | Employee Payable obligation | update Payable |
| `ConfirmSales` | Sales | — | Receivable + obligation items | create Receivable position |
| `AddPayableAdjustment` | — | — | Adjustment | update Payable |
| `PayPayable` | — | — | Payment | update Payable |
| `ReceiveReceivable` | — | — | Receipt | update Receivable |

## 27. Part 5 unresolved rules retained

Must remain in the Business Rule Gap Register:
- HANDLING-001
- HANDLING-002
- LABOR-001
- FIN-001 through FIN-009 as applicable

In particular:
- FIN-007 — Company Pickup Transport final THB rounding rule
- FIN-008 — Company Pickup Transport payable grouping / confirmation boundary
- FIN-009 — Company Pickup Transport payee recording semantics

The schema intentionally does not convert these gaps into permanent Business Rules.
