# Business Rule Gap Register v0.1

## Categories

- A — must be confirmed before affected Business Fact behavior goes live
- B — safe v0.1 control can be used without pretending it is a Business Rule
- C — deferred business extension; do not implement until real need appears
- RESOLVED — confirmed YowThi Business Fact; retained here for decision history
- CONTROL — ERP Data Control / maintenance concern, not a Business Rule gap; implementation is governed by technical authorization, concurrency, dependency, Audit, idempotency, and transaction safety

The later registration/control boundary is authoritative for classification:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

| ID | Gap / concern | Class | Safe v0.1 handling / current decision |
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
| OUT-003 | Outsourced Supply Batch CLOSED then late detail | A | Block normal late detail until real YowThi behavior is confirmed; do not silently reopen within the Business Fact command |
| SALES-001 | Sales issue location when stock spans locations | A | require explicit resolution if ambiguous |
| SALES-002 | Sales Product negative inventory / presales | C | not implemented |
| SALES-003 | Confirmed Sales allocation correction submission semantics | RESOLVED | Confirmed 2026-09-04: both operating modes occur. `COMPLETE_REPLACEMENT` supplies the full official replacement allocation set; `OVERRIDE_AND_REALLOCATE` supplies explicit overrides first and re-runs the established allocation priority for the remaining quantity. Caller selects the mode explicitly; the system does not infer it. |
| BATCH-001 | Automatic vs user-confirmed batch close | A | sold-out detection separate from Close command |
| INV-001 | Inventory count UI enters absolute or delta | B | ledger always stores delta; physical discrepancy is reconciled through Inventory Count / Adjustment |
| INV-002 | Fixed adjustment reason codes | C | store operational context first |
| HANDLING-001 | Sales lifecycle state required before handling work | RESOLVED | Confirmed 2026-09-03: Sales Packaging / Handling Work may be recorded while Sales is either DRAFT or CONFIRMED |
| HANDLING-002 | Multiple same day-rate entries per sale/date/employee/item | B | no business unique constraint yet |
| LABOR-001 | Late work after Daily Wage confirmed | A | block normal late work |
| FIN-001 | Payment > Outstanding | A | block |
| FIN-002 | Receipt > Outstanding | A | block |
| FIN-003 | Correction of wrongly registered confirmed Payment/Receipt | CONTROL | Confirmed control boundary 2026-09-04: registration error is ERP Data Correction, not a new Business Fact. Use target-specific correction, Audit before/after, concurrency, idempotency, and rebuild affected Outstanding. V8-C6 implements direct Payment amount correction through `CorrectPaymentAmount`; V8-C7 implements direct Receipt amount correction through `CorrectReceiptAmount`. If money actually moves again, record a new Finance Business Fact. |
| FIN-004 | Deduction causing negative Payable Outstanding | A | block |
| FIN-005 | Correction of wrongly registered Payable Adjustment | CONTROL | Confirmed control boundary 2026-09-04: registration error is ERP Data Correction. V8-C8 implements target-specific `CorrectPayableAdjustment` with Audit before/after, Outstanding concurrency/idempotency, projection rebuild, and no fabricated reversal business event. If reality contains another later-world event, record that new Business Fact separately. |
| FIN-006 | Adjustment types other than supplier deduction | C | not implemented |
| FIN-007 | Company Pickup Transport final THB rounding rule | A | persist applicable quantity, applied THB/kg rate, and confirmed final THB amount; do not assume floor/round; confirm before transport payable goes live |
| FIN-008 | Company Pickup Transport payable grouping / confirmation boundary | A | persist per-entry transport basis; do not impose a business unique grouping or automatically decide which basis records form one Payable; confirm before transport payable goes live |
| FIN-009 | Company Pickup Transport payee recording semantics | A | no Driver Master; do not persist payee on Procurement/transport basis; confirm whether payee belongs to Payable, Payment, or both before transport settlement goes live |
| LIFE-001 | Reopen Closed Batch | CONTROL | Confirmed control boundary 2026-09-04: Reopen is ERP lifecycle control. V8-C9 implements target-specific Procurement Batch reopen through `ReopenProcurementBatch`: current lifecycle returns to ACTIVE, structural current close markers are cleared, prior Close Audit and immutable `BATCH_RECONCILIATION` history remain, and physical inventory mismatch is handled through stocktake / `AdjustInventory`. Other Batch types remain separate target-specific control scope. |
| CONFIG-001 | Multiple simultaneously selectable active routes per Procurement Product | C | not implemented |

## Classification rule

Business Rule gaps govern **how real YowThi operations are recorded**.

ERP Data Control concerns do not need a Business Rule merely to permit maintenance of ERP state. They are governed by:
- target-specific command/endpoint
- authorization capability
- structural/dependency safety
- concurrency
- Audit
- idempotency
- transaction/projection consistency

Do not use this register to block legitimate ERP data maintenance simply because no separate business event exists.

Conversely, do not use ERP Data Correction to hide a real later-world event. If reality contains another Payment, Receipt, Refund, inventory loss, or other event, record the appropriate new Business Fact / Inventory Adjustment.

Business-rule safe/deferred controls must not be silently encoded as permanent database Business Rules.