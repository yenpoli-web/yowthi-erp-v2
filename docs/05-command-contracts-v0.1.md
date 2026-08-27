# Application Command Contracts v0.1

## Command design principle

Commands express business intent, not CRUD.

Examples:
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

Core business writes are atomic in one PostgreSQL transaction.

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
Must include business context/reason.

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

## Correction Framework

Decision:
- `ALLOW_DIRECT_AMENDMENT`
- `ALLOW_WITH_COMPENSATION`
- `BLOCK`

All confirmed-transaction corrections require:
- dependency assessment
- revalidation inside transaction
- owning domain decision
- compensating facts if required
- audit
- idempotency
- atomic commit

No generic Update Any Entity.
No silent cascade correction.
No generic Undo.
No hard delete as correction.
