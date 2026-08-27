# PostgreSQL Schema v0.1 — Part 2: Procurement

## `procurement.procurement_batches`

Core:
- `id uuid`
- `procurement_date date`
- `procurement_product_id uuid`
- `procurement_status`: OPEN / COMPLETED
- `lifecycle_status`: ACTIVE / CLOSED
- optional route + route version binding
- completion/closing timestamps/operators
- `row_version`
- soft-delete metadata

Business unique:
- `(procurement_date, procurement_product_id)`

This unique identity should not be released merely because of soft delete.

Route binding:
- nullable before first processing
- once bound, normal operation does not change it

`SOLD_OUT` is not required as a persisted linear state; it is a closing eligibility projection.

## `procurement.procurement_entries`

Confirmed fact:
- id
- procurement_batch_id
- source_type
- supplier_id nullable
- farmer_id nullable
- net_quantity
- unit_code_snapshot
- unit_price exact numeric
- amount_thb bigint
- company_pickup boolean
- recorded_at
- recorded_by
- concurrency/lifecycle

Typed source CHECK:
- SUPPLIER → supplier_id present, farmer null
- FARMER → farmer_id present, supplier null

Amount:
- `floor(net_quantity × unit_price)`

Do not store:
- current inventory balance
- remaining/processed quantity
- payable/paid/outstanding
- driver
- basket/arrival sequence
- Farmers Combined identity
- batch total as source of truth

Price history is derived from confirmed Procurement Entries.
