# P7 System Skeleton Closure Checkpoint v0.1 — YowThi ERP V2

Status: **P7 SYSTEM SKELETON S0–S12 COMPLETE / PRESENTATION REDESIGN OPEN**

This checkpoint records the formal closure of the P7 React application system skeleton after S12. It confirms that all 12 registered ERP web modules now have operational routes and real vertical-slice functionality. It does **not** claim that the current visual design is accepted as final, and it does not close the remaining Desktop / Tablet / Mobile presentation redesign or full Traditional Chinese / Thai presentation-quality acceptance.

## 1. Closure rule

P7 system-skeleton closure means:
- the React application foundation is established
- shared locale and API transport foundations are established
- the ERP module registry contains no remaining skeleton module
- all 12 current ERP web modules expose an operational route backed by real application/API behavior
- target-specific authorization, concurrency, idempotency, and Business Fact / ERP Control boundaries remain enforced by the existing backend architecture
- no unresolved Business Fact is invented merely to make a UI module operational
- every S0–S12 candidate was validated at its exact SHA on the configured Windows self-hosted runner before formal main promotion

P7 system-skeleton closure does **not** mean:
- the current visual style is final
- the current navigation/information architecture is final
- all Desktop / Tablet / Mobile UX acceptance is complete
- every zh-TW / th-TH string has final presentation-quality review
- deferred lifecycle / Hard Delete / Business Rule gaps are resolved

## 2. Formal implementation baseline

Formal baseline before this checkpoint-document commit:
- repository: `yenpoli-web/yowthi-erp-v2`
- local repository: `C:\Dev\yowthi-erp-v2`
- `main = origin/main`
- SHA: `137190098bb343ce072957dba25c50631c433092`
- commit: `feat(data-protection): add hard delete ui`
- working tree: clean
- promotion policy: fast-forward only
- push policy: non-force

S12 final local technical gates before publication:
- Release solution build: 0 warnings / 0 errors
- .NET tests: **339 / 339 PASS**
- ESLint: PASS
- TypeScript: PASS
- Vite production build: PASS, 174 modules transformed

## 3. P7 slice chain and exact-SHA validation evidence

All historical evidence below was re-read during this closure checkpoint. Every row reports `dotnet-self-hosted` / `.github/workflows/dotnet.yml`, exactly one `build-test` job, runner `YowThi-ERP-V2`, labels `self-hosted` + `yowthi-erp-v2`, completed/success, and `eligibleForMainFastForward=true`.

| Slice | Scope | Exact SHA | Run | Job |
|---|---|---|---:|---:|
| S0 | Web foundation / frontend toolchain | `c452334ab4add8e6182a5f036b6e61f5955053ce` | `33979180944` | `101341164573` |
| S1 | Operational locale / zh-TW + th-TH foundation | `49d46b11b4d2b516c1b9a1194ac069aa2ad8769f` | `33993636342` | `101380138880` |
| S2 | Shared API transport | `254fe76bca194a8b47273995e9ab2c2d2c246bce` | `33996193586` | `101387060818` |
| S3 | ERP module skeleton / routing registry | `dba8f9fd84c5c9e87e767d6ef0f6a20e6870aa26` | `33999660933` | `101396132517` |
| S4 | Sales confirmation foundation | `008c3bca3fd0c3a82211667d39f6a569895d7ada` | `34001902304` | `101402140360` |
| S5 | Sales Handling packaging work | `603e43b624361a71341edc4594c574a61d73777d` | `34003182065` | `101405582557` |
| S6 | Labor daily wage | `ad0292c62ac99e9804c160692e7fc5a4f73bcb10` | `34010265880` | `101424733872` |
| S7 | Finance settlement | `fdf36297390a1f8574fe50163d1e451f735d8238` | `34014504114` | `101435848691` |
| S8 | Inventory transfer / adjustment operations | `a57d7c40d2660d7496d793bbe1d6bc3d854632c1` | `34016830422` | `101441957689` |
| S9 | Party lifecycle workspace | `2d1eb9af8f55de3ff042f4aab35de0ed7d6022ac` | `34019788927` | `101450115599` |
| S10 | Infrastructure lifecycle workspace | `626329e2b897d540d68fd50a6515554d5913a538` | `34022563248` | `101457688150` |
| S11 | Sales Product Group lifecycle workspace | `c1d07fb1452dc4b2ebc49f636822332463127b9d` | `34027184946` | `101470136106` |
| S12 | Data Protection Hard Delete workspace | `137190098bb343ce072957dba25c50631c433092` | `34029172393` | `101475445145` |

No S0–S12 slice is accepted solely from an unvalidated local candidate.

## 4. Operational web modules — 12 / 12

The current module registry now has all modules in operational state:

| Module | Operational route | Current functional scope |
|---|---|---|
| Procurement | `/procurement/entries/new` | Confirm Procurement Entry |
| Outsourced | `/outsourced/supply-details/new` | Confirm Outsourced Supply Detail |
| Processing | `/processing/executions/new` | Confirm Processing Execution |
| Sales | `/sales` | Confirm Sales |
| Sales Handling | `/sales-handling` | Record Sales Packaging Work |
| Labor | `/labor` | Confirm Employee Daily Wage |
| Finance | `/finance` | Pay / Receive settlement operations |
| Inventory | `/inventory` | Transfer Inventory / Adjust Inventory |
| Party | `/party` | Supplier / Customer / Outsourced Vendor / Farmer / Employee lifecycle controls |
| Infrastructure | `/infrastructure` | Container / Warehouse lifecycle controls |
| Product | `/product` | Sales Product Group lifecycle controls |
| Data Protection | `/data-protection` | Supplier / Customer / Outsourced Vendor / Farmer Hard Delete controls |

There are no remaining P7 module-registry skeleton entries after S12.

## 5. Architecture boundaries preserved

P7 does not redefine the established backend architecture:
- .NET 10 / ASP.NET Core 10 / C#
- EF Core 10 + Npgsql
- PostgreSQL 18
- Modular Monolith
- one write `ErpDbContext`
- React + TypeScript + Vite + React Router + TanStack Query
- UUID v7
- explicit `row_version bigint`
- persistent CommandId idempotency for command endpoints
- append-oriented Audit
- transactional Outbox where applicable
- typed real foreign keys
- no Generic Repository / generic CRUD architecture
- no generic lifecycle / correction / Hard Delete server resolver

P7 S0–S12 introduced no schema or migration change. `InitialV01` remains unchanged.

Read-option endpoints added by P7 are purpose-specific application queries. They do not redefine write-side Business Rules and do not replace server-side command validation.

## 6. Business Fact / ERP Control boundary

P7 does not resolve or invent a YowThi Business Rule merely to make a page functional.

Existing backend commands remain authoritative for real Business Facts and ERP Control decisions. The UI may provide convenience validation or an irreversible-operation confirmation guard, but server transaction behavior remains authoritative for:
- concurrency
- persistent idempotency
- lifecycle state
- structural dependency closure
- current-use eligibility
- Hard Delete eligibility

Hard Delete remains highest-authority Data Protection control and remains target-specific.

## 7. Deferred / unresolved scope remains unchanged

P7 system-skeleton closure does not resolve:
- `OUT-003` — unresolved Class A Business Rule gap
- Outsourced Supply Batch Reopen — DEFERRED
- `ProcurementProduct` lifecycle — TO VERIFY / DEFERRED
- `SalesProduct` lifecycle — TO VERIFY / DEFERRED
- `StorageLocation` lifecycle — DEFERRED
- `ProcessMaterial` lifecycle — DEFERRED
- `ProcessingRoute` lifecycle — DEFERRED
- additional Hard Delete targets — DEFERRED / future target-specific scope

Inventory adjustment continues to permit signed quantity delta according to the implemented command contract. P7 does not invent a no-negative-inventory rule.

## 8. Presentation redesign remains open

The system skeleton was intentionally completed before final presentation design.

Current presentation status:
- functional pages exist and can be exercised on the current application shell
- current visual style is **not accepted as final ERP UI**
- broad layout, navigation, information density, visual hierarchy, responsive behavior, and interaction design are still subject to redesign
- Desktop / Tablet / Mobile acceptance remains open
- zh-TW / th-TH switching exists, but full presentation-quality language completeness must be reviewed across the complete application

The next UI phase must follow:
- `docs/16-adaptive-web-ui-architecture-v0.1.md`
- applicable UI ADRs
- the user-confirmed direction to redesign the ERP presentation only after system-skeleton closure

The redesign must preserve the already validated command/API architecture rather than rebuilding backend behavior around the visual design.

## 9. Cost / validation governance

Routine validation remains on the YowThi ERP V2 Windows self-hosted runner only.

Required runner labels:
- `self-hosted`
- `yowthi-erp-v2`

Do not make GitHub-hosted runners, paid/larger runners, Codespaces, or unconfirmed metered GitHub services required development infrastructure.

Formal advancement remains:
1. focused local candidate
2. exact validation branch publication after explicit approval when required
3. exact-SHA self-hosted validation
4. fast-forward-only local `main`
5. non-force push
6. final `main = origin/main` read-back

## 10. Closure result

After this checkpoint is accepted, the formal state is:

```text
P6 / V8                      COMPLETE
P7 system skeleton S0–S12    COMPLETE
12 / 12 web modules          OPERATIONAL
P7 presentation redesign     OPEN
P7 Desktop/Tablet/Mobile UX  OPEN
P7 full zh-TW/th-TH review   OPEN
P8 production hardening      FUTURE
```

This closure freezes the working system skeleton before presentation redesign.

The next implementation focus is the **ERP Desktop / Tablet / Mobile presentation redesign plus application-wide zh-TW / th-TH completeness review**, while preserving all deferred Business Rule and ERP Control boundaries above.
