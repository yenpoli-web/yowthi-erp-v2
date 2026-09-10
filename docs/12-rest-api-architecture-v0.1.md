# REST/API Architecture v0.1 — YowThi ERP V2

Status: **DECISION / v0.1**
Revision basis: REST/API Architecture Revision 0.2 + Final Architecture Review, formally confirmed.

## 1. Purpose and precedence

This document defines the HTTP/REST contract and ASP.NET Core API boundary for YowThi ERP V2.

Business Facts and Business Rules remain owned by Business Discovery, Command Contracts, the Gap Register, and owning-domain documents.

Implementation precedence:
- Business Facts / Business Rules: Business Discovery, Command Contracts, Gap Register, owning-domain documents
- relational implementation: `docs/10-relational-model-consolidation-v0.1.md`
- EF Core/Npgsql implementation architecture: `docs/11-ef-core-mapping-architecture-v0.1.md`
- HTTP/REST contract: this document

The API is a Domain/Application boundary, not a database browser. It must not expose persistence objects merely because they exist in PostgreSQL.

## 2. Technology baseline

Backend API:
- ASP.NET Core 10 / .NET 10
- Minimal APIs
- `MapGroup` route composition
- `TypedResults`
- first-party ASP.NET Core validation
- `IProblemDetailsService`
- first-party OpenAPI
- REST / JSON

Do not mix Controllers and Minimal APIs in the v0.1 business API without a later architecture decision.

No MediatR dependency is required by the v0.1 architecture.

## 3. API boundary

Conceptual flow:

```text
HTTP
  ↓
Minimal API endpoint
  ↓
transport validation / actor / locale / command metadata
  ↓
Application Command or Query
  ↓
Application handler
  ↓
Domain + persistence/query contracts
  ↓
Infrastructure / ErpDbContext / native SQL where approved
```

Rules:
- endpoints are thin transport adapters
- endpoints do not contain Business Rules
- endpoints do not calculate wages, allocations, payable amounts, or inventory balances
- business-write endpoints do not directly own `ErpDbContext`
- HTTP contracts are separate from Domain entities and EF entities
- Domain/Application layers do not depend on `HttpContext`

## 4. API project structure

Recommended API structure:

```text
YowThi.Erp.Api/
├─ Program.cs
├─ Endpoints/
│  ├─ Party/
│  ├─ Product/
│  ├─ ProcessingConfig/
│  ├─ Procurement/
│  ├─ Processing/
│  ├─ Inventory/
│  ├─ Outsourced/
│  ├─ Sales/
│  ├─ SalesHandling/
│  ├─ Labor/
│  ├─ Finance/
│  ├─ Audit/
│  └─ DataProtection/
├─ Contracts/
│  ├─ Requests/
│  └─ Responses/
├─ Errors/
├─ Idempotency/
├─ Authorization/
└─ Localization/
```

`Program.cs` remains a composition root. Module endpoint registration is separated into small registration extensions.

## 5. Route versioning and module groups

Public v0.1 prefix:

```text
/api/v1
```

Module route groups:

```text
/api/v1/party/...
/api/v1/product/...
/api/v1/processing-config/...
/api/v1/procurement/...
/api/v1/processing/...
/api/v1/inventory/...
/api/v1/outsourced/...
/api/v1/sales/...
/api/v1/sales-handling/...
/api/v1/labor/...
/api/v1/finance/...
/api/v1/audit/...
/api/v1/data-protection/...
```

API version semantics:
- additive compatible changes remain under `/api/v1`
- breaking HTTP-contract changes require a new major API version such as `/api/v2`
- database schema version, Domain design version, and API version are separate concepts

## 6. Business-intent command endpoints

Commands express business intent, not generic CRUD.

Confirmed examples:

