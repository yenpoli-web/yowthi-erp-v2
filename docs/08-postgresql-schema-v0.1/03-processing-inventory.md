# PostgreSQL Schema v0.1 — Part 3: Processing + Inventory

## `processing.processing_executions`

Core:
- work_date
- employee_id
- procurement_batch_id
- processing_route_version_id
- processing_module_id
- execution_mode snapshot
- negative_inventory_policy snapshot
- processing_source_kind nullable
- supplier_id nullable
- recorded_at / operator
- concurrency/lifecycle

Processing source:
- SOURCE_TRACKED + SUPPLIER → supplier_id
- SOURCE_TRACKED + FARMERS_COMBINED → no supplier_id
- POOLED_OUTPUT / FINAL_PACKAGING → no processing source

## `processing.processing_execution_inputs`

One input in v0.1:
- execution_id PK/FK
- consumption_basis
- consumed_quantity
- scale/container snapshot fields when applicable

Consumption basis:
- SCALE_NET
- OUTPUT_QUANTITY
- PACKAGING_WEIGHT

## `processing.processing_execution_outputs`

Common header:
- execution FK
- module/output definition FK
- output_kind
- configured_wage_rate_snapshot

Subtype: Process Material output
- observed scale reading
- actual container count
- tare snapshot
- derived net quantity

Subtype: Sales Product output
- completed quantity
- packaging_weight_snapshot
- source_consumption_quantity

Final Packaging v0.1:
- one Sales Product output per execution
- source consumption = completed quantity × packaging weight
- source may go negative if module policy allows

## `inventory.inventory_operations`

Groups one business inventory operation:
- PROCUREMENT_RECEIPT
- PROCESSING
- TRANSFER
- ADJUSTMENT
- BATCH_RECONCILIATION
- later OUTSOURCED_RECEIPT
- later SALES_ISSUE
- later SALES_ALLOCATION_REVISION

Use typed real source FKs where appropriate.

## `inventory.inventory_movements`

Append-oriented ledger truth.

Dimensions:
- operation
- sequence
- movement type
- Origin
- typed Source Batch FK
- typed Inventory Object FK
- storage_location_id
- optional Raw Source Segment
- signed quantity delta
- recorded_at

Movement signs:
- receipts/produces/transfers-in > 0
- consumes/issues/transfers-out < 0
- adjustment/reconciliation non-zero

Raw source segment:
- SUPPLIER + supplier FK
- FARMERS_COMBINED
- only relevant to raw-stage identity

## `inventory.inventory_positions`

Transactional current-state projection with the same full identity dimensions:
- Origin
- Source Batch
- Inventory Object
- Location
- optional Raw Source Segment

Fields:
- balance_quantity
- row_version

Position may be negative because Final Packaging can legally make source material negative.
This does not mean all commands allow negative inventory.

Movement write + Position update occur in the same transaction.
Position can be rebuilt from Movement ledger.

## Processing inventory examples

SOURCE_TRACKED:
- raw consume actual derived input quantity
- 1..N process material produces
- input-output difference is not fabricated as a Waste product

POOLED_OUTPUT:
- source consume = sum output quantity
- output material produces
- remaining source balance remains until closing

FINAL_PACKAGING:
- consume Procurement Product or Process Material
- produce Sales Product
- consume = completed quantity × Packaging Weight
