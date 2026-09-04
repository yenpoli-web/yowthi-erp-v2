# AuthN/AuthZ Implementation Architecture v0.1 — YowThi ERP V2

Status: **DECISION / v0.1**
Revision basis: P3.5 AuthN/AuthZ Candidate Architecture, formally confirmed on 2026-08-28.

## 1. Purpose and precedence

This document completes the P3.5 AuthN/AuthZ implementation architecture hard gate required before `InitialV01`.

It defines technical/security architecture only. It does not create a YowThi Business Rule, role hierarchy, employee authority model, or business approval rule.

Implementation precedence remains:
- Business Facts / Business Rules: Business Discovery, Command Contracts, Gap Register, owning-domain documents
- ERP Registration / Data Control classification: `docs/17-erp-registration-data-control-boundary-v0.1.md`
- relational baseline: `docs/10-relational-model-consolidation-v0.1.md`
- EF Core/Npgsql architecture: `docs/11-ef-core-mapping-architecture-v0.1.md`
- HTTP/REST architecture: `docs/12-rest-api-architecture-v0.1.md`
- implementation sequencing: `docs/13-implementation-sequencing-build-plan-v0.1.md`
- AuthN/AuthZ implementation/security architecture and the auth-specific `system.accounts` correction: this document

Auth-specific relational correction:
- this document is the later confirmed correction to earlier statements in `docs/10` section 6 and `docs/08-postgresql-schema-v0.1/06-audit-system.md` section 2 that external identity mapping was not yet defined
- only the confirmed `system.accounts.identity_issuer` / `identity_subject` addition is superseding those earlier statements
- all other relational rules in `docs/10` remain authoritative
- relation count remains 55 and schema count remains 14

## 2. Security goals

The v0.1 authentication/authorization design must:
- require authenticated identity for ERP business and control endpoints
- keep actor identity server-controlled
- resolve the authenticated principal to persistent `system.accounts`
- avoid ERP-owned password storage
- avoid browser access/refresh-token storage
- use operation/capability authorization rather than inventing business roles
- allow an account to be disabled centrally and rejected on subsequent requests
- keep test authentication isolated from staging/production
- preserve the existing no-password-bootstrap prohibition for deployed ERP APIs
- not treat Tailscale or network location as application authentication

## 3. Authentication mechanism

v0.1 uses a provider-neutral external OpenID Connect Identity Provider.

Interactive browser authentication:
- OpenID Connect Authorization Code flow
- PKCE
- confidential server-side client where the provider supports/requires it
- ASP.NET Core authentication middleware owns the protocol exchange

The specific Identity Provider is deployment-configured.
The ERP architecture does not hard-code Microsoft, Google, or another provider as a domain fact.

Provider configuration includes trusted values such as:
- authority / issuer
- client id
- client secret where applicable
- callback path
- allowed redirect/logout destinations

Secrets must come from deployment secret configuration and must not be committed to the repository.

## 4. Browser session model

After successful OIDC authentication, the ERP uses an ASP.NET Core encrypted authentication cookie as the browser application session.

Cookie baseline:
- `HttpOnly`
- `Secure`
- appropriate `SameSite` mode for the confirmed deployment topology
- finite configured lifetime
- no unbounded persistent login

Exact session lifetime is an Operations/Deployment security setting, not a Business Rule.

The React application does not become the owner of authentication tokens.

## 5. API redirect behavior

`/api/v1` is an API boundary.

Unauthenticated API requests return `401 Unauthorized`; the API must not transform arbitrary business/control requests into an HTML/OIDC redirect response.

Interactive login is initiated explicitly through an authentication route such as:

```text
GET /auth/login
```

Logout is explicit, for example:

```text
POST /auth/logout
```

The OIDC callback route is protocol infrastructure and is not a business endpoint.

## 6. External identity key

The stable external identity key is the validated OIDC pair:

```text
(issuer, subject)
```

The ERP must not use the following as the persistent authentication key:
- display name
- email address
- phone number
- Employee identity
- mutable IdP profile labels

