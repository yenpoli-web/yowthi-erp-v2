# ERP Registration / Data Control Boundary v0.1 — YowThi ERP V2

Status: **DECISION / v0.1**
Confirmed: **2026-09-04**

## 1. Purpose

This decision separates three concerns that must not be collapsed into one logic:

1. **Business Fact Registration** — record an operation that is happening or a result that actually happened.
2. **ERP Data Control / Maintenance** — correct, activate/deactivate, soft-delete, restore, reopen, or otherwise maintain ERP data because an operator needs the system record/state changed.
3. **Physical Inventory Reconciliation** — reconcile ERP inventory with physical reality through stocktake / inventory adjustment when loss, damage, weighing variance, handling loss, or other real-world discrepancy exists.

The ERP must not use Business Rules to unnecessarily block legitimate ERP data-control operations.

## 2. Business Fact Registration

Business Rules apply when ERP is recording real YowThi operating facts.

Examples:
- Procurement occurring
- Processing work occurring
- Outsourced supply received
- Sales confirmed
- Payment made
- Receipt received
- Employee work recorded

A Business Command must represent the real operation and may only encode Business Rules supported by real YowThi facts.

If reality contains a second event, record a second Business Fact.

Example:
- a Payment really occurred
- a later Refund really occurred

These are separate real events. The second event must not be disguised as a data correction of the first event.

## 3. ERP Data Control / Maintenance

Changing an ERP record because the registered data or operational state needs maintenance is an ERP Control concern, not automatically a new Business Rule.

Examples:
- correcting a wrongly entered Payment amount
- correcting a wrongly entered Receipt or Adjustment
- correcting a wrong master-data value
- Soft Delete
- Restore
- Activate / Deactivate
- reopening an operational record or Batch so that ERP operations can continue
- highest-authority Hard Delete when the target is structurally safe to remove

These operations do not require inventing a corresponding real-world business event merely to justify the database change.

ERP Control commands still require technical safety:
- authenticated actor
- explicit capability authorization
- target-specific command / endpoint
- Idempotency Key for persisted writes
- expected row version where applicable
- dependency / structural protection
- Audit of the change
- transactional projection rebuild/update where applicable

ERP Control must **not** become generic persistence CRUD. The following remain architecture regressions:

```text
GenericUpdate(type, id, patch)
GenericSoftDelete(type, id)
GenericRestore(type, id)
GenericHardDelete(type, id)
GenericCorrection(type, id, patch)
```

The command remains explicit about the target and control intent even though its justification is ERP maintenance rather than a Business Rule.

## 4. Correction boundary

A correction answers:

> Was the ERP registration wrong, or did reality later produce another event?

If the ERP registration was wrong:
- correct the registered fact through a target-specific ERP Control command
- retain Audit before/after evidence
- re-evaluate affected projections/derived state transactionally
- do not fabricate a compensating Business Fact unless the persistence architecture itself requires append-only technical facts

If reality later produced another event:
- record the new Business Fact
- do not rewrite history to make the earlier real event disappear

This distinction supersedes any older wording that treats every confirmed-data correction as requiring a separate Business Rule or a compensating business event.

Append-oriented technical history remains append-oriented where already architected. For example, Inventory Movement history is not rewritten merely because another ERP record is corrected.

## 5. Finance correction

Payment, Receipt, and Adjustment registration errors are ERP Data Control concerns.

If a confirmed Payment / Receipt / Adjustment was entered incorrectly, a target-specific correction may amend the registered value/state and transactionally rebuild the affected Outstanding projection, subject to:
- row-version / Outstanding concurrency protection
- dependency protection where required
- Audit
- idempotency
- atomic commit

A correction must not be represented as a fake new Payment/Receipt/Refund merely to preserve an artificial business narrative.

If money actually moves again in reality, that is a new Finance Business Fact and must be registered as such.

Therefore `FIN-003` and `FIN-005` are no longer Business Rule blockers. Their remaining work is ERP Control contract/implementation design.

## 6. Lifecycle control

Soft Delete, Restore, Activate/Deactivate, and Reopen are ERP lifecycle controls.

They do not require a new Business Rule for each target merely because the ERP state changes.