| Application Command | HTTP endpoint |
|---|---|
| `ConfirmProcurementEntry` | `POST /api/v1/procurement/entries` |
| `ConfirmProcessingExecution` | `POST /api/v1/processing/executions` |
| `ConfirmOutsourcedSupplyDetail` | `POST /api/v1/outsourced/supply-details` |
| `ConfirmSales` | `POST /api/v1/sales/{salesId}/confirm` |
| `RecordSalesPackagingWork` | `POST /api/v1/sales-handling/work-records` |
| `ConfirmEmployeeDailyWage` | `POST /api/v1/labor/daily-wages` |
| `AddPayableAdjustment` | `POST /api/v1/finance/payables/{payableId}/adjustments` |
| `PayPayable` | `POST /api/v1/finance/payables/{payableId}/payments` |
| `ReceiveReceivable` | `POST /api/v1/finance/receivables/{receivableId}/receipts` |
| `CorrectPaymentAmount` | `POST /api/v1/finance/payables/{payableId}/payments/{paymentId}/correct-amount` |
| `CorrectReceiptAmount` | `POST /api/v1/finance/receivables/{receivableId}/receipts/{receiptId}/correct-amount` |
| `CorrectPayableAdjustment` | `POST /api/v1/finance/payables/{payableId}/adjustments/{adjustmentId}/correct` |
| `TransferInventory` | `POST /api/v1/inventory/transfers` |
| `AdjustInventory` | `POST /api/v1/inventory/adjustments` |
| `CloseProcurementBatch` | `POST /api/v1/procurement/batches/{batchId}/close` |
| `CloseOutsourcedSupplyBatch` | `POST /api/v1/outsourced/batches/{batchId}/close` |

Do not introduce:

```text
POST /api/v1/commands
POST /api/v1/entities/{type}/{id}/update
DELETE /api/v1/entities/{type}/{id}
```

No generic command endpoint or unconstrained entity mutation endpoint exists in v0.1.

## 7. HTTP method semantics

Use HTTP methods according to domain intent.

Fact creation:

```text
POST /procurement/entries
POST /processing/executions
POST /finance/payables/{id}/payments
```

State transition:

```text
POST /sales/{id}/confirm
POST /procurement/batches/{id}/close
POST /party/suppliers/{id}/restore
```

Correction resource:

```text
POST /sales/{id}/allocation-revisions
```

Editable master/draft operations may use ordinary `POST`, `PUT`, or a deliberately typed `PATCH` contract where the owning domain explicitly allows direct amendment.

Do not use generic JSON Patch as a universal mutation mechanism.

HTTP `DELETE` may be used only for a real ephemeral/draft-owned removal where owning-domain rules explicitly permit ordinary deletion. It must not ambiguously mean Soft Delete, Hard Delete, or confirmed-transaction correction.

## 8. Synchronous command completion

Core ERP write commands are synchronous atomic commands.

A successful command response means the core PostgreSQL business transaction committed.

Do not return `202 Accepted` merely because the transaction also writes Outbox messages. External side effects are post-commit concerns.

Typical successful statuses:
- `200 OK` for a successful state transition/result
- `201 Created` when a new formal resource/fact is created
- `204 No Content` when success intentionally has no response body

## 9. API DTO separation

The following must remain separate:

```text
HTTP Request DTO
≠ Application Command
≠ Domain Entity
≠ EF Entity
```

Endpoints compose Application Commands from:
- route values
- headers
- request body
- authenticated actor context
- server-resolved metadata

Application code does not accept `HttpContext` as a command model.

Read endpoints return dedicated response/projection DTOs rather than serialized Domain/EF entities.

## 10. JSON contract

v0.1 JSON conventions:
- `application/json`
- camelCase property names
- UUID values use textual UUID representation
- business dates use `YYYY-MM-DD`
- timestamps use UTC ISO-8601/RFC3339-compatible representation
- enum/domain code values are strings
- decimal business quantities/rates/prices are JSON numbers
- THB `bigint` values are represented as numeric contract values subject to frontend precision validation during implementation

Write DTOs must reject unknown/unsupported properties rather than silently ignoring fields the client believed were accepted.

## 11. Business-write idempotency header

Every persisted Business Write Command requires:

```http
Idempotency-Key: <uuid>
```

This includes:
- confirmed business commands
- master/draft mutations
- Soft Delete
- Restore
- Hard Delete
- correction commands

GET/read-only queries do not require an idempotency key.

The HTTP Idempotency Key maps to the persistent Command Identity represented by `system.command_executions.command_id`.

Do not duplicate the same command identity in ordinary write bodies unless a specific external integration contract later requires it.

## 12. Canonical command identity

The canonical command request hash is computed after transport binding has produced the semantic Application Command request.

Canonical identity includes:
- Command Type
- route-derived business IDs
- semantic request-body fields
- expected concurrency tokens supplied by the client