The issuer and subject used for lookup come only from a successfully validated authentication ticket/token produced by the configured OIDC middleware.

## 7. `system.accounts` relational revision

The existing relation remains the persistent actor identity anchor.

Existing fields remain:
- `id uuid` PK
- `display_name text NOT NULL`
- `active boolean NOT NULL`
- `row_version bigint NOT NULL DEFAULT 1`
- `created_at timestamptz NOT NULL`

Add:
- `identity_issuer text NULL`
- `identity_subject text NULL`

Pair rule:
- both fields are NULL, or both fields are present

When present:
- issuer must be nonblank
- subject must be nonblank

External identity uniqueness:

```text
UNIQUE (identity_issuer, identity_subject)
WHERE identity_issuer IS NOT NULL
```

No database-generated UUID is introduced; account IDs remain application/maintenance generated according to the existing UUID v7 technical baseline.

## 8. Why identity mapping belongs in `system.accounts`

The authenticated actor recorded on Business Facts and ERP Control Audit must resolve deterministically to the same persistent actor anchor used by all `*_by_account_id` foreign keys.

Keeping `(issuer, subject)` on `system.accounts` avoids:
- email/name-based identity matching
- deployment configuration becoming the only identity database
- ambiguous actor correlation after IdP profile changes
- adding a relation 56 solely for one external identity per v0.1 account

v0.1 supports one OIDC identity pair per ERP account.

Multiple external identities per account are not introduced without a later formal architecture revision.

## 9. Relation-count decision

P3.5 does not introduce:
- `roles`
- `permissions`
- `account_roles`
- `account_permissions`
- `external_identities`
- `sessions`
- `password_credentials`
- `refresh_tokens`

Formal relational count therefore remains:

```text
55 relations
14 schemas
```

If a later security requirement genuinely needs additional persistence, it must go through the relational revision rule before migration/application implementation.

## 10. Account provisioning

v0.1 uses pre-provisioned ERP accounts.

Successful OIDC authentication does not automatically create a new ERP account.

Request resolution:

```text
validated OIDC principal
→ exact issuer + subject
→ lookup system.accounts
→ account not found: reject
→ account inactive: reject
→ account active: resolve ActorAccountId
```

Unknown authenticated identities do not become ERP users merely by reaching the login callback.

## 11. Bootstrap boundary

The first ERP account may be provisioned using an explicit host-side deployment/maintenance mechanism.

Requirements:
- not an anonymous HTTP ERP endpoint
- not a staging/production no-password bypass
- not a permanent hard-coded account
- operator action is explicit and auditable operationally
- account receives an application-generated UUID and explicit external `(issuer, subject)` binding

A future in-product Security Administration workflow may replace manual provisioning only after its command/authorization design is formally defined.

## 12. Account active-state enforcement

A valid authentication cookie is not sufficient by itself to authorize ERP activity indefinitely.

For authenticated ERP requests, server-side actor resolution must confirm the persistent account still exists and is `active = true`.

If the account is missing or inactive:
- no ActorContext is produced for Application Write Commands
- the request is rejected

This ensures disabling an ERP account takes effect without waiting only for natural cookie expiration.

Caching, if later added for performance, must preserve a bounded revocation delay explicitly accepted as a security control. No such cache is required in v0.1.

## 13. Actor context

Clients never submit authoritative actor IDs.

The API authentication/actor-resolution layer produces the existing Application `ActorAccountId` from `system.accounts.id`.

Application write flow is conceptually:

```text
OIDC/Cookie authentication
→ validated principal
→ system.accounts lookup by issuer + subject
→ active-account validation
→ ActorAccountId
→ capability authorization
→ endpoint/Application command
```

The authenticated actor used for:
- CommandExecution
- Audit
- created/recorded/confirmed/deleted actor columns

is server-resolved.

## 14. Authorization model

Authorization uses explicit operation/capability policy names.

Current examples include:
- `sales.confirm`
- `sales.correct-allocation`
- `finance.pay`
- `finance.correct`
- `inventory.adjust`
- `party.supplier.lifecycle`
- `party.customer.lifecycle`
- `data-protection.hard-delete`

