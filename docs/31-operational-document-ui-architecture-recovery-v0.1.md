# Operational Document UI Architecture Recovery v0.1

Status: ACTIVE RECOVERY BASELINE
Date: 2026-09-08

## 1. Recovery trigger

The P7/P8 presentation work proved that command endpoints were technically operable, but that is not sufficient to call an ERP operational module complete. The current UI frequently collapses a document/header and its detail lines into one command form. This produces incorrect operator semantics even where the relational/domain model already contains the correct parent/child structure.

This recovery does not redefine YowThi Business Rules. It restores the UI/Application boundary so existing verified Business Facts are represented as ERP documents and details. Unverified operating facts remain Gap / TO VERIFY / DEFERRED.

## 2. Confirmed global defects

1. Operational module roots are sometimes command-entry routes instead of module workspaces. `procurement`, `outsourced`, and `processing` currently point directly to `/.../new`.
2. Header fields and detail fields are rendered as one flat form. Operators cannot select/create a document and then manage multiple details inside that document.
3. Searchable foreign-key fields are implemented as a visible search input stacked above a second select. The first control has no visible business meaning and appears as an unexplained blank field on mobile.
4. Page-level language controls remain in Procurement, Outsourced, and Processing even though locale ownership belongs to the application shell.
5. Persistent instructional/hint UI remains in operational pages (`default-hint`, `required-hint`, command-explanation copy), contrary to the confirmed hint-free presentation requirement. Operational validation/error messages remain allowed when an action actually requires them.
6. Existing presentation acceptance has false-positive coverage: it only counts `<LocaleControl />` components and `placeholder=` attributes, so it misses manually rendered `.locale-control` blocks and other hint classes.
7. Technical command result panels expose UUID-oriented results instead of returning the operator to a document workspace/list context. UUIDs may remain in diagnostics/audit but are not the primary operational UX.
8. Visual acceptance proves viewport/layout/locale rendering only. It must not be used as evidence that an operational module is complete.

## 3. Required operational UI shape

Every document-oriented operational module must use this structure unless the underlying Business Fact is explicitly not a document:

```text
Module
├─ Document List / Search / Status filter
└─ Document Workspace
   ├─ Header
   ├─ Detail Lines / Related Lines
   ├─ Totals / State / Projection where applicable
   ├─ Related Records where applicable
   └─ Target-specific Actions
```

Header fields and detail fields are separate. A detail may be added from inside the selected/current header workspace. Confirm/Close/Settlement commands are actions on an already understandable document/workspace; they are not substitutes for the document UI.

## 4. Field interaction hard rules

- Direct user data: text/number/date/input controls.
- Foreign-key/entity selection: one searchable selector/combobox. Do not render a standalone search input plus a separate select for one business field.
- System-derived/display-only values: read-only display. Do not render a second editable control.
- Locale control: application shell only.
- Persistent instruction/hint text: prohibited. Validation/error text is shown only when operationally necessary.
- Mobile form controls remain at least 16 px to avoid iOS focus zoom.
- No horizontal viewport overflow or non-scroll floating canvas behavior.

## 5. Module recovery map

### Procurement

Confirmed model:
- Header: `ProcurementBatch`
- Header identity: Procurement Date + Procurement Product
- Details: `ProcurementEntry`
- Existing add-detail command: `ConfirmProcurementEntry` resolves/locks the date+product Batch and appends one Entry atomically with Inventory/Finance/Audit/Outbox effects.
- Existing control commands: Close and Reopen are implemented and remain target-specific. The `COMPLETED` procurement state exists in the model and `PROC-001` governs late entry after completion, but no formal `CompleteProcurementBatch` command/API exists in the current repository. UI recovery must not invent that transition; it remains a separate contract gap to resolve before exposing a Complete action.

Recovery requirement:
- `/procurement` becomes the module workspace.
- Batch list/workspace query is required.
- Header shows date/product/status/lifecycle.
- Entry list is inside the Batch.
- `ConfirmProcurementEntry` becomes Add/Confirm Detail within the selected Batch context; supplier/farmer is a detail field, not Batch identity.
- No separate Business Rule for Batch creation is introduced merely for UI convenience.

Gap classification: API query gap + UI architecture gap. Existing write command is reusable for detail registration.

### Outsourced Supply

Confirmed model:
- Header: `OutsourcedSupplyBatch`
- Header identity: Supply Date + Outsourced Vendor
- Details: `OutsourcedSupplyDetail`
- Existing `ConfirmOutsourcedSupplyDetail` resolves the Batch and writes a Detail.

Recovery requirement:
- `/outsourced` becomes Batch workspace.
- Batch list/workspace query required.
- Product/quantity/price/location are detail fields inside selected Batch.

Gap classification: API query gap + UI architecture gap. Existing write command is reusable for detail registration.

### Processing

Confirmed model:
- Header: `ProcessingExecution`
- Details: `ProcessingExecutionInput` and `ProcessingExecutionOutput`
- Existing `ConfirmProcessingExecution` creates the execution and related input/output facts atomically according to existing module configuration.

