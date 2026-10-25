$ErrorActionPreference = 'Stop'
$binding = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'binding.json') -Raw | ConvertFrom-Json
function Get-LfUtf8Hash([string] $Path) {
    $normalized = [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($normalized))).ToLowerInvariant()
}
foreach ($entry in $binding.sourceSha256LfUtf8.PSObject.Properties) {
    if ((Get-LfUtf8Hash (Join-Path $PSScriptRoot $entry.Name)) -cne $entry.Value) { throw "Probe source binding differs: $($entry.Name)" }
}
$recordPath = Join-Path $PSScriptRoot 'results.jsonl'
if ((Get-LfUtf8Hash $recordPath) -cne $binding.resultsSha256LfUtf8) { throw 'Recorded probe results hash differs' }
$expected = @(Get-Content -LiteralPath $recordPath)
$rows = @($expected | ConvertFrom-Json)
if ($rows.Count -ne 26 -or @($rows.name | Select-Object -Unique).Count -ne 26 -or
    @($rows | Where-Object { $_.invariant -ne $true -or $null -ne $_.physicalWireAttempts }).Count -ne 0) { throw 'Probe records have invalid count/invariants/claim scope' }
$env:BAIZE_RUN_LIVE_TESTS = '0'
$env:BAIZE_ENV_FILE = Join-Path $PSScriptRoot '__stage0_no_environment_file__'
$actual = @(& dotnet (Join-Path $PSScriptRoot 'bin/Debug/net10.0/Penghou.Baize.Qualification.Tests.dll'))
if ($LASTEXITCODE -ne 0 -or $actual.Count -ne 26 -or
    [string]::Join("`n", $actual) -cne [string]::Join("`n", $expected)) { throw 'Probe replay differs from the complete saved cohort' }
'Verified 26/26 invariants and exact case/order-sensitive replay; source/result hashes match.'