Canonical identity excludes transport/non-semantic metadata such as:
- `Idempotency-Key` itself
- Authorization credentials
- `Accept-Language`
- trace headers
- `User-Agent`
- request receive timestamp

Canonical serialization/hashing must be deterministic and version-controlled. The stored request hash remains SHA-256 per the System persistence baseline.

## 13. Idempotency ordering

Idempotency replay detection must occur before current aggregate/state/concurrency validation.

Correct high-level sequence:

```text
authenticate actor
→ validate Idempotency-Key format
→ construct canonical Application Command request
→ acquire/check CommandId
→ if committed replay: return stored result
→ otherwise load current aggregate/state
→ validate expected versions / Business Rules
→ execute and commit
```

This ordering is mandatory.

Example:
- first `ConfirmSales` succeeds with expected row version 8 and commits Sales row version 9
- same Idempotency Key is retried
- the retry must return the original committed result
- it must not revalidate version 8 against the current version 9 and return a false stale-version conflict

Hard Delete replay follows the same rule: the target row may already be physically gone, but the original success must still replay before target lookup produces `404`.

## 14. Same-key replay rules

A duplicate CommandId is a valid replay only when all of the following match:
- same authenticated actor/account identity
- same Command Type
- same canonical request hash

If they match and the original command succeeded:
- return the stored committed Application result
- do not execute the Business Command again

If actor, Command Type, or request hash differs:

```text
409 Conflict
code = idempotency.key-reused
```

If the client changes an expected row version or other semantic field and intends a new attempt, it must use a new Idempotency Key.

Idempotency retention duration is an Operations/Deployment configuration that must be explicitly set before production. The API must not claim infinite replay guarantees beyond the configured retention policy.

## 15. Persisted command result must be locale-neutral

`system.command_executions.result_payload` stores a locale-neutral Application Command result.

Appropriate result facts include:
- created IDs
- resulting row versions
- monetary totals
- resulting state codes
- other stable command result data

Do not persist locale-dependent labels or human-readable localized error/success text as command truth.

`Accept-Language` does not participate in idempotency identity. Localized display text, when needed, is produced by the HTTP/query presentation layer.

## 16. Explicit row-version HTTP contract

YowThi uses explicit `row_version bigint` persistence concurrency and exposes the relevant expected version directly in mutation contracts.

Typical response:

```json
{
  "id": "...",
  "rowVersion": 7
}
```

Typical mutation request:

```json
{
  "expectedRowVersion": 7
}
```

Do not use HTTP ETag/`If-Match` as the v0.1 ERP concurrency contract.

Reason:
- the business concurrency owner may not be identical to the HTTP route resource representation
- Finance Outstanding and Inventory concurrency can use separate internal/secondary concurrency boundaries
- the explicit token matches the established relational architecture

## 17. Versioned mutation behavior

If a command requires a client-visible row-version precondition and it is missing or syntactically invalid:

```text
400 Bad Request
code = request.validation-failed
```

If the expected version is stale:

```text
409 Conflict
code = concurrency.stale-row-version
```

A conflict response may include the current row version where this is safe and useful, but it must not automatically serialize the entire current entity as a hidden refresh API.

## 18. Internal concurrency remains internal

Not every persistence row version is a client API token.

Example: `ConfirmSales`
- client supplies the expected Sales row version
- command internally loads/resolves Inventory Positions
- Inventory conflict is handled by the command transaction

If internal Inventory concurrency loses:

```text
409 Conflict
code = inventory.concurrent-change
```

The client does not receive or manage all Inventory Position row versions.

## 19. Finance concurrency contract

`PayPayable` and `ReceiveReceivable` use the applicable Outstanding Position version as the monetary concurrency boundary.

Example payment request:

```json
{
  "amountThb": 5000,
  "expectedOutstandingVersion": 12
}
```

If amount is omitted because the UI requests the current full Outstanding, the expected Outstanding version still participates in the command identity and atomic compare-and-set operation.

If Outstanding changed:

```text
409 Conflict
code = finance.outstanding-changed
```

The API does not expose direct mutation of Outstanding projection rows.

`CorrectPaymentAmount` uses the same applicable Payable Outstanding Position version as its monetary concurrency boundary. The correction command owns the transactional delta update of Payment + Outstanding; clients never write the projection row directly.

## 20. Validation layers

### Transport/API validation

