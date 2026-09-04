# Business Rule Gap Register v0.1

## Categories

- A — must be confirmed before affected function goes live
- B — safe v0.1 control can be used without pretending it is a Business Rule
- C — deferred extension; do not implement until real need appears
- RESOLVED — confirmed YowThi Business Fact; retained here for decision history

| ID | Gap | Class | Safe v0.1 handling |
|---|---|---|---|
| PROC-001 | Procurement Batch COMPLETED then late entry | A | Block normal entry |
| PROC-002 | Procurement receipt location default vs override | B | use unique default; otherwise require explicit choice |
| PROC-003 | Procurement Entry zero Net Quantity confirmation | A | Block zero-quantity confirmation until real YowThi behavior is confirmed; Procurement Entry currently permits zero but required PURCHASE_RECEIPT structurally requires a positive movement |
| PROCESS-001 | Input spread across multiple locations | A | require explicit source if ambiguous |
| PROCESS-002 | Processing output location | B | unique default or explicit choice |
| PROCESS-003 | Multi-input Processing Module | C | not implemented |
| PROCESS-004 | Negative inventory for modes other than Final Packaging | C | disallow unless explicitly configured/confirmed |
| OUT-001 | Outsourced receipt location | B | unique default or explicit choice |
| OUT-002 | Outsourced Supply Detail zero Quantity confirmation | A | Block zero-quantity confirmation until real YowThi behavior is confirmed; Outsourced Supply Detail currently permits zero but required OUTSOURCED_RECEIPT structurally requires a positive movement |
| OUT-003 | Outsourced Supply Batch CLOSED then late detail | A | Block normal late detail until real YowThi behavior is confirmed; do not silently reopen or write new inventory into a closed batch |
| SALES-001 | Sales issue location when stock spans locations | A | require explicit resolution if ambiguous |
| SALES-002 | Sales Product negative inventory / presales | C | not implemented |
| SALES-003 | Confirmed Sales allocation correction submission semantics: complete replacement allocation vs manual overrides plus system re-allocation | A | Keep immutable Allocation Revision / compensating Inventory architecture, but do not expose an allocation-correction command until real YowThi correction input behavior is confirmed |
| BATCH-001 | Automatic vs user-confirmed batch close | A | sold-out detection separate from Close command |
| INV-001 | Inventory count UI enters absolute or delta | B | ledger always stores delta |
| INV-002 | Fixed adjustment reason codes | C | store context first |
| HANDLING-001 | Sales lifecycle state required before handling work | RESOLVED | Confirmed 2026-09-03: Sales Packaging / Handling Work may be recorded while Sales is either DRAFT or CONFIRMED |
| HANDLING-002 | Multiple same day-rate entries per sale/date/employee/item | B | no business unique constraint yet |
| LABOR-001 | Late work after Daily Wage confirmed | A | block normal late work |
| FIN-001 | Payment > Outstanding | A | block |
| FIN-002 | Receipt > Outstanding | A | block |
| FIN-003 | Confirmed Payment/Receipt correction method | A | no generic edit/delete |
| FIN-004 | Deduction causing negative Payable Outstanding | A | block |
| FIN-005 | Adjustment correction/reversal method | A | no generic edit |
| FIN-006 | Adjustment types other than supplier deduction | C | not implemented |
| FIN-007 | Company Pickup Transport final THB rounding rule | A | persist applicable quantity, applied THB/kg rate, and confirmed final THB amount; do not assume floor/round; confirm before transport payable goes live |
| FIN-008 | Company Pickup Transport payable grouping / confirmation boundary | A | persist per-entry transport basis; do not impose a business unique grouping or automatically decide which basis records form one Payable; confirm before transport payable goes live |
| FIN-009 | Company Pickup Transport payee recording semantics | A | no Driver Master; do not persist payee on Procurement/transport basis; confirm whether payee belongs to Payable, Payment, or both before transport settlement goes live |
| LIFE-001 | Reopen Closed Batch | C | not implemented |
| CONFIG-001 | Multiple simultaneously selectable active routes per Procurement Product | C | not implemented |

These gaps do not currently block persistence architecture design.
They must not be silently encoded as permanent database Business Rules.
