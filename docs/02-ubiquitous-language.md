# Ubiquitous Language v0.1

| Term | Meaning |
|---|---|
| Procurement Product | Real purchased raw product master |
| Procurement Batch | Same procurement date + same Procurement Product |
| Procurement Entry | One confirmed raw procurement fact from one Supplier/Farmer |
| Supplier | Long-term raw-material supplier |
| Farmer | Local farmer raw-material source |
| Farmers Combined | Processing source segment representing physically mixed farmer material within one Procurement Batch |
| Processing Route | Configurable material transformation graph |
| Processing Route Version | Historical, immutable route configuration version used by a batch |
| Processing Module | One configurable transformation step |
| Process Material | ERP-traceable intermediate virtual product owned by route configuration |
| SOURCE_TRACKED | Processing behavior with supplier/farmers-combined source and scale input/output |
| POOLED_OUTPUT | Processing behavior where only output is entered and output quantity is also source deduction |
| FINAL_PACKAGING | Processing behavior that produces Sales Product inventory and consumes source via Packaging Weight |
| Packaging Weight | Actual production consumption weight per Sales Product unit |
| Sales Weight | Declared/billable weight per Sales Product unit |
| Sales Product Group | Common content identity grouping final packaging variants; not inventory |
| Sales Product | Actual sellable/stocked product specification |
| Origin | `IN_HOUSE` or `OUTSOURCED` |
| Source Batch | Procurement Batch or Outsourced Supply Batch |
| Inventory Movement | Append-oriented stock quantity fact |
| Inventory Position | Current stock projection for one complete stock identity |
| Outsourced Vendor | Vendor providing ready-to-sell outsourced products |
| Outsourced Supply Batch | Same date + same Outsourced Vendor |
| Sales Allocation | Final mapping of a Sales Detail quantity to source batches |
| Sales Packaging Item | Sales handling/day-rate work item |
| Sales Packaging Work | Employee work caused by a Sales transaction; not production packaging |
| Employee Daily Wage | One employee's formal wage for one Work Date |
| Payable | Company's financial obligation |
| Receivable | Customer financial obligation to company |
| Adjustment | Financial obligation change that does not rewrite original source transaction |
| Settlement | Payment or Receipt |
| Outstanding | Original + Adjustments - Settlements |
| BATCH_RECONCILIATION | Explicit movement used to zero remaining in-house batch inventory at closing |
| Soft Delete | Removes record from normal use while retaining data |
| Hard Delete | Highest-authority permanent deletion after dependency validation |
