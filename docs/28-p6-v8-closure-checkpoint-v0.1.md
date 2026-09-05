# P6 / V8 Closure Checkpoint v0.1 — YowThi ERP V2

Status: **P6 COMPLETE / V8 COMPLETE**

This document closes the current v0.1 P6 Business / ERP Control vertical-slice phase after V8-C18. It does not claim that every conceivable ERP maintenance operation has been implemented, and it does not redefine unresolved YowThi Business Facts.

## 1. Closure rule

`docs/13-implementation-sequencing-build-plan-v0.1.md` defines P6 as vertical business slices and V8 as:

```text
Correction / Lifecycle / Hard Delete commands
```

That sequencing label is not an exhaustive requirement to add lifecycle or Hard Delete to every master relation.

P6/V8 is complete when:
- the required current v0.1 Business Fact vertical slices are implemented
- confirmed ERP Control concerns required by the current operating baseline are implemented
- remaining candidate controls that would require unresolved Business Fact interpretation are explicitly deferred
- optional additional control targets are not treated as mandatory merely because a technical command could be built
- no unresolved Business Rule is silently decided in order to keep P6 open or to close it

## 2. P6 Business Fact vertical slices

Current P6 vertical slices are complete through V1–V7:
- V1 `ConfirmProcurementEntry`
- V2 `ConfirmOutsourcedSupplyDetail`
- V3 `ConfirmProcessingExecution`
- V4 `ConfirmSales`
- V5 Sales Packaging Work + Employee Daily Wage
- V6 Finance adjustment / settlement
- V7 Inventory Transfer / Adjustment + Batch Close

Each implemented persisted Business Command retains the established architecture for applicable:
- authenticated actor
- persistent CommandId idempotency
- explicit transaction boundary
- typed persistence
- Audit
- Outbox where applicable
- API contract / Problem Details
- PostgreSQL integration and concurrency acceptance

## 3. V8 completed ERP Control scope

V8 completed the focused control scope established during implementation:

- C1 Supplier Hard Delete
- C2 Customer Hard Delete
- C3 Sales Allocation Correction
- ERP Registration / Data Control boundary clarification
- C4 Supplier Soft Delete / Restore
- C5 Customer Soft Delete / Restore
- C6 Payment Amount Correction
- C7 Receipt Amount Correction
- C8 Payable Adjustment Correction
- C9 Procurement Batch Reopen
- C10 Outsourced Vendor Soft Delete / Restore
- C11 Farmer Soft Delete / Restore
- C12 Employee Soft Delete / Restore + current-use command guards
- C13 Sales Packaging Item Soft Delete / Restore
- C14 Sales Product Group Soft Delete / Restore
- C15 Container Soft Delete / Restore
- C16 Warehouse Soft Delete / Restore
- C17 Outsourced Vendor Hard Delete
- C18 Farmer Hard Delete

The target-specific control supplements remain authoritative for their individual scopes.

## 4. Gap Register closure hard gate

The Business Rule Gap Register contains three concerns classified `CONTROL`.

### FIN-003

Correction of wrongly registered Payment / Receipt:
- `CorrectPaymentAmount` complete in C6
- `CorrectReceiptAmount` complete in C7

### FIN-005

Correction of wrongly registered Payable Adjustment:
- `CorrectPayableAdjustment` complete in C8

### LIFE-001

Reopen Closed Batch:
- Procurement Batch Reopen complete in C9
- other Batch types remain separate target-specific control scope
- no generic Batch Reopen command is introduced

No additional unresolved `CONTROL` item in the current Gap Register requires implementation before P6/V8 closure.

## 5. OUT-003 remains unresolved

`OUT-003` remains a Class A Business Rule gap.

Current `ConfirmOutsourcedSupplyDetail` behavior blocks late detail while the Outsourced Supply Batch is `CLOSED`.

Changing that Batch back to `ACTIVE` would currently change whether a late real Outsourced Supply Detail can be registered. Therefore an Outsourced Supply Batch Reopen command would decide an unconfirmed Business Fact unless a future control design can preserve the unresolved behavior safely.

Result:
- Outsourced Supply Batch Reopen remains **DEFERRED**
- P6/V8 closure does not resolve or reinterpret `OUT-003`

## 6. Deferred lifecycle candidates

The following lifecycle targets remain explicitly deferred because current-use behavior is not uniformly resolved:

- `StorageLocation` — DEFERRED
- `ProcessMaterial` — DEFERRED
- `ProcessingRoute` — DEFERRED

The following product lifecycle targets remain TO VERIFY / DEFERRED because changing current master lifecycle may affect already-created Business Fact workflows:

- `ProcurementProduct` — TO VERIFY / DEFERRED
- `SalesProduct` — TO VERIFY / DEFERRED

These are not P6 completion blockers.

They become implementation candidates only after the affected current-use / Business Fact behavior is confirmed or a control design can preserve the unresolved facts safely.

## 7. Additional Hard Delete targets

Hard Delete remains target-specific Data Protection control.

Completed targets:
- Supplier
- Customer
- Outsourced Vendor
- Farmer

No architecture document requires Hard Delete for every master relation in v0.1.

Additional targets such as Employee, Container, Warehouse, Storage Location, Product/configuration masters, or other relations are therefore **DEFERRED / future target-specific scope** unless a real operational Data Protection need is established.

A future Hard Delete target still requires:
- repository-wide typed dependency closure
- explicit no-cascade/no-history-rewrite decision
- concurrency
- persistent idempotency
- Audit
- authorization
- target-specific endpoint/command

No generic Hard Delete resolver is permitted.

## 8. Other future ERP Control targets

Additional lifecycle, correction, reopen, or Hard Delete operations may be added later when there is a real system-maintenance need.

Future eligibility does not make them required P6 work.

Any future target must remain one of:
- technically closed ERP Control with no Business Fact reinterpretation, or
- explicitly blocked/deferred pending real YowThi Business Fact confirmation

## 9. Architecture boundaries preserved

P6/V8 closure does not change:
- .NET 10 / ASP.NET Core 10 baseline
- EF Core 10 + Npgsql
- PostgreSQL 18
- Modular Monolith
- single write `ErpDbContext`
- UUID v7
- explicit `row_version bigint`
- persistent CommandId idempotency
- transactional Outbox where applicable
- append-oriented Audit
- typed real FKs
- target-specific commands
- no Generic Repository / generic CRUD architecture

No relation/schema/model snapshot/migration change is introduced by this closure checkpoint.

`InitialV01` remains unchanged.

## 10. Business Rule boundary

P6/V8 closure introduces no new YowThi Business Rule.

Existing unresolved Class A gaps remain authoritative. Existing Class C extensions remain unimplemented until real need is confirmed.

ERP Control remains separate from Business Fact Registration according to:
- `docs/17-erp-registration-data-control-boundary-v0.1.md`

The closure decision is about implementation scope, not about declaring unknown business behavior resolved.

## 11. Formal implementation baseline

Baseline before this closure-document commit:
- `main = origin/main`
- SHA `5ba09434827c5081161739b5172ccab574ec5026`
- commit `docs: checkpoint farmer hard delete`
- working tree clean
- Formal r18 primary formal channel
- Bootstrap r2 independent recovery/read-back channel

C18 implementation remains validated at:
- `0871bd006dfe1fa48db0b305a043277e09debfa2`
- local hard gates 328/328 PASS

C18 checkpoint commit:
- `5ba09434827c5081161739b5172ccab574ec5026`
- exact checkpoint validation completed successfully on the configured Windows self-hosted runner before promotion to `main`

## 12. P7 boundary

P7 React UI is **not** completed by this checkpoint.

Current React shell/routes exist, but UI design remains intentionally deferred until the system skeleton is complete.

Observed preview follow-up for P7:
- current visual style is not accepted as the final ERP design
- Traditional Chinese / Thai language switching is not yet complete across the presentation
- these are UI/i18n concerns, not P6 Business Rule blockers

P7 must later follow `docs/16-adaptive-web-ui-architecture-v0.1.md` and the three-experience Desktop / Tablet / Mobile acceptance model.

## 13. Closure result

Formal phase state after this checkpoint is accepted:

```text
P6   Business / ERP Control vertical slices   COMPLETE
V8   Correction / Lifecycle / Hard Delete     COMPLETE
```

Remaining listed lifecycle / Hard Delete / Batch-control candidates are explicitly deferred or future target-specific scope and therefore do not keep P6 open.

Next work should evaluate the remaining **system skeleton / technical phase boundary** before starting P7 visual redesign.

P7 remains NOT FORMALLY COMPLETE.
P8 remains FUTURE.
