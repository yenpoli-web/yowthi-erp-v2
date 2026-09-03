# Adaptive Web UI Architecture v0.1 — YowThi ERP V2

Status: **DECISION / v0.1**
Revision basis: Desktop / Tablet / Mobile presentation strategy formally confirmed on 2026-09-03.

## 1. Purpose and precedence

This document defines the frontend presentation architecture for YowThi ERP V2.

It does not redefine:
- Business Facts / Business Rules
- Application Commands
- REST contracts
- persistence
- AuthN/AuthZ

Precedence remains:
- Business Facts / Rules: owning domain documents and Gap Register
- REST behavior: `docs/12-rest-api-architecture-v0.1.md`
- implementation sequencing: `docs/13-implementation-sequencing-build-plan-v0.1.md`
- AuthN/AuthZ: `docs/15-authn-authz-implementation-architecture-v0.1.md`
- presentation architecture: this document and `ADR-006`

When presentation convenience conflicts with a required command fact, authorization gate, concurrency contract, or approved safe handling, the presentation must change.

## 2. Supported web experiences

YowThi ERP V2 formally supports three web presentation experiences:

```text
Desktop
Tablet
Mobile
```

These are three operational UI compositions within one React application.
They are not three separate ERP systems.
They are not three separate repositories.
They do not use separate backend contracts.

Native Windows, iOS, or Android applications are not part of v0.1.

## 3. Architectural model

```text
                 ASP.NET Core / REST API
                          |
                Shared typed API layer
                          |
                  Shared feature core
             /            |            \
       Desktop UI      Tablet UI      Mobile UI
       composition     composition     composition
```

The frontend remains:
- React
- TypeScript
- Vite
- React Router
- TanStack Query
- pnpm

## 4. Shared feature core

The following must remain shared unless a later approved architecture decision explicitly changes ownership:
- API request/response DTOs
- API transport client
- query keys
- TanStack Query query/mutation functions
- cache invalidation
- stable Problem Details code mapping
- locale resolution
- authentication/session integration
- capability/authorization presentation mapping
- antiforgery transport
- Idempotency-Key handling
- expected-row-version / concurrency handling
- command payload construction semantics
- domain-neutral formatting helpers

Do not copy these into Desktop/Tablet/Mobile folders merely to make presentation development easier.

## 5. Presentation ownership

Device-specific presentation may own:
- application shell/navigation
- page composition
- information density
- list/table/card choice
- panel/drawer/full-page choice
- field grouping
- step sequencing in the browser
- action placement
- touch-target sizing
- keyboard/pointer affordances
- local purely-presentational state

Presentation code may reorder how required facts are collected, but the final submitted command must preserve the same semantic contract.

## 6. Experience resolution

Automatic selection is capability-aware.

The baseline resolver considers:
- viewport width
- hover support
- pointer precision / coarse input

Rules:
- narrow viewport resolves Mobile
- wide viewport with desktop-class pointer/hover resolves Desktop
- the middle/capability-touch region resolves Tablet

The exact thresholds are technical UI controls and may be tuned without becoming Business Rules.

Do not use User-Agent parsing as the authoritative classification mechanism.

A touch-capable large screen must not automatically be treated as Desktop merely because its CSS pixel width is large.

## 7. QA / development override

Development and acceptance testing may explicitly force an experience, for example through a controlled client-only mechanism.

Requirements:
- accepted values only: Desktop / Tablet / Mobile
- does not alter API route or payload semantics
- does not alter server-side authorization
- does not persist as a Business Fact
- cannot bypass required command fields

This exists so each experience can be tested deterministically on the development workstation.

## 8. Application shell strategy

The application shell is device-specific.

Desktop baseline:
- high information density
- persistent/high-visibility primary navigation
- multi-column workspace where useful
- keyboard/mouse efficient

Tablet baseline:
- touch-first controls
- larger hit targets
- reduced simultaneous density
- no required hover interaction
- drawers/panels may replace persistent desktop side context

Mobile baseline:
- compact navigation suitable for one-hand/narrow operation
- task-oriented presentation
- one primary action hierarchy at a time
- long multi-column ERP tables replaced by cards, drill-in, or progressive detail where appropriate

These are UX architecture principles, not Business Rules.

## 9. Feature folder boundary

Preferred structure as device-specific complexity grows:

```text
src/
  app/
    device/
    layouts/

  features/
    <feature>/
      api/
      model/
      shared/
      desktop/
      tablet/
      mobile/
```

A feature may remain flatter while it has no device-specific composition need.
Do not create empty Desktop/Tablet/Mobile duplicates only to satisfy folder symmetry.

## 10. Routing boundary

A business route remains stable across presentations.

Example:

```text
/processing/executions/new
```

may render different Desktop/Tablet/Mobile compositions, but all variants operate against the same Processing queries and `ConfirmProcessingExecution` command contract.

Do not create:

```text
/api/v1/mobile/...
/api/v1/tablet/...
/api/v1/desktop/...
```

for UI convenience.

## 11. Business-rule and gap boundary

Device presentation must not create or relax Business Rules.

Examples:
- SALES-001 explicit resolution requirement remains identical on all devices
- PROC-002 receipt-location safe handling remains identical on all devices
- Processing location/mode gaps remain governed by the Gap Register
- Finance concurrency and expected outstanding version remain identical

A Mobile workflow may present a required location selector on a separate step; it may not omit the location requirement.

## 12. Authorization boundary

Different navigation density does not imply different authority.

A hidden button is not an authorization mechanism.
Server-side capability enforcement remains authoritative.

Presentation may suppress unavailable operations when capability information is known, but it cannot grant or infer capability from device type.

## 13. Accessibility and input baseline

All three experiences must:
- preserve semantic labels and accessible names
- support keyboard operation where the browser/platform provides a keyboard
- not require hover to access mandatory controls
- provide touch-appropriate target sizing in Tablet/Mobile compositions
- preserve visible loading/error/disabled states
- preserve locale behavior

## 14. Existing UI migration

Existing Procurement, Outsourced, and Processing pages began as shared responsive compositions.

They are not discarded.
Migration is incremental:

```text
shared responsive page
→ device-aware application shell
→ extract shared feature/API logic
→ introduce device-specific page composition only where operationally valuable
```

Do not perform a high-risk rewrite of all current pages at once.

## 15. P7 implementation sequencing

For each UI vertical slice after backend stability:

1. identify shared feature core and command/query contract
2. define Desktop composition
3. define Tablet composition
4. define Mobile composition
5. verify required command facts are equivalent
6. validate loading / Problem Details / concurrency behavior in all applicable experiences
7. run typecheck/lint/build
8. perform three-experience acceptance for the affected workflow

A simple feature may intentionally share most presentation components if the three experience checks pass.

## 16. Acceptance matrix

Every operational UI slice must be evaluated at least against:

| Concern | Desktop | Tablet | Mobile |
| --- | --- | --- | --- |
| Navigation usable | required | required | required |
| Required command facts accessible | required | required | required |
| Loading/error state | required | required | required |
| Problem Details code behavior | required | required | required |
| Locale behavior | required | required | required |
| Concurrency refresh/retry flow | when applicable | when applicable | when applicable |
| Touch-only operation | not required | required | required |
| Hover-independent mandatory flow | required | required | required |
| Narrow-screen overflow safety | n/a | required | required |

Exact viewport acceptance values belong to test configuration rather than Business Rules.

## 17. Definition of Done — UI slice revision

A UI slice is Done only when:
- backend contract is stable
- shared server-state/query/mutation integration is not duplicated by device
- Desktop/Tablet/Mobile presentation behavior is intentionally defined
- required command facts and semantic validation remain equivalent
- loading/error/validation states are handled
- Problem Details codes drive client flow where appropriate
- locale behavior is handled
- concurrency refresh/retry follows API contract
- mandatory actions do not depend on hover
- applicable touch interactions are usable
- typecheck/lint/build pass

## 18. Non-decisions

This architecture does not define:
- native mobile apps
- offline-first synchronization
- PWA installation requirements
- barcode/camera workflows
- biometric authentication
- per-device Business Rules
- per-device capabilities/permissions
- permanent device identity persistence

Those require real operational requirements before implementation.

## 19. Immediate implementation action

Introduce the device-experience foundation in `YowThi.Erp.Web`:
- capability-aware experience resolver
- Desktop application shell
- Tablet application shell
- Mobile application shell
- shared navigation definition
- deterministic development/QA experience override

Existing feature pages remain functional while presentation-specific refactoring proceeds incrementally.
