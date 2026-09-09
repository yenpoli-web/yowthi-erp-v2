# AuthN/AuthZ Implementation Architecture v0.1 — YowThi ERP V2

Status: **DECISION / v0.1**
Revision basis: P3.5 AuthN/AuthZ Candidate Architecture, formally confirmed on 2026-08-28; revised by the approved P8 Security Foundation on 2026-09-08 for persisted account capability administration and Development Test Admin login; revised on 2026-09-09 by the confirmed Transaction Deletion Control for deletion re-authentication.

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
- `system.accounts.identity_issuer` / `identity_subject` remain the external identity binding
- the approved P8 Security Foundation adds `system.account_capability_grants` for persistent explicit Account-to-Capability authorization
- all other relational rules in `docs/10` remain authoritative
- relation count is 56 and schema count remains 14

## 2. Security goals

The v0.1 authentication/authorization design must:
- require authenticated identity for ERP business and control endpoints
- keep actor identity server-controlled
- resolve the authenticated principal to persistent `system.accounts`
- avoid ERP-owned password storage
- require recent re-authentication before destructive transaction deletion without introducing a second ERP-owned password database
- provide Development-only automatic deletion re-authentication for the persistent Development Test Admin without exposing a plaintext test credential in the browser
- avoid browser access/refresh-token storage
- use operation/capability authorization rather than inventing business roles
- allow an account to be disabled centrally and rejected on subsequent requests
- keep test authentication isolated from staging/production
- permit passwordless Development Test Admin login only in the Development environment when explicitly enabled, while making that route impossible to enable in staging/production
- not treat Tailscale or network location as application authentication

## 3. Authentication mechanism

Deployed ERP authentication uses a provider-neutral external OpenID Connect Identity Provider.

Interactive deployed browser authentication:
- OpenID Connect Authorization Code flow
- PKCE
- confidential server-side client where the provider supports/requires it
- ASP.NET Core authentication middleware owns the protocol exchange

Development additionally supports one explicit passwordless test-login route. That route is not an OIDC substitute for deployed environments: it is mapped only when the host environment is `Development` and `Security:DevelopmentTestAdmin:Enabled=true`. Attempting to enable it outside Development is a startup error.

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
- `Secure` is mandatory outside the Development environment
- Development loopback HTTP acceptance may use `CookieSecurePolicy.SameAsRequest`; staging/production use `CookieSecurePolicy.Always`
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

P3.5 originally introduced no authorization relation. The approved P8 Security Foundation is the formal later persistence revision and introduces exactly one authorization relation:
- `system.account_capability_grants`

It still does not introduce:
- `roles`
- `permissions` as a separate capability-master relation
- `account_roles`
- `external_identities`
- `sessions`
- `password_credentials`
- `refresh_tokens`

Formal relational count is now:

```text
56 relations
14 schemas
```

Capabilities remain code-defined technical authorization contracts; Account-to-Capability assignment is persisted. Additional security persistence still requires a formal relational revision.

## 10. Account provisioning

ERP accounts are explicitly provisioned. Successful OIDC authentication still does not automatically create a new ERP account.

Normal account provisioning and maintenance is now available through the authorized Account Management slice guarded by `security.account.manage`. The account can optionally be bound to one exact `(identity_issuer, identity_subject)` pair and receives explicit persisted capability grants.

OIDC login resolution:

```text
validated OIDC principal
→ exact issuer + subject
→ lookup system.accounts
→ account not found: reject
→ account inactive: reject
→ issue ERP cookie containing persistent Account UUID
```

Subsequent ERP requests resolve that persistent Account UUID against `system.accounts`, require `active = true`, then load current active capability grants. Unknown authenticated identities do not become ERP users merely by reaching the login callback.

## 11. Bootstrap / Development Test Admin boundary

The approved P8 Security Foundation provides a Development-only bootstrap account for local acceptance work.

Development Test Admin requirements:
- a real `system.accounts` row with an application-generated UUID v7
- fixed technical development identity `(urn:yowthi:development, test-admin)`
- explicit grant rows for every currently known capability, including `security.account.manage`
- no wildcard `*`, bypass authorization handler, or generic `SuperAdmin` role
- ordinary Account Management cannot disable it, rebind it, or reduce its grants
- passwordless login endpoint exists only in Development and only when explicitly enabled
- staging/production configuration attempting to enable the route fails startup
- Tailscale membership is network reachability only and is never treated as ERP authentication

Production/staging initial account provisioning remains an explicit deployment/maintenance concern until the configured OIDC-backed Account Management workflow is available there.

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
OIDC login or Development-only test login
→ ERP encrypted cookie with persistent Account UUID
→ system.accounts lookup by Account UUID
→ active-account validation
→ load active system.account_capability_grants
→ ActorAccountId + capability claims
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
- `procurement.batch.lifecycle`
- `party.supplier.lifecycle`
- `party.customer.lifecycle`
- `data-protection.hard-delete`