API-layer validation covers request representation concerns such as:
- required fields
- UUID parsing
- date parsing
- enum/code representation
- basic type/range requirements that are part of the HTTP contract
- unknown properties

Transport/request contract failure returns `400 Bad Request`.

### Application/Domain validation

Business Rule and command-semantic validation belongs to Application/Domain.

Examples:
- Sales allocation total does not equal required Sales Detail quantity
- current lifecycle state blocks operation
- Processing Route/Version is incompatible
- dependency assessment blocks correction

Do not encode Business Rules into Minimal API validation attributes merely for convenience.

## 21. Problem Details

All expected HTTP errors use a consistent Problem Details representation.

Canonical shape:

```json
{
  "type": "urn:yowthi:error:finance.outstanding-changed",
  "title": "Outstanding has changed",
  "status": 409,
  "detail": "...",
  "instance": "/api/v1/finance/payables/...",
  "code": "finance.outstanding-changed",
  "traceId": "..."
}
```

Optional field-level validation information may be included through an `errors` extension.

Client flow control must depend on:
- HTTP status
- stable `code`

Clients must not parse:
- `title`
- `detail`
- raw PostgreSQL messages
- constraint names
- stack traces

## 22. Error-code vocabulary

Stable code format:

```text
<module>.<reason>
```

Representative codes:

```text
request.validation-failed
request.rate-limit-exceeded
resource.not-found
idempotency.key-missing
idempotency.key-invalid
idempotency.key-reused
concurrency.stale-row-version
sales.invalid-state
sales.allocation-invalid
inventory.insufficient-stock
inventory.concurrent-change
finance.outstanding-changed
finance.payment-exceeds-outstanding
finance.receipt-exceeds-outstanding
```

Business Rule Gap decisions may introduce future stable error codes, but error vocabulary must not silently invent unresolved Business Rules.

## 23. HTTP status taxonomy

| HTTP | Responsibility |
|---:|---|
| `200` | successful query/state transition/result |
| `201` | successful creation of a formal resource/fact |
| `204` | successful command intentionally returning no body |
| `400` | HTTP/JSON/request-contract validation failure |
| `401` | unauthenticated |
| `403` | authenticated but unauthorized |
| `404` | addressed resource not found in the applicable API view |
| `409` | request conflicts with current server/business/concurrency/idempotency state |
| `422` | structurally valid request whose semantic command input is invalid independent of a current-state race |
| `429` | rate limit exceeded |
| `500` | unexpected server failure |

Examples:
- malformed UUID -> `400`
- manual allocation sum is semantically invalid -> `422`
- Sales is already in a conflicting current state -> `409`
- stale row version -> `409`
- duplicate Idempotency Key with different command -> `409`

## 24. Database exceptions are safety nets

Expected Business Rule failures are validated before relying on PostgreSQL errors.

Database constraints remain structural/concurrency safety nets.

Known race-driven unique/concurrency violations may be mapped to a stable expected `409` where the mapping is explicit.

Unknown integrity failures are unexpected server errors:
- log technical details server-side
- return generic `500` Problem Details
- never expose SQL, connection strings, stack traces, or raw constraint details to the client

## 25. Query contracts

Read APIs return dedicated response projections.

Example:

```text
GET /api/v1/finance/payables/{id}
→ PayableDetailResponse
```

Do not return EF/Domain entity graphs merely because navigations exist.

Query implementations may use:
- EF `AsNoTracking` projection
- Dapper
- approved native SQL

according to the EF Core Mapping Architecture.

## 26. Pagination

Do not introduce OData or a generic database-expression language in v0.1.

Default list pagination uses:

```text
?limit=50&cursor=...
```

Response shape:

```json
{
  "items": [],
  "nextCursor": "..."
}
```

Rules:
- cursor is opaque to clients
- ordering is stable and server-defined
- server defines a default page size
- server defines a finite technical maximum
- `totalCount` is optional and not guaranteed on every list endpoint

Exact technical limits are implementation/operational controls, not Business Rules.

## 27. Filtering and sorting

Every query endpoint defines its own allowlisted filter/sort contract.

Example:

```text
GET /api/v1/sales?fromDate=...&toDate=...&customerId=...&status=CONFIRMED
```

Do not expose:
- arbitrary SQL-like filter strings
- arbitrary database column sorting
- unrestricted expression trees from clients

Sort keys must be stable public API concepts, not accidental column names.