Target-specific structural safety still applies:
- target must exist in the applicable lifecycle view
- concurrency must be checked
- impossible structural states must be rejected
- dependent records must not be silently cascaded or corrupted
- Audit must record the lifecycle change

Restore means restoring the ERP record from soft-deleted state. It does not automatically imply `active = true`; the target's existing active/inactive value remains a separate state unless an explicit control command changes it.

Reopen means the ERP object is again open for applicable operations. Reopen does not erase historical Audit or rewrite prior immutable ledger history.

## 7. Closed Batch reopen

A Closed Batch may be reopened as an ERP lifecycle-control operation when an authorized operator needs further ERP activity on that Batch.

Reopen does not mean:
- deleting prior Batch Close history
- deleting prior `BATCH_RECONCILIATION` movements
- rewriting immutable Inventory Movement history
- reconstructing an imagined pre-close physical inventory state

The Batch lifecycle state is reopened; prior inventory ledger facts remain history.

Any mismatch between ERP inventory and physical stock is handled through stocktake / `AdjustInventory`, not by forcing Reopen to reverse historical movements.

Therefore `LIFE-001` is no longer a Business Rule blocker. Its remaining work is lifecycle-control implementation and concurrency/Audit behavior.

## 8. Inventory reality and stocktake

ERP inventory is an accounting/operational record derived from registered movements. Physical inventory can differ because reality is not perfectly deterministic.

Examples include:
- commercial loss / shrinkage
- damage
- weighing variance
- handling loss
- spoilage
- missing stock
- manual operational error
- other physical discrepancies

The correction mechanism for physical discrepancy is Inventory Count / Inventory Adjustment.

Conceptually:

```text
registered inventory movements
→ ERP inventory balance
→ physical count
→ difference identified
→ explicit Inventory Adjustment
→ ERP balance reconciled to physical reality
```

Do not rewrite unrelated historical Procurement, Processing, Sales, or Batch movements solely to force inventory to equal a later physical count.

## 9. Hard Delete

Hard Delete remains a separate highest-authority Data Protection operation.

Business Rules do not decide whether a technically unused master row may be physically deleted. The controlling questions are:
- is this target explicitly supported by a target-specific Hard Delete command?
- does dependency closure prove physical deletion will not corrupt traceability or structural integrity?
- is the actor authorized for `data-protection.hard-delete`?

If dependencies exist, block physical deletion.
If structurally safe, physical deletion may proceed with retained Hard Delete Audit.

No generic `(type, id)` Hard Delete resolver is introduced.

## 10. Authorization boundary

Capability policies are ERP Control / security identifiers, not Business Rules.

Adding a target-specific lifecycle or correction capability does not require inventing a YowThi business role. Capability grants remain deployment-configured by persistent Account UUID under `docs/15-authn-authz-implementation-architecture-v0.1.md`.

The architecture should keep authority proportional to the operation:
- ordinary lifecycle/data-maintenance capability for Soft Delete / Restore / Reopen
- module/operation-specific correction capability for sensitive corrections such as Finance
- `data-protection.hard-delete` remains the highest-authority physical-delete boundary

Exact capability names are technical API/security contract choices and may be introduced with the target-specific implementation slice without creating a Business Rule.

## 11. REST / command interpretation

Older documents sometimes use the phrase "Business Write Command" for every persisted mutation. Under this later clarification, interpret persisted writes as:

```text
Application Write Command
├─ Business Fact Command
└─ ERP Control Command
```

Both use the established technical write controls: authentication, capability authorization, idempotency, concurrency where applicable, Audit, transaction boundaries, and Problem Details.

This clarification supersedes older wording only where that wording incorrectly requires a Business Rule or business event to justify ERP data maintenance. It does not weaken the established no-generic-CRUD, typed-FK, idempotency, Audit, concurrency, or transaction architecture.

## 12. Implementation consequence for P6 V8

The following are no longer blocked merely for lack of a Business Rule:
- target-specific Soft Delete / Restore
- additional target-specific Hard Delete, after dependency closure
- Payment / Receipt / Adjustment data correction
- Closed Batch Reopen

Implementation may proceed as focused ERP Control slices, each with explicit authorization and technical acceptance tests.

Physical inventory discrepancy remains handled by stocktake / Inventory Adjustment rather than by inventing business-event reversals.