Capability names are technical authorization policy identifiers around explicit Application operations.
They do not define a YowThi job-title or role hierarchy.

`party.supplier.lifecycle` is the ordinary target-specific ERP lifecycle capability used by V8-C4 Supplier Soft Delete / Restore.

`party.customer.lifecycle` is the ordinary target-specific ERP lifecycle capability used by V8-C5 Customer Soft Delete / Restore.

`procurement.batch.lifecycle` is the target-specific ERP lifecycle capability used by V8-C9 Procurement Batch Reopen. It is deliberately distinct from `procurement.confirm`: authority to register or close normal Procurement work does not automatically imply authority to reopen a Closed Procurement Batch.

None of these ordinary lifecycle capabilities implies permission for physical deletion.

## 15. Capability assignment v0.1

Capability assignments are persisted in `system.account_capability_grants` and keyed by persistent ERP Account UUID plus exact capability name.

Rules:
- exact capability names from the code-defined capability registry
- one structural grant identity per `(account_id, capability_name)`
- active/inactive grant state is explicit
- no wildcard super-user role
- no inference from display name, email, phone, or Employee record
- no automatic capability assignment from unconfirmed organization/business roles
- authorization is re-resolved from persistent Account and current active grants on authenticated requests, so permission changes do not depend on waiting for cookie expiry
- Account Management itself requires `security.account.manage`

Target-specific ERP lifecycle/correction capabilities may be added with their implementation slices without creating a new Business Rule or business-role hierarchy. New capability identifiers are added to the code registry; grants remain explicit persisted security state.

`finance.correct` is the ERP Control capability used by V8-C6 `CorrectPaymentAmount`. It is separate from `finance.pay`: permission to register a Payment does not automatically imply permission to amend a previously registered Payment. This authorization distinction is technical security architecture, not a YowThi business-role hierarchy.

## 16. Highest-authority operations

Existing Data Protection Hard Delete governance requires the highest authority boundary.

P3.5 implements this through explicit capability assignment, for example:

```text
data-protection.hard-delete
```

It does not invent a `SuperAdmin` role.

Who receives this capability is a controlled Account Management security decision, not a new Business Rule encoded in the domain model.

`data-protection.hard-delete` is deliberately separate from ordinary lifecycle capabilities such as `party.supplier.lifecycle`, `party.customer.lifecycle`, and `procurement.batch.lifecycle`. Possession of an ordinary lifecycle capability must not imply Hard Delete authority, and Hard Delete authority must not be reused as the normal lifecycle permission.

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
- antiforgery cookies follow the same environment boundary as the session cookie: Development loopback HTTP may use `SameAsRequest`; non-Development remains `Secure=Always`
- the request-token cookie exposed to React is non-`HttpOnly` by design, while the framework antiforgery cookie remains `HttpOnly`

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

API contract/integration tests may use a dedicated test authentication handler/principal. Interactive local Development may additionally use the persisted Development Test Admin described above.

The test scheme must:
- live only in test composition/configuration
- never become the default staging/production authentication path
- never create a deployed no-password bypass
- still exercise authenticated/unauthenticated and capability metadata behavior

The Development Test Admin must exercise the same persistent Account, capability policy, ActorContext, command idempotency, Audit, and PostgreSQL paths as normal ERP operations; only credential entry/OIDC challenge is bypassed in Development.

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

## 24. P8 Security Foundation implementation order

`InitialV01` is immutable historical baseline and must not be rewritten. The approved P8 Security Foundation proceeds forward:

1. update the 56-relation EF model and metadata hard gates
2. implement Account Management, persistent capability resolution, cookie/CSRF runtime, and Development Test Admin boundaries
3. generate a new forward migration in the dedicated migrations project; do not edit `InitialV01`
4. static-review the generated migration so it adds only the approved security relation/index/FKs required by this revision
5. run architecture/API/integration hard gates
6. apply the forward migration to PostgreSQL 18 development only after those gates pass
7. exercise Development Test Admin login and real Supplier/Customer PostgreSQL operations through the same-origin PWA/API runtime
8. validate the exact candidate SHA on the YowThi self-hosted runner before main promotion

OIDC provider-specific deployed wiring remains provider-neutral and can follow without changing the persisted Account/Capability model.

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

The P8 Security Foundation revision is complete when:
- `system.accounts` retains nullable paired issuer/subject columns and their existing structural checks/index
- `system.account_capability_grants` is the only new relation required by this revision
- relation count is exactly 56 across the same 14 schemas
- Account Management is guarded by `security.account.manage`
- all API capability policies resolve through persisted active grants
- Development Test Admin is a real persistent account with all explicit capabilities and cannot be downgraded through ordinary Account Management
- passwordless Development login is absent outside Development and enabling it outside Development fails startup
- unsafe authenticated API writes require antiforgery validation
- real same-origin PWA → API → PostgreSQL Supplier/Customer operations pass
- full local hard gates and exact-SHA self-hosted restore/build/test succeed