## 28. Localization contract

Supported operational UI languages in the current baseline:
- `zh-TW`
- `th-TH`

Request language preference uses `Accept-Language`.

For operational/list projections, a DTO may expose a resolved `displayName`.

Resolution:
1. choose the first supported requested language
2. use that localized name when present
3. if missing, fall back to the other existing localized name
4. if no supported request language exists, use deployment-configured default locale

Do not hard-code a new permanent business rule that `zh-TW` must always be the default locale.

## 29. Bilingual master DTOs

Master/edit detail APIs expose both persisted names where editing requires them:

```json
{
  "id": "...",
  "nameZhTw": "...",
  "nameThTh": "...",
  "rowVersion": 5
}
```

Mutation request may include:

```json
{
  "nameZhTw": "...",
  "nameThTh": "...",
  "expectedRowVersion": 5
}
```

The existing invariant that at least one localized name must exist remains a Domain/persistence rule.

Problem Details `title`/`detail` may be localized; `type`, `code`, and status remain machine-stable.

## 30. Soft Delete / Restore endpoints

Soft Delete and Restore are explicit lifecycle commands rather than overloaded generic DELETE behavior.

Examples:

```text
POST /api/v1/party/suppliers/{id}/soft-delete
POST /api/v1/party/suppliers/{id}/restore
```

For a versioned target, request includes the applicable expected row version.

Normal current-use query endpoints may return `404` for soft-deleted resources. Historical, correction, audit, and Data Protection query paths may intentionally include deleted references where authorized.

This API behavior does not require a global EF query filter.

## 31. Hard Delete endpoints

Hard Delete remains an explicit Data Protection operation.

Use target-specific routes only for supported targets, for example:

```text
POST /api/v1/data-protection/suppliers/{id}/hard-delete
POST /api/v1/data-protection/customers/{id}/hard-delete
```

Do not create:

```text
/data-protection/entities/{entityType}/{id}
```

Such a route would recreate a generic `(type,id)` resolver that the architecture intentionally rejects.

Hard Delete requires:
- authenticated actor
- highest-authority capability boundary
- Idempotency Key
- owning-domain dependency assessment
- explicit physical deletion
- same-transaction Hard Delete audit

Expected row version is required only where the target owns an applicable concurrency token.

Hard Delete replay must be resolved from idempotency before target lookup, because the original success may already have removed the row.

## 32. Correction endpoints

Confirmed transaction correction is owning-domain behavior.

Do not create a generic correction endpoint such as:

```text
POST /api/v1/corrections
{
  entityType,
  entityId,
  patch
}
```

Example domain-owned correction:

```text
POST /api/v1/sales/{salesId}/allocation-revisions
```

This aligns with immutable allocation revision history plus compensating Inventory Movements.

V8-C6 implements the target-specific Payment amount correction endpoint:

```text
POST /api/v1/finance/payables/{payableId}/payments/{paymentId}/correct-amount
```

This is an ERP Control direct amendment with correction Audit, idempotency, and Outstanding concurrency/rebuild. It is not a fabricated reversal Business Fact.

V8-C7 also implements the target-specific Receipt amount correction endpoint:

```text
POST /api/v1/finance/receivables/{receivableId}/receipts/{receiptId}/correct-amount
```

It uses the same `finance.correct` capability and the Receivable Outstanding Position version as its monetary concurrency boundary.

V8-C8 also implements the target-specific Payable Adjustment correction endpoint:

```text
POST /api/v1/finance/payables/{payableId}/adjustments/{adjustmentId}/correct
```

It uses the same `finance.correct` capability and the Payable Outstanding Position version as its concurrency boundary. The request supplies the complete corrected Adjustment amount delta and reason state; original adjustment type and recorded metadata remain unchanged.

Payment, Receipt, and Payable Adjustment registration corrections are now all target-specific ERP Control operations. Do not expose a generic Payment/Receipt/Adjustment correction or reversal endpoint.

## 33. Authentication baseline

All ERP business endpoints under `/api/v1` require authenticated identity by default.

Conceptual composition:

```csharp
var api = app.MapGroup("/api/v1")
    .RequireAuthorization();
```

Anonymous endpoints must be explicit and narrowly scoped, such as minimum health probes where deployment requires them.

Tailscale or trusted network location does not replace ERP authentication.

