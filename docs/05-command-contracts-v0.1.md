# Application Command Contracts v0.1

## Command design principle

Application writes express **explicit application intent**, not generic CRUD.

There are two command families:

```text
Application Write Command
├─ Business Fact Command
└─ ERP Control Command
```

Business Fact Commands register a real YowThi operation or result and may only encode Business Rules supported by real operating facts.

ERP Control Commands maintain the ERP record/state because an authorized operator needs a correction or lifecycle change. They do not require inventing a new Business Rule merely to justify the database mutation, but they remain target-specific and must preserve authorization, idempotency, concurrency, dependency protection, Audit, and transaction safety.

The later boundary decision is `docs/17-erp-registration-data-control-boundary-v0.1.md`.

Examples of Business Fact Commands:
- `ConfirmProcurementEntry`
- `ConfirmProcessingExecution`
- `ConfirmSales`
- `ConfirmOutsourcedSupplyDetail`
- `RecordSalesPackagingWork`
- `ConfirmEmployeeDailyWage`
- `AddPayableAdjustment`
- `PayPayable`
- `ReceiveReceivable`
- `TransferInventory`
- `AdjustInventory`
- `CloseProcurementBatch`
- `CloseOutsourcedSupplyBatch`

Examples of ERP Control Commands:
- `CorrectSalesAllocation`
- `SoftDeleteSupplier`
- `RestoreSupplier`
- `SoftDeleteCustomer`
- `RestoreCustomer`
- target-specific data correction
- target-specific Reopen
- target-specific Hard Delete under Data Protection

Core persisted writes are atomic in one PostgreSQL transaction where the command spans multiple affected facts/projections.

## ConfirmProcurementEntry

Input:
- Procurement Date
- Procurement Product
- Source Type
- Source
- Net Quantity
- Unit Price
- Company Pickup
- Receipt Storage Location — optional
- Command Identity

System resolves:
- Procurement Batch by date + product
- Unit from Procurement Product
- Amount = floor(quantity × price)
- Receipt Storage Location when not explicitly supplied: use the unique applicable default when one can be resolved

Receipt-location safe v0.1 handling under PROC-002:
- if Receipt Storage Location is supplied, validate and use it
- if it is not supplied and exactly one applicable default can be resolved, use that default
- if it is not supplied and no unique applicable default can be resolved, block confirmation and require an explicit Receipt Storage Location

Atomic effects:
- Procurement Entry
- PURCHASE_RECEIPT at the resolved Receipt Storage Location
- Procurement Payable source
- Transport obligation basis if applicable
- audit/idempotency/outbox

## ConfirmProcessingExecution

Shared:
- Work Date
- Employee
- Procurement Batch
- Processing Module
- Command Identity

Mode: SOURCE_TRACKED
- Supplier or Farmers Combined
- input scale reading
- output scale readings
- inventory consumes actual derived input quantity
- inventory produces output quantities

Mode: POOLED_OUTPUT
- no Supplier/Farmer
- no input scale
- source consumption = sum output quantity

Mode: FINAL_PACKAGING
- completed Sales Product quantity
- source consumption = completed quantity × Packaging Weight
- source negative allowed by module policy

## ConfirmSales

Confirm process:
- validate details/pricing
- read current sellable inventory
- auto allocation: OUTSOURCED oldest→newest, then IN_HOUSE oldest→newest
- authorized manual override
- allocation total must equal Sales Detail quantity
- resolve inventory locations
- persist final allocations
- SALES_ISSUE
- create Receivable
- mark Sales Confirmed

## CorrectSalesAllocation

Confirmed YowThi correction input semantics under SALES-003 support two explicit modes. The caller selects the intended mode; the system does not guess between them.

Shared input:
- Sales
- Expected Sales Row Version
- Correction Mode
- Allocation inputs
- Command Identity

Mode: `COMPLETE_REPLACEMENT`
- the user supplies the complete official replacement allocation set
- for each Sales Detail, submitted allocation total must equal that Sales Detail quantity
- no automatic remainder allocation is performed

Mode: `OVERRIDE_AND_REALLOCATE`
- the user supplies explicit allocation overrides first
- submitted override total for a Sales Detail may be less than the Sales Detail quantity
- the remaining quantity is re-allocated using the established priority: OUTSOURCED oldest→newest, then IN_HOUSE oldest→newest

Atomic effects:
- append the next immutable Sales Allocation Revision + Revision Items
- replace only the current `sales_allocations` pointer projection
- compare previous official allocation with the new official allocation
- create `SALES_ALLOCATION_ADJUSTMENT` Inventory Movements only for actual net allocation deltas
- update affected Inventory Positions transactionally
- increment Sales row version
- write correction Audit + correction lineage
- persist idempotent command result and Outbox message

