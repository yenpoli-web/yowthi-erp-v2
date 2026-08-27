# Business Discovery v0.1

## CURRENT BUSINESS FACT — Procurement

Daily procurement has two main sources:
- long-term Suppliers
- local Farmers

Suppliers may be notified in the morning and report quantities in the afternoon. Goods may be company pickup or supplier delivery.
Farmers may deliver directly at any time.

Arrival time/order/basket arrival sequence is not a core ERP business dimension.

### Procurement Batch identity

One Procurement Batch is:

> same Procurement Date + same Procurement Product

regardless of:
- number of suppliers/farmers
- number of deliveries
- delivery timing
- registration timing

Inventory and Payable do not wait for Procurement Batch completion.

### Procurement Entry

Each entry records:
- Source Type: `SUPPLIER` / `FARMER`
- Source identity
- Net Quantity
- Unit from Procurement Product
- Unit Price
- Amount = `floor(Quantity × Unit Price)`
- Company Pickup yes/no
- registration timestamp

Price default may use the last entered price for the same source + same product, but the current confirmed price is the fact.

On confirmation:
1. Raw Inventory receipt is immediately created.
2. Payable becomes immediately available.

Processing projection:
- Supplier remains separate by supplier.
- All Farmers in the same Procurement Batch become `Farmers Combined` for source-tracked processing.

Finance projection:
- Supplier payable aggregates by Procurement Batch + Supplier.
- Farmer payable aggregates by Procurement Batch + Farmer.

## CURRENT BUSINESS FACT — Processing

Processing must be modular/composable, not fixed H01/H02/H03 and not fixed first/second/third stages.

Minimum module template v0.1:
- 1 Input
- 1 Module
- 1..N Outputs
- configurable execution mode
- outputs may create Process Materials
- outputs may feed next modules or final packaging

Three known behaviors:

### SOURCE_TRACKED
- Procurement Batch + processing source + employee
- input scale data
- output scale data
- source = Supplier or Farmers Combined

### POOLED_OUTPUT
- source material already pooled by Procurement Batch
- no Supplier/Farmer input
- no actual picked input quantity
- employee records output
- output quantity is also source material deduction quantity

### FINAL_PACKAGING
- source Process Material or Procurement Product
- employee records completed Sales Product quantity
- source deduction = completed quantity × Packaging Weight
- Sales Product inventory increases
- source inventory may go negative
- negative is reconciled only at Procurement Batch closing

## CURRENT BUSINESS FACT — Measurement / Container

Employee always enters what the scale displays.
Employee does not manually subtract container weight.

If material uses container:
- one configured container type per material/config version
- Container Master has bilingual name, tare weight, active
- material config has Default Container Count
- user may modify actual container count
- system derives net quantity

If no container:
- container fields are hidden
- no tare deduction

Processing quantities/weights use max 1 decimal place.

## CURRENT BUSINESS FACT — Products

Three business concepts:

1. Procurement Product
   - formal purchased product master

2. Process Material
   - route/configuration-owned intermediate virtual product
   - inventory traceable
   - may have container, wage, storage mapping

3. Sales Product
   - formal sellable product master

Sales Product Group groups multiple final packaging specifications sharing the same content identity.
The Group itself is not inventory.

## CURRENT BUSINESS FACT — Packaging Weight vs Sales Weight

These are distinct:

- Packaging Weight: actual amount packed; used to deduct source material.
- Sales Weight: declared/billable amount; used for pricing.

Example:
- Packaging Weight = 5.2 kg
- Sales Weight = 5.0 kg

20 packages:
- source consumption = 104 kg
- billing at THB 100/kg = 20 × 5.0 × 100 = THB 10,000

## CURRENT BUSINESS FACT — Inventory

Required capabilities:
- Multi-Lot
- Multi-Warehouse
- Multi-Storage Location

"Multi-Lot" means:
> 1 Procurement Batch = 1 continuing Inventory Traceability Batch

Processing never creates a new procurement lot identity.

