# Project Charter — YowThi ERP V2

## ERP CONTROL GOVERNANCE

- ERP Control Governance defines technical and operational control boundaries. These controls must be strict, auditable, and must not be confused with Business Rules.
- Business Rules must come from real YowThi operations, users, management, finance, laws, and real exception cases.
- AI/developers must not invent Business Rules merely to make implementation easier.
- ERP must support business reality; business operations must not be distorted to fit technical convenience.
- Major technical decisions should be recorded as ADRs.

## TECHNICAL BASELINE

Frontend:
- React + TypeScript + Vite
- React Router
- TanStack Query
- pnpm

Backend:
- ASP.NET Core 10 / C# / .NET 10 LTS
- EF Core 10 / Npgsql
- Dapper / native SQL only when necessary
- Modular Monolith
- Domain-oriented modules
- REST / JSON / OpenAPI

Infrastructure:
- PostgreSQL 18 in Docker
- Docker Desktop / WSL2
- GitHub private repository
- GitHub Actions
- Windows 11 development workstation
- .NET 10 SDK
- Node 24 LTS
- pnpm
- VS Code
- Codex CLI

## LEGACY REFERENCE

`C:\yowthi-erp` is protected Legacy ERP:
- read-only reference
- migration source
- validation source

Never:
- modify
- delete
- move
- reset
- overwrite
- derive the new Domain Model from the old schema

Legacy H01/H02/H03 and legacy cost/schema structures are references only, not ERP V2 design constraints.

## DEVELOPMENT CONTROL PLANE

YowThi ERP Dev v2 is the formal controlled development management plane.
RDC is retired.

## DEVELOPMENT ACCESS

Tailscale is approved for development cross-device testing.
It does not replace ERP authentication.
Development-only no-password bootstrap access must not exist in staging/production.