Capability names are technical authorization policy identifiers around explicit Application operations.
They do not define a YowThi job-title or role hierarchy.

`party.supplier.lifecycle` is the ordinary target-specific ERP lifecycle capability used by V8-C4 Supplier Soft Delete / Restore.

`party.customer.lifecycle` is the ordinary target-specific ERP lifecycle capability used by V8-C5 Customer Soft Delete / Restore.

Neither ordinary lifecycle capability implies permission for physical deletion.

## 15. Capability assignment v0.1

v0.1 capability grants are deployment-configured and keyed by persistent ERP Account UUID.

Conceptual configuration:

```text
sales.confirm
  → account UUID A
  → account UUID B

finance.pay
  → account UUID A

party.supplier.lifecycle
  → account UUID B

party.customer.lifecycle
  → account UUID B
```

Rules:
- exact capability names
- exact account UUID grants
- no wildcard super-user role
- no inference from display name, email, phone, or Employee record
- no automatic capability assignment from unconfirmed organization/business roles

The mechanism may later move to persisted authorization administration only through a formal architecture revision.

Target-specific ERP lifecycle/correction capabilities may be added with their implementation slices without creating a new Business Rule or business-role hierarchy. Their grants remain explicit deployment configuration.

`finance.correct` is the ERP Control capability used by V8-C6 `CorrectPaymentAmount`. It is separate from `finance.pay`: permission to register a Payment does not automatically imply permission to amend a previously registered Payment. This authorization distinction is technical security architecture, not a YowThi business-role hierarchy.

## 16. Highest-authority operations

Existing Data Protection Hard Delete governance requires the highest authority boundary.

P3.5 implements this through explicit capability assignment, for example:

```text
data-protection.hard-delete
```

It does not invent a `SuperAdmin` role.

Who receives this capability is a controlled security configuration decision, not a new Business Rule encoded in the domain model.

`data-protection.hard-delete` is deliberately separate from ordinary lifecycle capabilities such as `party.supplier.lifecycle` and `party.customer.lifecycle`. Possession of an ordinary Soft Delete / Restore capability must not imply Hard Delete authority, and Hard Delete authority must not be reused as the normal lifecycle permission.

## 17. Authentication token storage

The ERP does not require downstream IdP API access for the confirmed v0.1 ERP boundary.

Therefore:
- do not request `offline_access` merely for ERP login
- do not persist refresh tokens
- do not store access/refresh tokens in React localStorage/sessionStorage
- do not place provider tokens in ERP database tables
- keep ASP.NET Core external token saving disabled unless a later approved integration requires it

The server authentication cookie represents the ERP browser session.

## 18. CSRF / antiforgery

Because browser authentication uses cookies, unsafe browser requests require antiforgery protection.

This protection is independent of CommandId/idempotency.
`Idempotency-Key` is not a CSRF defense.

Baseline:
- server issues/validates ASP.NET Core antiforgery tokens
- React sends the request token in a dedicated header

Confirmed technical header name:

```text
X-CSRF-TOKEN
```

Apply antiforgery to unsafe authenticated browser operations such as POST/PUT/PATCH/DELETE according to the endpoint model.

Authentication protocol callback endpoints follow the security requirements of the OIDC middleware rather than being treated as ERP Application Writes.

## 19. Same-origin / CORS interaction

Preferred deployment remains same-origin React + API where practical.

If browser UI and API are cross-origin:
- exact deployment-configured CORS origin allowlist
- credential support only for trusted configured origins
- never `AllowAnyOrigin` with credentials
- cookie SameSite policy must match the confirmed topology

No CORS wildcard becomes a shortcut around AuthN/AuthZ.

## 20. OIDC validation requirements

The configured OIDC middleware must validate protocol/security properties appropriate to the provider and flow, including the provider metadata/signing chain and standard anti-forgery/replay properties supplied by the framework.

The ERP must not accept an arbitrary client-supplied `iss` / `sub` pair as proof of identity.

