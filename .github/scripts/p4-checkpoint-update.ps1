$ErrorActionPreference = 'Stop'

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Replace-Exact([string]$text, [string]$old, [string]$new, [string]$label) {
    if (-not $text.Contains($old)) {
        throw "Required text not found for $label"
    }
    return $text.Replace($old, $new)
}

function Replace-Regex([string]$text, [string]$pattern, [string]$replacement, [string]$label) {
    $regex = New-Object System.Text.RegularExpressions.Regex($pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $regex.IsMatch($text)) {
        throw "Required pattern not found for $label"
    }
    return $regex.Replace($text, $replacement, 1)
}

$readmePath = 'README.md'
$readme = [IO.File]::ReadAllText($readmePath)
$readme = Replace-Regex $readme '(?m)^Implementation P3 API technical shell.*InitialV01.*$' 'Implementation P4 is complete: the formal `InitialV01` migration has been generated, statically reviewed, drift-checked, and validated. Next: **P5 - PostgreSQL 18 persistence acceptance**.' 'README current phase'

$readmeMigrationSection = @'
## InitialV01 migration baseline

P4 is complete. Formal migration:

```text
src/YowThi.Erp.Infrastructure.Migrations/Migrations/20260828033151_InitialV01.cs
```

P4 acceptance:
- 55 `CreateTable` operations
- 14 PostgreSQL schemas
- no pending EF model changes after migration generation
- explicit lower snake_case database index names; convention/truncation fallback is guarded by ArchitectureTests
- P3.5 `system.accounts` OIDC identity columns, constraints, and partial unique index are included
- Inventory Position `NULLS NOT DISTINCT` identity is included
- Finance Outstanding formula is retained without inventing a permanent `outstanding >= 0` rule
- Audit/Outbox CommandId reference and retention boundaries are preserved
- self-hosted restore/build/test passed

`InitialV01` has **not** yet been applied to a real PostgreSQL database.

P5 now requires Docker Desktop / PostgreSQL 18:

```text
approved InitialV01
-> Docker Desktop ON
-> PostgreSQL 18 clean database
-> apply InitialV01
-> schema / migration-history introspection
-> PostgreSQL integration and concurrency acceptance
```

## Local .NET validation
'@
$readme = Replace-Regex $readme '## InitialV01 readiness\r?\n.*?## Local \.NET validation\r?\n' $readmeMigrationSection 'README InitialV01 section'
[IO.File]::WriteAllText($readmePath, $readme, $utf8NoBom)

$checkpointPath = 'docs/09-current-design-checkpoint.md'
$checkpoint = [IO.File]::ReadAllText($checkpointPath)
$checkpoint = Replace-Exact $checkpoint 'Checkpoint status: **v0.1 implementation baseline through P3.5**' 'Checkpoint status: **v0.1 implementation baseline through P4**' 'checkpoint status'
$checkpoint = Replace-Exact $checkpoint '- P3.5 AuthN/AuthZ architecture hard gate and auth-specific `system.accounts` mapping revision' "- P3.5 AuthN/AuthZ architecture hard gate and auth-specific `system.accounts` mapping revision`n- P4 `InitialV01` generation, static review, model-drift verification, and migration validation" 'completed P4 phase'

$checkpointNextPhase = @'
Current next phase:

**P5 - PostgreSQL 18 persistence acceptance.**

P4 `InitialV01` generation/static review is complete. Docker Desktop is now required for the first real PostgreSQL 18 apply, schema introspection, and integration/concurrency acceptance.

## 4. Core domain modules
'@
$checkpoint = Replace-Regex $checkpoint 'Current next phase:\r?\n.*?## 4\. Core domain modules\r?\n' $checkpointNextPhase 'checkpoint current phase'

$checkpoint = Replace-Exact $checkpoint '- migration history at `system.__ef_migrations_history`' "- migration history at `system.__ef_migrations_history``n- formal initial migration `InitialV01` generated and statically approved in P4`n- post-generation `dotnet ef migrations has-pending-model-changes` reports no model drift`n- `InitialV01` has not yet been applied to PostgreSQL" 'checkpoint migration status'
$checkpoint = Replace-Exact $checkpoint 'P4  InitialV01 generation/static review               NEXT' 'P4  InitialV01 generation/static review               COMPLETE' 'phase table P4'
$checkpoint = Replace-Exact $checkpoint 'P5  PostgreSQL 18 persistence acceptance' 'P5  PostgreSQL 18 persistence acceptance              NEXT' 'phase table P5'

$p4Result = @'
P4 result:
- one formal migration named `InitialV01` generated in the dedicated migrations project
- static review against `docs/10` plus auth-specific `docs/15` correction passed
- exactly 55 `CreateTable` operations across 14 PostgreSQL schemas
- generated migration contains the approved P3.5 `system.accounts` external OIDC identity mapping
- Inventory Position `NULLS NOT DISTINCT`, Finance Outstanding formula, and Audit/Outbox boundaries are preserved
- EF convention-generated/truncated database index names discovered during the first static review were corrected in the model before final migration generation
- all database index names are now guarded by a full-model explicit lower-snake-case metadata test
- `dotnet ef migrations has-pending-model-changes` reports no drift after generation
- self-hosted restore/build/test passed
- migration has not yet been applied to PostgreSQL

P5 activation:
'@
$checkpoint = Replace-Regex $checkpoint 'P4 rules:\r?\n.*?P5 activation:\r?\n' $p4Result 'checkpoint P4 result'

$nextStep = @'
## 23. Next step

Proceed with:

**P5 - PostgreSQL 18 persistence acceptance.**

Required sequence:

```text
P4 InitialV01 approved
-> Docker Desktop ON
-> PostgreSQL 18 clean database/container
-> apply InitialV01
-> verify system.__ef_migrations_history
-> schema / constraint / index introspection
-> row-version conflict acceptance
-> Finance CAS concurrency acceptance
-> CommandId race acceptance
-> Inventory identity/concurrency acceptance
-> Outbox SKIP LOCKED / lease acceptance
-> self-hosted validation
```

Do not start Business vertical slices until P5 persistence acceptance is green.
'@
$checkpoint = Replace-Regex $checkpoint '## 23\. Next step\r?\n.*\z' $nextStep 'checkpoint next step'
[IO.File]::WriteAllText($checkpointPath, $checkpoint, $utf8NoBom)

Write-Host 'P4 checkpoint documents updated.'
