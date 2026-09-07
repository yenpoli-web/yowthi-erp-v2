# P7 Presentation Closure Checkpoint v0.1 — YowThi ERP V2

Status: **P7 COMPLETE — SYSTEM SKELETON + NATURE GREEN PRESENTATION + ADAPTIVE UX + zh-TW / th-TH PRESENTATION ACCEPTANCE**

This checkpoint closes the remaining P7 presentation scope that was explicitly left open by `docs/29-p7-system-skeleton-closure-checkpoint-v0.1.md`. The accepted v0.1 presentation baseline is the user-selected **Nature Green** direction, applied across the current Desktop / Tablet / Mobile experiences and all 12 operational ERP modules.

This closure does not add or redefine Business Rules, command semantics, persistence architecture, schema, migrations, or API contracts.

## 1. Closure rule

P7 presentation closure means:
- the P7 system skeleton remains complete with 12 / 12 operational modules
- the accepted visual direction is Nature Green
- Desktop, Tablet, and Mobile shells have explicit adaptive presentation behavior
- the Desktop left navigation uses a Nature Green brand block with a light navigation body rather than a full-height solid-green sidebar
- the home workspace and all current operational/management pages use one coherent presentation baseline
- application-level zh-TW / th-TH ownership is centralized and the current static interface is accepted for both locales
- current browser visual acceptance covers the home workspace plus all 12 operational module routes across Desktop / Tablet / Mobile
- the current validation workflow enforces frontend lint, typecheck, presentation acceptance, production build, and browser visual acceptance on the YowThi ERP V2 self-hosted runner

P7 presentation closure does **not** mean:
- user-entered or master-data names must be in one language only
- future routes/features are implicitly presentation-complete before they receive their own acceptance coverage
- every future lifecycle / Hard Delete / Batch-control candidate is implemented
- any unresolved Business Fact is decided by UI design
- P8 production hardening is complete

## 2. Formal baseline before this checkpoint-document commit

- repository: `yenpoli-web/yowthi-erp-v2`
- local repository: `C:\Dev\yowthi-erp-v2`
- `main = origin/main`
- SHA: `cae5a5c0bf2f1cec7bf0ffa96dde7cbafbbe4ced`
- commit: `feat(web): complete nature green management presentation`
- working tree: clean
- promotion policy: fast-forward only
- push policy: non-force

Exact current presentation candidate validation:
- branch: `p7-management-presentation-validation`
- SHA: `cae5a5c0bf2f1cec7bf0ffa96dde7cbafbbe4ced`
- workflow: `dotnet-self-hosted` / `.github/workflows/dotnet.yml`
- run: `34093318207`
- job: `101651291888` / `build-test`
- runner: `YowThi-ERP-V2`
- labels: `self-hosted`, `yowthi-erp-v2`
- conclusion: `success`
- `eligibleForMainFastForward=true`

The workflow executes:
- .NET SDK verification
- restore
- Release build
- .NET tests
- frontend frozen-lockfile install
- ESLint
- TypeScript typecheck
- presentation acceptance
- Vite production build
- browser visual acceptance

## 3. Presentation implementation chain and exact-SHA validation evidence

Every presentation candidate below was published on a `p*-validation` branch and completed successfully on the configured validation workflow for its exact local branch HEAD.

| Scope | Validation branch | Exact SHA | Run |
|---|---|---|---:|
| Presentation shell localization / redesign foundation | `p7-presentation-shell-redesign-validation` | `7f65e1121c3c6ae5a7460ea4a117759cc0484676` | `34065416502` |
| Nature Green Desktop navigation alignment | `p7-presentation-acceptance-validation` | `daa7414a92b67fc112856a4957a24bc75c0948da` | `34079221024` |
| Nature Green home workspace | `p7-presentation-home-validation` | `05e76c71642639dbce1be28361d01f34bb1528bb` | `34081873041` |
| Tablet / Mobile adaptive Nature Green shell | `p7-presentation-adaptive-validation` | `ddc9017303489af46d95cd6547e05edef61b1743` | `34084681197` |
| Browser visual-acceptance hard gate | `p7-presentation-visual-acceptance-validation` | `1bcc349618b24c1e2f741682bda9d003d8834aae` | `34086266890` |
| Core operation-page presentation | `p7-core-operations-presentation-validation` | `a9d61ebcd4530732a7750370ea6de8420594cb8b` | `34090670470` |
| Secondary operation-page presentation | `p7-operations-secondary-presentation-validation` | `fa80801763e4ed2bca504ac28e3be28046f3f5f6` | `34091509981` |
| Management / Data Protection presentation | `p7-management-presentation-validation` | `cae5a5c0bf2f1cec7bf0ffa96dde7cbafbbe4ced` | `34093318207` |

No presentation candidate is accepted solely from an unvalidated local state.

## 4. Accepted Nature Green presentation baseline

### Desktop

Accepted characteristics:
- Nature Green brand block at the top of the left navigation
- light/white navigation body below the brand block
- green active-state treatment only for the selected navigation item
- module icons and consistent module naming
- home workspace as the root experience
- consistent operation-page title, form, result, field, focus, error, and action hierarchy

The previously rejected full-height solid dark-green sidebar is not the accepted baseline.

### Tablet

