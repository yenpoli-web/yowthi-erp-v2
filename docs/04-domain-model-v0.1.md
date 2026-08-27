# Domain Model / Aggregate Boundary v0.1

## Aggregate Roots / Write Models

### Party
- Supplier
- Farmer
- Employee
- Customer
- Outsourced Vendor

### Product
- Procurement Product
- Sales Product Group
- Sales Product

### Processing Configuration
- Processing Route
  - Route Version
  - Processing Module
  - Process Material
  - Module Outputs
  - Final Packaging mapping

### Infrastructure
- Container
- Warehouse
  - Storage Location

### Procurement
- Procurement Batch
  - Procurement Entry

### Processing
- Processing Execution
  - Input fact
  - 1..N output results

### Inventory
- Inventory Movement is append-oriented ledger truth
- Inventory Position is current projection/concurrency boundary

### Outsourced
- Outsourced Supply Batch
  - Outsourced Supply Detail

### Sales
- Sales
  - Sales Detail
  - Sales Allocation

### Sales Handling
- Sales Packaging Work Record

### Labor
- Employee Daily Wage
  - Processing Wage Components
  - Sales Packaging Wage Components

### Finance
- Payable
  - Adjustments
  - Payments
- Receivable
  - Adjustments
  - Receipts

### Audit / Protection
Do not create an Everything Aggregate.
The protection system is a controlled entry point; each owning domain decides whether Edit/Delete/Restore/Hard Delete is valid.

## Important aggregate decisions

- Batch Lifecycle is not a separate aggregate.
- Sales Allocation is inside Sales aggregate because allocation total must equal Sales Detail quantity.
- Inventory Position is not the historical truth.
- Farmers Combined is not a Party.
- Sales Product Group is not inventory.
- Process Material is not a general product master.
- Origin is not stored on Sales Product.
- No Universal Product aggregate.
- No Universal Party aggregate is required in v0.1.
