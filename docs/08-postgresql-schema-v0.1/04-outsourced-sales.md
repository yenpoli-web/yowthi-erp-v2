# PostgreSQL Schema v0.1 — Part 4: Outsourced + Sales

## `outsourced.outsourced_supply_batches`

Business identity:
- `supply_date + outsourced_vendor_id`

Fields:
- lifecycle ACTIVE/CLOSED
- closing metadata
- row version
- soft delete metadata

Product is not part of batch identity.

## `outsourced.outsourced_supply_details`

Confirmed fact:
- batch FK
- Sales Product FK
- quantity
- pricing_basis_snapshot
- sales_weight_snapshot conditional
- unit_price
- amount_thb
- recorded_at/operator
- lifecycle/concurrency

On confirm:
- one typed `OUTSOURCED_RECEIPT` inventory operation
- OUTSOURCED Sales Product inventory becomes available immediately
- Vendor Payable source is created in Finance

## `sales.sales`

Header:
- sales_date
- customer_id
- status DRAFT / CONFIRMED
- confirmation metadata
- row version
- lifecycle

Draft:
- editable
- no inventory issue
- no Receivable

Confirmed:
- final allocation
- SALES_ISSUE
- Receivable

## `sales.sales_details`

- sales FK
- line number
- Sales Product FK
- quantity
- pricing_basis_snapshot
- sales_weight_snapshot
- unit_price
- amount_thb
- lifecycle/concurrency

Same Sales Product may appear on multiple lines.

## `sales.sales_allocations`

Final allocation only:
- Sales Detail FK
- sequence
- Origin
- typed Procurement Batch / Outsourced Supply Batch FK
- allocated quantity
- manual-override flag

Invariant:
- sum allocation quantity = Sales Detail quantity

Auto Allocation preview is not the formal source of truth.

Priority:
1. OUTSOURCED oldest → newest
2. IN_HOUSE oldest → newest

## Allocation revision

Use controlled revision history and compensating inventory movements.
Do not edit prior Inventory Movement history.

Suggested:
- `sales_allocation_revisions`
- `sales_allocation_revision_items`

Current `sales_allocations` may represent the current final allocation while revision tables preserve history.

## SALES_ISSUE

One Sales may create one Sales Issue Inventory Operation with multiple movements.
One allocation may map to multiple movements if inventory spans multiple locations.

Movement may point back to `sales_allocation_id`.

Receivable remains owned by Finance; do not store outstanding/payment state on Sales.
