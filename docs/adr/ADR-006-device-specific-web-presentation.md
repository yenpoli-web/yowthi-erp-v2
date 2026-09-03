# ADR-006 — Device-Specific Web Presentation

Status: **Accepted / v0.1**

## Context

YowThi ERP V2 is a React web application and must be operable from desktop computers, tablets, and mobile phones.

The initial frontend slices used one shared page composition with CSS media queries to collapse columns and resize controls. That approach is appropriate for simple responsiveness, but ERP operational workflows can differ materially by interaction environment:
- desktop users can work with high information density, multi-column layouts, keyboard/mouse, and persistent side context
- tablet users commonly operate by touch and need larger controls, reduced simultaneous density, and layouts that do not depend on hover
- mobile users need compact navigation and task-oriented/step-oriented presentation rather than a desktop form compressed into a narrow viewport

Creating three independent frontend applications would duplicate API clients, server-state logic, error handling, authorization behavior, locale handling, idempotency behavior, and business-command integration.

## Decision

YowThi ERP V2 uses:

**Single Web Application / Shared Feature Core / Three Adaptive Presentation Layers**.

The three supported presentation experiences are:
- Desktop
- Tablet
- Mobile

They are presentation variants inside the same `YowThi.Erp.Web` React application, not separate ERP applications or repositories.

### Shared layer

All device experiences share the same:
- REST/OpenAPI contracts
- authentication/session boundary
- authorization/capability semantics
- TanStack Query server-state ownership
- query keys and invalidation rules
- API client / DTOs
- Problem Details machine-code handling
- locale semantics
- idempotency behavior
- concurrency behavior
- command payload semantics

Device presentation must never redefine Business Rules or omit required command facts.

### Presentation layer

Desktop, Tablet, and Mobile may independently compose:
- application navigation
- page layout
- information density
- form grouping
- modal/drawer/full-page flow
- list/table/card representation
- action placement
- touch/keyboard interaction affordances

A feature does not need three duplicate components when one component already works correctly across experiences. Device-specific composition is introduced only where interaction needs differ.

### Experience resolution

Automatic experience selection uses viewport and interaction capabilities together.

Do not treat viewport width alone as the complete device model.
Do not use browser User-Agent sniffing as the authoritative ERP architecture boundary.

Examples of relevant browser capabilities include:
- viewport width
- pointer precision
- hover capability
- coarse/touch input

Development/QA may provide an explicit non-business override to force Desktop/Tablet/Mobile presentation for acceptance testing. Such an override does not change server-side behavior or authorization.

### Routing

The same business URL may render a different device presentation.

Do not create device-specific REST endpoints or device-specific Business Commands.
Do not encode device identity into persisted business facts merely for presentation purposes.

## Consequences

- YowThi maintains one frontend dependency graph and one application deployment.
- Desktop, Tablet, and Mobile can have genuinely different operational UX without triplicating business/application integration code.
- UI implementation cost is higher than pure CSS shrinking, but materially lower than three independent applications.
- UI acceptance expands to a three-experience matrix for affected operational flows.
- Existing responsive pages can be migrated incrementally into the new presentation architecture.
- Mobile/tablet interfaces cannot silently simplify away required command fields, concurrency tokens, authorization gates, or safe handling for unresolved Business Rule gaps.
- Native iOS/Android/Windows applications are not introduced by this ADR. A later native-client requirement would need a separate architecture decision.