Development-only no-password bootstrap access must not exist in staging/production.

## 34. Actor identity

Business write actor is resolved server-side from the authenticated principal and mapped to the persistent `system.accounts` identity anchor.

Do not accept client-supplied actor IDs as authority:

```json
{
  "actorAccountId": "..."
}
```

must not determine the persisted operator.

The API/application layer must reject an authenticated principal that cannot be resolved to an allowed active actor according to the future AuthN/AuthZ implementation design.

## 35. Authorization policy boundary

Use operation/capability-style authorization policies such as:

```text
sales.confirm
finance.pay
finance.correct
inventory.adjust
data-protection.hard-delete
```

This document does not define:
- Role Master
- Permission persistence
- account-role tables
- JWT vs Cookie authentication
- external identity provider model

Do not hard-code unconfirmed role names such as `Admin`, `Manager`, or `SuperAdmin` as Business Facts.

Which identities receive capabilities belongs to the AuthN/AuthZ implementation architecture.

## 36. Endpoint composition rule

Business endpoints should perform only transport responsibilities:
- bind route/header/body values
- perform transport-level validation
- resolve locale/actor/CommandId metadata
- map request to Application Command/Query
- invoke Application
- map Application result to HTTP result

Endpoint Filters may implement cross-cutting HTTP concerns only. They must not own Business Rule validation or PostgreSQL transactions.

## 37. Transaction ownership

HTTP endpoints do not call `BeginTransaction()`.

Persisted Business Command flow:

```text
endpoint
→ Application Command Executor
→ fresh scoped ErpDbContext
→ CommandId acquire/replay
→ explicit PostgreSQL transaction for new command
→ owning-domain read/revalidation
→ business + cross-module facts
→ audit + outbox
→ CommandExecution SUCCEEDED
→ COMMIT
→ HTTP result
```

The persisted transaction policy remains defined by the EF Core Mapping Architecture.

## 38. Cancellation

`HttpContext.RequestAborted` is propagated into Application/persistence work where appropriate.

A client disconnect is not proof of transaction rollback.

Before a successful COMMIT, cancellation/errors normally cause the transaction to roll back.

If the COMMIT result becomes unknown because the connection/client fails at the commit boundary:
- do not guess whether commit succeeded
- do not retry using the same tracked `ErpDbContext`
- dispose the context
- replay the whole command with the same Idempotency Key using a fresh context

If the original transaction committed, persistent CommandExecution replay returns the committed result.
If it did not commit, no durable CommandExecution exists and the new attempt may execute.

## 39. External side effects

External work must not occur before the core transaction commits.

Pattern:

```text
business transaction
→ write Outbox
→ COMMIT
→ Outbox dispatcher
→ external side effect
```

This maintains atomic Business Fact persistence while permitting at-least-once external delivery.

The synchronous command response reports the core business transaction result, not completion of all eventual external integrations.

## 40. OpenAPI

Use ASP.NET Core first-party OpenAPI generation.

Business endpoints should provide explicit stable endpoint names/OperationIds.

OperationId stability is part of the client contract after frontend/generated clients begin depending on it.

OpenAPI describes:
- API DTOs
- routes and HTTP methods
- Idempotency-Key requirements
- concurrency request fields
- success responses
- Problem Details/error responses
- authorization metadata where appropriate

OpenAPI does not describe EF entities as the public model.

Production does not expose OpenAPI publicly by default. Development may expose a generated document such as `/openapi/v1.json`.

## 41. OpenAPI and endpoint contract tests

Two test layers are required during implementation.

### Endpoint metadata tests

Verify:
- route
- HTTP method
- authorization requirement/policy
- stable OperationId
- request/response type
- documented status codes
- Business Writes require Idempotency Key
- applicable concurrency input is represented

### OpenAPI semantic tests

Parse the generated OpenAPI document and verify required operations, schemas, headers, status responses, and Problem Details contracts.

Do not rely on fragile whole-file snapshots that fail only because JSON property ordering changed.

OpenAPI remains a generated contract artifact. It does not override Business Facts or Application Command Contracts.

## 42. Production HTTPS baseline

Production Business API must not accept plaintext HTTP business traffic.

Preferred deployment patterns:
- HTTPS terminates at a trusted reverse proxy/ingress which forwards safely to Kestrel
- or Kestrel directly serves HTTPS-only traffic

