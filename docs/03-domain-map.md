# Core Domain Map v0.1

## Modules

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

## DEFERRED

Do not design yet:
- Costing / margin
- Reporting / analytics

They will consume established transaction facts after the architecture is complete.

## Main in-house chain

Procurement Product
→ Procurement Batch
→ Procurement Entries
→ Raw Inventory
→ Processing Route / Processing Executions
→ Process Materials
→ Final Packaging
→ Sales Product Group / Sales Product
→ Sellable Inventory
→ Sales Allocation
→ Sold-out
→ Batch Reconciliation
→ Closed

## Outsourced chain

Outsourced Vendor
→ Outsourced Supply Batch
→ Outsourced Supply Details
→ OUTSOURCED Sellable Inventory
→ Sales Allocation
→ Sold-out
→ Closed

## Finance chain

Source Transaction
→ Receivable / Payable
→ Adjustment
→ Outstanding
→ Payment / Receipt

## Labor chain

Processing Execution
+
Sales Packaging Work
→ Employee Daily Wage
→ Payable
→ Payment