Recovery requirement:
- `/processing` becomes execution workspace/list.
- Header fields are separated from input/output line sections.
- Configured output definitions remain configuration-driven; recovery must not invent add/remove output Business Rules.
- Historical execution workspace/list query is required.

Gap classification: API query gap + UI architecture gap. Existing confirmation write remains reusable.

### Sales

Confirmed model:
- Header: `Sale`
- Details: `SalesDetail`
- Related confirmed allocation: `SalesAllocation`, `SalesAllocationRevision`, `SalesAllocationRevisionItem`
- Existing query can read DRAFT Sale workspaces.
- Existing `ConfirmSales` confirms an existing DRAFT; it does not create the Sale/header or its details.

Recovery requirement:
- `/sales` becomes Sale list/workspace.
- Create DRAFT Sale and target-specific detail add/update/remove/control contracts are required before Sales UI can be called complete.
- Allocation remains a confirm/correction-related structure, not a generic editable detail grid.

Gap classification: write-contract gap + UI architecture gap.

### Sales Packaging / Handling

Confirmed model:
- Parent business context: Sale
- Related lines: `SalesPackagingWorkRecord`
- Existing `RecordSalesPackagingWork` records one work fact.

Recovery requirement:
- Present packaging work as related records under a Sale-oriented workspace or equivalent target-specific operational list.
- Work Date / Employee / Packaging Item / Confirmed Wage are record fields, not a fake standalone document header.
- Add historical work-record query.

Gap classification: API query gap + UI architecture gap.

### Labor

Confirmed model:
- Header: `EmployeeDailyWage` identified by Work Date + Employee
- Details/components: `ProcessingWageComponent`, `ProcessingWageComponentSource`, `SalesPackagingWageComponent`
- Existing pre-confirm workspace query resolves pending processing targets and sales-packaging totals.

Recovery requirement:
- Employee/day workspace shows component detail before confirmation and confirmed historical component detail afterward.
- Add/extend historical query as needed.

Gap classification: historical query gap + UI architecture gap.

### Finance

Confirmed model:
- Header/accounting object: `Payable` / `Receivable`
- Related detail/history: obligation items, adjustments, payments/receipts, outstanding projection
- Existing settlement list query exposes only summary selection.

Recovery requirement:
- `/finance` becomes payable/receivable list + ledger workspace.
- Selected header shows obligations, adjustments, settlements and Outstanding.
- Pay/Receive/Add Adjustment/Correction are actions/related records inside the workspace.
- Historical facts are not generic editable grid rows; corrections remain target-specific ERP Control commands.

Gap classification: detailed query gap + UI architecture gap.

### Inventory

Confirmed model:
- Operation header: `InventoryOperation`
- Movement details: `InventoryMovement`
- Current balance projection: `InventoryPosition`
- Existing Transfer/Adjust commands create target-specific operations/movements.

Recovery requirement:
- `/inventory` begins from current Inventory Positions and operation history, not an isolated command form.
- Transfer/Adjustment opens an operation workspace/dialog with movement semantics.
- Inventory Position remains a projection, never generic direct CRUD.

Gap classification: operation-history query gap + UI architecture gap.

## 6. Control/master modules

Party, Infrastructure, Product, Security and Data Protection are not forced into document/header-detail semantics. Their existing target-specific master/lifecycle architecture remains applicable. However the shared field interaction rules still apply: no ambiguous stacked search+select fields, no duplicate page locale control, no persistent hint UI, no route replacement that makes sibling master/control modules disappear.

## 7. Acceptance gates before any operational module may be called COMPLETE

An operational module is not complete unless all applicable gates pass:

1. Module registry route is a module root/workspace, not a direct `/new` command form.
2. Existing documents/operations can be listed/searched.
3. Header fields are separately identifiable from detail fields.
4. Details/related records are visible inside the selected header workspace.
5. Applicable detail creation/registration is initiated inside the header workspace.
6. Target-specific confirm/close/settle/control actions remain available at the correct level.
7. Entity selectors use one searchable selector control.
8. Application shell is the only locale-control owner.
9. No persistent informational hint banners/fields.
10. Desktop/Tablet/Mobile visual and interaction acceptance pass.
11. Runtime read/write acceptance proves the full workspace flow, not only one command endpoint.
12. Exact-SHA self-hosted restore/build/test passes before main promotion.

## 8. Recovery sequencing

1. Harden presentation/operational acceptance so current known anti-patterns fail.
2. Introduce shared searchable selector primitive and migrate all stacked option-picker controls.
3. Correct operational module registry/root routes.
4. Procurement document workspace first, because its Batch identity and Detail relationship are already formally confirmed and the existing write command can be reused.
5. Outsourced, Processing, Sales, Sales Handling, Labor, Finance, Inventory in that order, adding only the query/write contracts required by existing confirmed domain facts.
6. Run a final cross-module completeness audit before declaring the operational presentation layer complete.

## 9. Non-goals

- No new Business Rule merely to simplify UI.
- No generic Repository/CRUD/document framework that erases target-specific command semantics.
- No migration/schema change unless a verified existing relational gap is found.
- No modification of Legacy ERP.