Inventory identity dimensions:
- Origin
- Source Batch
- Inventory Object
- Warehouse/Location
- quantity
- optional Raw Source Segment at raw stage

Movement/Ledger is source of truth.
Current Balance is projection/cache.

Movement types include:
- PURCHASE_RECEIPT
- PROCESS_CONSUME
- PROCESS_PRODUCE
- FINAL_PACKAGE_CONSUME
- FINAL_PACKAGE_PRODUCE
- OUTSOURCED_RECEIPT
- TRANSFER_OUT
- TRANSFER_IN
- SALES_ISSUE
- ADJUSTMENT
- BATCH_RECONCILIATION

## CURRENT BUSINESS FACT — Sales

Sales Header:
- Sales Date
- Customer

Sales Detail:
- Sales Product
- Quantity
- Pricing Basis snapshot
- Sales Weight snapshot when weighted
- Unit Price
- Amount

Pricing basis:
- `WEIGHT_BASED_UNIT`
- `UNIT_BASED`

Sales allocation:
- every source batch keeps its own final sellable inventory
- Sales may combine source batches
- system auto-allocates
- authorized user may override

Priority v0.1:
1. OUTSOURCED oldest → newest
2. then IN_HOUSE Procurement Batch oldest → newest

Final allocation is official for inventory and closing.

## CURRENT BUSINESS FACT — Batch Closing

Procurement Batch is sold out only when all sellable inventory belonging to that batch is sold out.
Then all remaining processing-chain positions, positive or negative, are reconciled to zero using explicit `BATCH_RECONCILIATION` movements.
Then the batch is Closed.

Outsourced Batch closes when all products in that batch are sold out.
No processing reconciliation is needed.

## CURRENT BUSINESS FACT — Outsourced Supply

Outsourced Vendor is separate from Supplier.

Outsourced Supply Batch identity:
> same Supply Date + same Outsourced Vendor

Details:
- Sales Product
- Quantity
- pricing snapshot
- Unit Price
- Amount

On confirm:
- OUTSOURCED sellable inventory immediately exists
- Vendor Payable immediately exists

## CURRENT BUSINESS FACT — Labor

Two work traces:
1. Processing Labor from Processing Execution
2. Sales Packaging/Handling Labor

Processing wage:
- configured per processing output/product
- Daily Wage confirmation may override the applied rate
- override does not update configuration
- historical configuration must remain stable

Wage calculation:
- aggregate same day + employee + processing wage item + applicable config
- multiply
- floor final amount
- do not floor detail-by-detail

Sales Packaging/Handling:
- Sales + Work Date + Employee + Work Item + Wage
- day-rate
- latest same Employee + same Work Item wage may be prefilled
- current confirmed wage is fact

Employee wages are paid daily:
Employee Daily Wage → Payable → Payment.

## CURRENT BUSINESS FACT — Finance

Shared pattern:
Source transaction → Receivable/Payable → Adjustments → Settlements → Outstanding

Payable sources:
- Procurement Supplier
- Procurement Farmer
- Company Pickup Transport
- Outsourced Vendor
- Employee Daily Wage

Receivable source:
- Sales

Supplier quality/weight deduction:
- adjust Payable
- do not rewrite Procurement

Company pickup transport:
- Company Pickup is captured at Procurement Entry confirmation
- no Driver Master
- payee recorded at Payable/Payment stage
- current rule basis: Procurement Batch applicable quantity × applicable THB/kg rate
- rate must not be hard-coded

## CURRENT BUSINESS FACT — Localization

System UI languages:
- `zh-TW`
- `th-TH`

Company-visible names support both languages.
Current UI language shows matching name; if missing, fallback to the other existing language.
No automatic formal translation is created.

## CURRENT BUSINESS FACT — Data Lifecycle

Applicable modules support:
- Edit
- Soft Delete

Hard Delete exists only in a separate Data Deletion Protection System for highest authority.

Restore is business-aware.
Hard Delete requires dependency checks.
No silent cascade across core traceability.
Hard Delete itself leaves audit.