Must not:
- rewrite or remove prior Allocation Revisions / Revision Items
- rewrite prior `SALES_ISSUE` or other Inventory Movement history
- silently reopen a Closed/deleted source Batch
- write an inventory delta back into a Closed/deleted source Batch

Existing Sales source-location ambiguity handling remains applicable; correction does not invent a new storage-location override vocabulary while SALES-001 remains unresolved.

## ConfirmOutsourcedSupplyDetail

Batch identity:
- Supply Date + Outsourced Vendor

Atomic:
- Detail
- OUTSOURCED_RECEIPT
- Vendor Payable source

## RecordSalesPackagingWork

Input:
- Sales
- Work Date
- Employee
- Sales Packaging Item
- Confirmed Wage

No quantity/weight/hour fields in v0.1.
Does not directly create Payable.

## ConfirmEmployeeDailyWage

Identity:
- Work Date + Employee

Processing wage:
- group same day + employee + output/wage item + applicable wage config
- aggregate quantity first
- apply configurable default rate
- user may override Applied Wage Rate
- floor after aggregate × rate

Add Sales Packaging Wage facts.
Atomic:
- Employee Daily Wage
- Employee Payable source

## PayPayable / ReceiveReceivable

Default settlement amount = current Outstanding.
User may reduce amount for partial settlement.
Final confirm records current system timestamp.

Outstanding:
- Original + Adjustments - Settlements

## AddPayableAdjustment

Confirmed business case:
- Supplier quality / weight deduction

Must not:
- rewrite Procurement
- rewrite Inventory
- rewrite previous Payments

## Finance data correction

If a registered Payment / Receipt / Adjustment is wrong, correction is an ERP Control operation rather than a requirement to fabricate another business event.

A target-specific Finance correction may:
- amend the wrongly registered value/state
- re-evaluate/rebuild the applicable Outstanding projection transactionally
- use expected row-version / Outstanding concurrency protection
- retain Audit before/after evidence
- use persistent idempotency

If money actually moves again in reality, that is a new Finance Business Fact and must be recorded as a new transaction rather than hidden inside a data correction.

`FIN-003` and `FIN-005` therefore no longer block implementation as Business Rule gaps; their remaining work is target-specific ERP Control contract/implementation design.

## TransferInventory

Same:
- Origin
- Source Batch
- Inventory Object
- Quantity

Different:
- Location

Atomic:
- TRANSFER_OUT
- TRANSFER_IN

## AdjustInventory

Creates explicit `ADJUSTMENT` movement.
Never directly updates balance.
Must include operational context/reason.

Inventory Adjustment / stocktake is also the normal reconciliation mechanism when physical stock differs from ERP stock because of shrinkage, damage, weighing variance, handling loss, spoilage, missing stock, or other real-world discrepancy.

Do not rewrite unrelated historical Procurement / Processing / Sales / Batch movements solely to make ERP inventory equal a later physical count.

## CloseProcurementBatch

Precondition:
- all sellable inventory for source batch = 0

Atomic:
- re-read all remaining positions
- create `BATCH_RECONCILIATION = -current balance` per position
- validate all positions = 0
- mark Closed

## CloseOutsourcedSupplyBatch

Precondition:
- all sellable inventory for outsourced batch = 0

No reconciliation.
Mark Closed.

## ERP lifecycle control

Soft Delete, Restore, Activate/Deactivate, and Reopen are ERP Control operations.

They do not require a new Business Rule for each target merely because an ERP state changes.

Target-specific lifecycle commands still require:
- authenticated actor
- explicit capability authorization
- target-specific route/command
- Idempotency Key
- expected row version where applicable
- structural/dependency safety
- Audit

Restore removes the soft-deleted state; it does not automatically force `active = true`.

Reopen makes the ERP object available for applicable operations again. Reopen does not erase prior Audit or rewrite immutable ledger history.

For Closed Batch Reopen specifically:
- do not delete prior Close Audit
- do not delete prior `BATCH_RECONCILIATION` movements
- do not reconstruct an imagined pre-close physical stock state
- physical stock discrepancy is handled by stocktake / `AdjustInventory`

`LIFE-001` therefore no longer blocks implementation as a Business Rule gap; its remaining work is lifecycle-control implementation and technical concurrency/Audit behavior.

### Supplier lifecycle — V8-C4 COMPLETE

Commands:
- `SoftDeleteSupplier`
- `RestoreSupplier`