Accepted characteristics:
- Nature Green brand/header treatment
- adaptive light module navigation
- responsive operation-page layout without horizontal viewport overflow
- the same locale ownership and module registry as Desktop

### Mobile

Accepted characteristics:
- Nature Green mobile brand/toolbar
- bounded module menu
- four-entry primary bottom navigation for the current mobile primary paths
- single-column operation-page behavior where required
- bounded interactive controls without horizontal viewport overflow

## 5. Current browser visual acceptance — 40 / 40

The browser acceptance uses built production assets, Vite preview, Google Chrome Headless, and Chrome DevTools Protocol metrics. It validates actual rendered DOM/viewport behavior rather than only source-code markers.

Accepted routes:
- Home
- Procurement
- Outsourced Supply
- Processing
- Sales
- Sales Handling
- Labor
- Finance
- Inventory
- Party
- Infrastructure
- Product
- Data Protection

Coverage:

```text
Desktop 1440×900: 13 routes
Tablet  1024×768: 13 routes
Mobile   390×844: 13 routes
Mobile   360×800: 1 narrow-home case
Total:            40 / 40 PASS
```

Current hard checks include:
- expected Desktop / Tablet / Mobile shell identity
- exact viewport dimensions
- no horizontal document overflow
- main workspace remains inside viewport
- visible interactive controls do not escape the viewport
- Desktop sidebar body is not the same solid-green background as the brand block
- Tablet navigation remains in viewport
- Mobile module menu remains in viewport
- Mobile bottom navigation remains in viewport and exposes exactly four entries
- `zh-TW` / `th-TH` locale switching updates document language
- visible static zh-TW interface text contains no Thai script
- visible static th-TH interface text contains no Han script
- field labels are included in static interface language checks

Dynamic business/master data may legitimately contain multilingual names; that is not static-interface language mixing.

## 6. Presentation acceptance baseline

The presentation acceptance hard gate confirms:
- application-shell locale ownership: 3 / 3
- page-level `LocaleControl` instances: 0
- placeholder hints: 0
- raw JSX domain enums: 0
- module registry: 12 operational / 12 localized
- Nature Green Tablet / Mobile shell markers: present
- mobile primary navigation: localized home + 3 core operations
- deterministic Desktop / Tablet / Mobile experience override: present

The selected design direction intentionally avoids adding fabricated dashboard business metrics. Real operational counts/alerts require real Query/Data Source support before presentation.

## 7. i18n closure boundary

For the current P7 route set:
- Traditional Chinese (`zh-TW`) is presentation-accepted
- Thai (`th-TH`) is presentation-accepted
- shell/navigation/module labels are localized
- current static page labels/actions are localized
- the remaining hard-coded `Sales Product Group` presentation label was removed during the management-page closure

This closure applies to the current implemented v0.1 pages. Future pages or new strings must be added to the same acceptance model and must not rely on this checkpoint as automatic evidence.

## 8. Architecture and Business Rule boundaries preserved

Presentation redesign did not redefine:
- .NET 10 / ASP.NET Core 10 architecture
- EF Core 10 / Npgsql persistence
- PostgreSQL 18 schema
- `InitialV01`
- one write `ErpDbContext`
- UUID v7 IDs
- explicit `row_version bigint`
- persistent CommandId idempotency
- transactional Outbox where applicable
- append-oriented Audit
- typed real foreign keys
- target-specific lifecycle / correction / Hard Delete commands

No Generic Repository or generic CRUD architecture was introduced.

Business Rules remain sourced only from real YowThi operating facts. Presentation work does not resolve or invent unresolved Business Facts.

The following remain unchanged/deferred:
- `OUT-003`
- Outsourced Supply Batch Reopen
- `ProcurementProduct` lifecycle — TO VERIFY / DEFERRED
- `SalesProduct` lifecycle — TO VERIFY / DEFERRED
- `StorageLocation` lifecycle — DEFERRED
- `ProcessMaterial` lifecycle — DEFERRED
- `ProcessingRoute` lifecycle — DEFERRED
- additional Hard Delete targets — future target-specific scope

## 9. Validation / cost governance

Routine validation remains on the YowThi ERP V2 Windows self-hosted runner.

Required labels:
- `self-hosted`
- `yowthi-erp-v2`

The frontend presentation hard gates are part of the same self-hosted `build-test` job after .NET build/test. GitHub-hosted runners, paid/larger runners, Codespaces, or unconfirmed metered GitHub services are not required by this closure.

Formal advancement remains:
1. focused validation branch
2. exact-SHA self-hosted validation
3. fast-forward-only local `main`
4. non-force push
5. `main = origin/main` read-back

## 10. Closure result

After this checkpoint is validated and promoted, the formal phase state is:

```text
P6 / V8                         COMPLETE
P7 system skeleton S0–S12       COMPLETE
12 / 12 ERP web modules         OPERATIONAL
P7 Nature Green presentation    COMPLETE
P7 Desktop/Tablet/Mobile UX     COMPLETE (current v0.1 route set)
P7 zh-TW / th-TH presentation   COMPLETE (current v0.1 route set)
P7                               COMPLETE
P8 production hardening         FUTURE
```

P7 is therefore closed as the current v0.1 React UI phase. New UI functionality must preserve this presentation and acceptance baseline unless a later explicitly approved design decision supersedes it.