Only claims from a successful configured authentication scheme may reach account resolution.

## 21. Test authentication boundary

API contract/integration tests may use a dedicated test authentication handler/principal.

The test scheme must:
- live only in test composition/configuration
- never become the default staging/production authentication path
- never create a deployment no-password bypass
- still exercise authenticated/unauthenticated and capability metadata behavior

## 22. Logging and secret handling

Production must not log:
- OIDC client secret
- authorization code
- access token
- refresh token
- full authentication cookie
- unrestricted claims dumps

Authentication failures may be logged with sanitized operational context and trace correlation.

Business/control request/response body logging remains off by default under the REST/API security baseline.

## 23. Session lifecycle

Cookie session lifetime and renewal settings are deployment security controls.

The architecture requires:
- finite lifetime
- explicit sign-out support
- account active-state revalidation

It does not create a server-side sessions relation in v0.1.

A future requirement for centralized session enumeration/revocation beyond active-account revocation requires a formal persistence/security revision.

## 24. Implementation order after this gate

After this document and the `system.accounts` mapping revision are validated:

1. P3.5 hard gate is complete.
2. Generate `InitialV01` in the dedicated migrations project.
3. Static-review generated migration against the 55-relation baseline plus this auth-specific account correction.
4. Verify no pending EF model changes.
5. Only then activate Docker Desktop / PostgreSQL 18 for P5 apply and provider-specific acceptance.

Actual OIDC provider wiring may proceed before Business API production use, but it does not require delaying the formal initial schema once this relational identity shape is included.

Later P6 ERP Control capability names remain additive technical authorization contracts and do not reopen the P3.5 relation-count decision unless persisted authorization administration is introduced.

## 25. Explicit non-decisions

P3.5 does not define:
- a YowThi Role Master
- employee-to-account linkage
- organization/job-title-based permissions
- approval chains
- MFA policy owned by ERP
- a specific commercial Identity Provider
- a password reset flow inside ERP
- bearer-token API for third-party integrations
- API keys
- service accounts
- multiple external identities per account

Those require real use cases and separate security/architecture decisions.

## 26. Acceptance gate

P3.5 is complete when:
- this architecture is committed
- `system.accounts` EF mapping contains nullable paired issuer/subject columns
- paired/nonblank CHECK constraints are present
- partial unique external identity index is present
- relation count remains 55
- existing full-model hard gates remain green
- self-hosted restore/build/test succeeds

After that, `InitialV01` is no longer blocked by AuthN/AuthZ architecture and the project may enter P4.

## 27. V8 lifecycle capability confirmations — 2026-09-04

Ordinary ERP lifecycle authorization is target-specific:

```text
party.supplier.lifecycle
party.customer.lifecycle
```

Confirmed operations:
- `party.supplier.lifecycle` → V8-C4 Supplier Soft Delete / Restore
- `party.customer.lifecycle` → V8-C5 Customer Soft Delete / Restore

Confirmed security consequences:
- capability grants remain deployment-configured by persistent Account UUID
- no `roles`, `permissions`, `account_roles`, or other new authorization relations are introduced
- no YowThi business-role hierarchy is inferred
- neither ordinary lifecycle capability grants Hard Delete
- `data-protection.hard-delete` remains the separate highest-authority physical-delete capability
- future target-specific lifecycle/correction capabilities may follow the same technical pattern without being treated as Business Rules

V8-C4 and V8-C5 therefore require no AuthN/AuthZ persistence revision and no EF migration.

## 28. V8-C6 Finance correction capability confirmation - 2026-09-04

Payment amount correction uses the explicit capability:

```text
finance.correct
```

Confirmed operation:
- `finance.correct` controls V8-C6 `CorrectPaymentAmount`

Confirmed security consequences:
- correction permission is distinct from `finance.pay`
- capability grants remain deployment-configured by persistent Account UUID
- no Role Master, permission tables, account-role persistence, or business-role hierarchy is introduced
- `finance.correct` does not imply `data-protection.hard-delete`
- no AuthN/AuthZ persistence revision or EF migration is required