Do not rely on HTTP-to-HTTPS redirect as the primary protection for sensitive business command requests, because a client may already have sent the body to an HTTP endpoint.

The exact TLS termination topology is deployment-specific.

## 43. Forwarded headers / trusted proxies

When behind a reverse proxy, forwarded client/scheme/host headers are trusted only from explicitly configured trusted proxy addresses/networks.

Relevant forwarded metadata may include:
- `X-Forwarded-For`
- `X-Forwarded-Proto`
- `X-Forwarded-Host`

Do not trust arbitrary Internet-supplied forwarding headers.

Forwarded-header processing must occur before middleware that depends on client address or request scheme.

## 44. CORS

CORS is disabled unless deployment topology requires cross-origin browser access.

If React UI and API are same-origin, no CORS policy is required.

If they are different origins:
- use an explicit deployment-configured origin allowlist
- allow only required methods/headers
- credentials policy depends on the future authentication mechanism

Do not use wildcard origin together with credentialed requests.

## 45. Rate limiting

Production API enables ASP.NET Core rate-limiting infrastructure.

Policies may partition by concepts such as:
- authenticated account
- endpoint/cost category

Exact numeric thresholds are deployment controls and must be selected through implementation/load testing, not invented as Business Rules.

Rejected requests return:

```text
429 Too Many Requests
code = request.rate-limit-exceeded
```

`Retry-After` should be provided when meaningful for the selected limiter.

## 46. Request-size control

Production configures a finite JSON request-body limit.

Endpoints requiring larger payloads must receive deliberate endpoint-specific configuration rather than inheriting an unlimited global setting.

The exact size limit is determined during implementation/operational profiling.

## 47. Production error/logging policy

Production must not expose:
- Developer Exception Page
- SQL exception text
- stack traces
- connection strings
- internal topology
- sensitive database constraint details

Unexpected `500` response returns a generic Problem Details payload with a trace/correlation identifier.

Server logs retain technical diagnostics according to access/retention policy.

Do not log Authorization credentials.

Generic request/response body logging is OFF by default in production because ERP requests/responses may contain bank, phone, address, financial, and other sensitive operational data.

## 48. Health and diagnostic exposure

Anonymous health endpoints, when deployment requires them, expose only minimum liveness/readiness state.

They must not expose:
- database connection strings
- detailed exceptions
- internal service topology
- credentials
- sensitive environment settings

OpenAPI, internal diagnostics, and audit-investigation endpoints are not publicly enabled in production merely for developer convenience.

## 49. Conceptual middleware ordering

Production middleware conceptually follows:

```text
Forwarded Headers
→ exception / ProblemDetails boundary
→ HTTPS/security boundary
→ CORS if explicitly configured
→ Rate Limiting
→ Authentication
→ Authorization
→ API endpoints
```

Exact ordering may be adjusted for the final hosting topology, but:
- forwarded headers must be validated before scheme/client dependent behavior
- Business endpoints must complete authentication/authorization before handlers
- untrusted forwarded headers must not influence authority or security decisions

## 50. Example — Confirm Sales

```http
POST /api/v1/sales/{salesId}/confirm
Idempotency-Key: <uuid>
Accept-Language: zh-TW
Content-Type: application/json
```

Body:

```json
{
  "expectedRowVersion": 8,
  "manualAllocationOverrides": []
}
```

Conceptual execution:

```text
transport validation
→ resolve actor
→ canonical ConfirmSales command
→ CommandId acquire/replay
→ load Sales/current inventory for new command
→ validate expected Sales version
→ allocate inventory
→ apply internal Inventory concurrency control
→ Allocation Revision 0 + current pointer projection
→ SALES_ISSUE
→ Receivable
→ Audit + Outbox
→ mark CommandExecution SUCCEEDED
→ commit
```

Success result may return:

```json
{
  "salesId": "...",
  "rowVersion": 9,
  "receivableId": "...",
  "allocationRevisionId": "..."
}
```

Same Idempotency Key replay returns the committed result without failing because Sales is now version 9.

## 51. Example — Pay Payable

```http
POST /api/v1/finance/payables/{payableId}/payments
Idempotency-Key: <uuid>
Content-Type: application/json
```

Body:

```json
{
  "amountThb": 5000,
  "expectedOutstandingVersion": 12
}
```

Success may return `201 Created`:

```json
{
  "paymentId": "...",
  "payableId": "...",
  "amountThb": 5000,
  "outstandingThb": 12500,
  "outstandingVersion": 13,
  "confirmedAt": "..."
}
```

Outstanding race:

```text
409 finance.outstanding-changed
```

Safe v0.1 FIN-001 block:

```text
409 finance.payment-exceeds-outstanding
```

The final Business Rule remains governed by the Gap Register.

## 52. Example — Confirm Procurement Entry

```http
POST /api/v1/procurement/entries
Idempotency-Key: <uuid>
Content-Type: application/json
```

Body concept:

```json
{
  "procurementDate": "2026-08-27",
  "procurementProductId": "...",
  "sourceType": "SUPPLIER",
  "supplierId": "...",
  "netQuantity": 125.5,
  "unitPrice": 18.25,
  "companyPickup": true
}
```

The 2026-09-10 Procurement receipt-destination contract makes receipt location server-owned Batch/header state:
- `ConfirmProcurementEntryRequest` does not expose `receiptStorageLocationId`
- a new date+product Procurement Batch captures the Procurement Product current valid default Storage Location
- later Entries reuse the captured Batch destination even if the Product default later changes
- the Inventory receipt ledger still records the concrete Storage Location
- an older Batch with multiple historical receipt locations is ambiguous and is not silently repaired

The Procurement UI may query `GET /api/v1/procurement/receipt-destination?procurementDate=...&procurementProductId=...` to present the resolved Warehouse at header level. This read endpoint does not create a Batch and does not require an Idempotency Key.

The client does not supply server-owned derived/created facts such as:
- Procurement amount THB
- resolved Procurement Batch ID when the command owns resolution
- Payable ID
- Inventory Movement ID

Those facts are created/resolved atomically by the command.

## 53. API testing acceptance baseline

Before an endpoint slice is considered implemented, tests must cover the applicable items:
- route/method/OperationId
- authentication default
- capability authorization requirement
- request parsing/unknown-property behavior
- Idempotency-Key required on Business Writes
- canonical hash includes route IDs and semantic concurrency tokens
- same-key same-command replay
- same-key different actor/type/hash conflict
- replay occurs before stale-state lookup
- expected row-version conflict mapping
- Finance Outstanding conflict mapping
- `400/409/422` taxonomy
- Problem Details stable code
- locale fallback behavior
- Soft Delete/Restore semantics
- Hard Delete replay after physical deletion
- correction endpoints only where owning command exists
- no sensitive technical detail in `500`
- OpenAPI semantic contract
- Procurement Batch receipt-destination capture/reuse/legacy-ambiguity behavior and header Warehouse query

Production integration testing later adds HTTPS/proxy/CORS/rate-limit/security-hosting checks according to deployment topology.

## 54. No new Business Rule gaps

REST/API Architecture v0.1 introduces no new YowThi Business Rules.

Technical controls such as:
- authentication required by default
- HTTPS-only production business traffic
- rate limiting
- finite request-size limits
- proxy trust configuration
- idempotency retention configuration

are ERP Control Governance / operational controls, not assertions about YowThi business operations.

Procurement `receiptStorageLocationId` is no longer a detail/write input. The server-owned Batch receipt destination and read-only header Warehouse projection implement the confirmed 2026-09-10 PROC-002 resolution.

Existing unresolved Business Rule gaps remain governed by `docs/06-business-rule-gap-register-v0.1.md`.

## 55. Architecture chain status

The v0.1 design chain now covers:
- Business Discovery / Ubiquitous Language
- Domain Map / Domain Model
- Application Command Contracts
- Business Rule Gap Register
- Persistence Architecture
- PostgreSQL Schema Parts 1–6
- Relational Model Consolidation
- EF Core Mapping Architecture
- REST/API Architecture

Implementation sequencing is defined separately in `docs/13-implementation-sequencing-build-plan-v0.1.md`.

## 56. Next step

Continue with the confirmed implementation sequence in:

**`docs/13-implementation-sequencing-build-plan-v0.1.md`**

Immediate next action:

**Implementation P0 — scaffold the .NET solution.**

Docker Desktop may remain stopped during P0/P1/P2/P3/P4 work that does not execute PostgreSQL.
It becomes required when the approved `InitialV01` is first applied to PostgreSQL 18 and integration/concurrency testing begins.