`InitialV01` remains unchanged; the security revision is delivered by a new forward migration.

## 27. V8 lifecycle capability confirmations — 2026-09-04

Ordinary ERP lifecycle authorization is target-specific:

```text
party.supplier.lifecycle
party.customer.lifecycle
procurement.batch.lifecycle
```

Confirmed operations:
- `party.supplier.lifecycle` → V8-C4 Supplier Soft Delete / Restore
- `party.customer.lifecycle` → V8-C5 Customer Soft Delete / Restore
- `procurement.batch.lifecycle` → V8-C9 Procurement Batch Reopen

Confirmed security consequences:
- capability grants are explicit persisted `system.account_capability_grants` rows keyed by persistent Account UUID
- no `roles`, `account_roles`, or wildcard authorization mechanism is introduced
- no YowThi business-role hierarchy is inferred
- ordinary lifecycle capabilities do not grant Hard Delete
- `procurement.batch.lifecycle` is separate from `procurement.confirm`
- `data-protection.hard-delete` remains the separate highest-authority physical-delete capability
- future target-specific lifecycle/correction capabilities may follow the same technical pattern without being treated as Business Rules

V8-C4, V8-C5, and V8-C9 themselves required no AuthN/AuthZ persistence revision. The later P8 Security Foundation is a separate formally approved technical security revision and introduces the forward capability-grant migration.

## 28. V8-C6 Finance correction capability confirmation - 2026-09-04

Payment amount correction uses the explicit capability:

```text
finance.correct
```

Confirmed operation:
- `finance.correct` controls V8-C6 `CorrectPaymentAmount`

Confirmed security consequences:
- correction permission is distinct from `finance.pay`
- capability grants are explicit persisted rows keyed by persistent Account UUID and capability name
- no Role Master, account-role persistence, wildcard super-user role, or business-role hierarchy is introduced
- `finance.correct` does not imply `data-protection.hard-delete`
- V8-C6 itself required no AuthN/AuthZ persistence revision; the later P8 Security Foundation independently introduces persisted capability grants and its forward EF migration

## 29. Transaction deletion re-authentication — 2026-09-09

`docs/32-transaction-deletion-control-v0.1.md` confirms that Procurement, Processing, and Sales master/detail deletion is a deliberate ERP maintenance operation and requires a recent authentication confirmation before Soft Delete or Hard Delete.

Security consequences:
- deployed accounts continue to authenticate through the configured external OIDC provider; ERP does not store a duplicate login password
- deployed deletion confirmation must use a fresh/recent interactive IdP authentication event and then carry only a short-lived deletion re-authentication assertion in the protected ERP session
- the exact freshness window is a technical security setting, not a Business Rule
- the Development Test Admin remains passwordless for normal Development login
- when the current persistent account is the Development Test Admin, a Development-only authenticated endpoint may automatically issue the short-lived deletion re-authentication assertion
- the Development automatic path is not mapped outside Development and cannot be enabled there
- no plaintext Development test password may be embedded in React source, generated bundles, localStorage, sessionStorage, request logs, or repository configuration
- deletion commands still require their explicit capability authorization in addition to recent re-authentication
- Restore is not considered a destructive delete action and does not require deletion re-authentication in this revision

The deletion re-authentication assertion must survive normal per-request persistent Account/capability re-resolution only while still fresh. It must not become a wildcard authorization claim or an unbounded session bypass.

## 30. System-wide deletion authentication baseline — 2026-09-09

`docs/33-system-wide-deletion-control-v0.1.md` extends the deletion security boundary from Procurement / Processing / Sales to all remaining UI-operable business, master-data, and transaction modules after the current transaction lifecycle work closes.

Confirmed security consequences:
- Soft Delete always requires fresh re-authentication by the current user before execution
- for deployed accounts, password/credential entry occurs in the configured identity-provider re-authentication flow rather than in an ERP-owned password database
- Hard Delete always requires the explicit highest deletion capability `data-protection.hard-delete` in addition to target/module lifecycle authorization where applicable
- Hard Delete also always requires fresh re-authentication
- Restore remains non-destructive and does not require deletion re-authentication unless later explicitly changed
- Development Test Admin continues to use the Development-only automatic re-authentication path without exposing a reusable plaintext test credential
- ordinary create/update/confirm/lifecycle capabilities never imply Hard Delete authority
- retained Audit and CommandExecution evidence are not converted into ordinary user-deletable operational records simply because business modules receive deletion support