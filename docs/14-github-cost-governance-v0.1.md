# GitHub Cost Governance v0.1 — YowThi ERP V2

Status: **DECISION / v0.1**

## Purpose

YowThi ERP V2 must use GitHub in a way that does not create paid overage risk after a free quota is exhausted.

This is a technical/operational governance constraint, not a YowThi Business Rule.

## Hard constraint

Do not rely on GitHub services whose normal use can continue into billable metered usage after an included free quota is exhausted.

If a GitHub feature has uncertain billing behavior, do not enable or depend on it until its current billing model is verified and the project explicitly approves it.

## Source-control use

GitHub remains approved for:
- Git repository hosting
- commits / branches / pull requests as needed
- source/history storage within the free plan constraints

## CI execution

Routine CI must not use GitHub-hosted runners for this private repository.

The backend validation workflow is configured as:
- automatic `push` execution only for `m*-validation` branches
- optional manual `workflow_dispatch` fallback
- `runs-on: [self-hosted, yowthi-erp-v2]`
- no automatic execution for `main`
- no GitHub-hosted runner labels

This allows candidate mapping branches such as `m6-validation` and `m7-validation` to validate automatically once a matching self-hosted runner is online, without consuming GitHub-hosted runner minutes.

The web workflow remains manual self-hosted until a later UI validation-branch convention is deliberately defined.

Until a matching self-hosted runner is deliberately configured, validation remains local-first.

A self-hosted runner may be used only on project-owned/user-owned compute where GitHub does not bill hosted-runner minutes. Any hardware, electricity, operating-system, network, or cloud cost of that machine is outside GitHub and must separately remain within the project's no-unapproved-cost rule.

## Local validation baseline

Backend:

```text
dotnet tool restore
dotnet restore YowThi.Erp.slnx
dotnet build YowThi.Erp.slnx --configuration Release
dotnet test YowThi.Erp.slnx --configuration Release
```

Frontend:

```text
cd src/YowThi.Erp.Web
pnpm install --frozen-lockfile
pnpm lint
pnpm typecheck
pnpm build
```

## GitHub features not to depend on without explicit zero-cost verification

Do not introduce as required project infrastructure:
- GitHub-hosted Actions runners
- larger/paid Actions runners
- Codespaces
- metered artifact/package/LFS storage
- paid GitHub add-ons or marketplace services
- any feature with free credits/minutes followed by possible overage billing

A feature may be reconsidered only after verifying that the intended usage cannot create charges, or after separate explicit approval to change this governance decision.

## Repository workflow rule

No future implementation commit may reintroduce `runs-on: ubuntu-latest`, `windows-latest`, `macos-latest`, or another GitHub-hosted runner label for routine CI without first revising this decision.

Automatic validation-branch execution is permitted only when every matching job is explicitly self-hosted.

No future workflow may enable automatic push/PR execution on a GitHub-hosted runner.

## Billing safety

Account-level billing/budget controls should be configured to prevent unintended paid usage where GitHub offers such controls. Repository code cannot by itself guarantee account-level billing settings, so project implementation must not rely on those settings as the only protection.