Routes:
- `POST /api/v1/party/suppliers/{supplierId}/soft-delete`
- `POST /api/v1/party/suppliers/{supplierId}/restore`

Capability:
- `party.supplier.lifecycle`

Shared input:
- Supplier
- Expected Supplier Row Version
- Command Identity

`SoftDeleteSupplier`:
- sets `deleted_at`
- sets `deleted_by_account_id` from the authenticated actor
- increments Supplier `row_version`
- does not change `active`
- does not physically delete the Supplier
- does not cascade/delete historical Procurement, Processing, Inventory, or Finance references
- current-use Supplier selectors exclude the soft-deleted Supplier

Historical dependencies do not by themselves block Soft Delete because the Supplier row remains available for FK/traceability history.

`RestoreSupplier`:
- clears `deleted_at`
- clears `deleted_by_account_id`
- increments Supplier `row_version`
- preserves the existing `active` value
- does not automatically reactivate an inactive Supplier

Lifecycle technical controls:
- replay/CommandId acquisition occurs before current lifecycle/version lookup
- same actor + command type + canonical hash replays the committed result
- changed actor/type/hash conflicts with `idempotency.key-reused`
- stale expected row version conflicts with `concurrency.stale-row-version`
- Soft Delete of an already deleted Supplier conflicts with `party.supplier-already-deleted`
- Restore of a current/non-deleted Supplier conflicts with `party.supplier-not-deleted`
- failed state/concurrency attempts roll back CommandExecution acquisition

Audit:
- event kind = `DATA_LIFECYCLE`
- subject kind = `party.supplier`
- Soft Delete subject change kind = `SOFT_DELETE`
- Restore subject change kind = `RESTORE`
- before/after row versions are retained

V8-C4 requires no relation, schema, snapshot, or EF migration change.

### Customer lifecycle — V8-C5 COMPLETE

Commands:
- `SoftDeleteCustomer`
- `RestoreCustomer`

Routes:
- `POST /api/v1/party/customers/{customerId}/soft-delete`
- `POST /api/v1/party/customers/{customerId}/restore`

Capability:
- `party.customer.lifecycle`

Shared input:
- Customer
- Expected Customer Row Version
- Command Identity

`SoftDeleteCustomer`:
- sets `deleted_at`
- sets `deleted_by_account_id` from the authenticated actor
- increments Customer `row_version`
- preserves `active`
- does not physically delete the Customer
- does not cascade/delete historical `sales.sales.customer_id` references

An existing Sale dependency does not by itself block Soft Delete because the Customer row remains available for FK/traceability history. This differs deliberately from Customer Hard Delete, where a Sale dependency blocks physical deletion.

`RestoreCustomer`:
- clears `deleted_at`
- clears `deleted_by_account_id`
- increments Customer `row_version`
- preserves the existing `active` value
- does not automatically reactivate an inactive Customer

Lifecycle technical controls:
- replay/CommandId acquisition occurs before current lifecycle/version lookup
- same actor + command type + canonical hash replays the committed result
- changed actor/type/hash conflicts with `idempotency.key-reused`
- stale expected row version conflicts with `concurrency.stale-row-version`
- Soft Delete of an already deleted Customer conflicts with `party.customer-already-deleted`
- Restore of a current/non-deleted Customer conflicts with `party.customer-not-deleted`
- failed state/concurrency attempts roll back CommandExecution acquisition and Audit

Audit:
- event kind = `DATA_LIFECYCLE`
- subject kind = `party.customer`
- Soft Delete subject change kind = `SOFT_DELETE`
- Restore subject change kind = `RESTORE`
- before/after row versions are retained

V8-C5 requires no relation, schema, snapshot, or EF migration change.

## Correction / control framework

The older correction decision vocabulary remains useful where applicable:
- `ALLOW_DIRECT_AMENDMENT`
- `ALLOW_WITH_COMPENSATION`
- `BLOCK`

But the decision is now interpreted according to the registration/control boundary:

- **ERP registration error** → target-specific ERP Control correction may directly amend the registered fact when structurally safe, with Audit and affected projection rebuild.
- **real later-world event** → record a new Business Fact; do not rewrite the earlier real event away.
- **append-oriented technical history** → keep it append-oriented where already architected; do not rewrite immutable Inventory Movement history.

All persisted corrections/control mutations require the applicable technical safeguards:
- dependency/structural assessment
- transaction-time revalidation
- concurrency protection
- Audit
- idempotency
- atomic commit
- projection rebuild/update when applicable

No generic Update Any Entity.
No generic JSON Patch correction.
No silent cascade correction.
No generic Undo.
No Hard Delete masquerading as ordinary correction.
