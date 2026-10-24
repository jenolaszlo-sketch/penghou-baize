param(
    [string] $ReplayPath = (Join-Path $PSScriptRoot 'challenge-results.jsonl')
)

$ErrorActionPreference = 'Stop'
$binding = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'binding.json') -Raw | ConvertFrom-Json
$normalized = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $ReplayPath)).Replace("`r`n", "`n")
$hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($normalized))).ToLowerInvariant()
if ($hash -cne $binding.resultsSha256LfUtf8) { throw 'Challenge replay differs from the bound result hash (UTF-8, LF)' }
$reviewPath = Join-Path $PSScriptRoot '../bq1a-remediation-rereview-probes/results.jsonl'
$before = @(Get-Content -LiteralPath $reviewPath | ConvertFrom-Json -AsHashtable)
$after = @(Get-Content -LiteralPath $ReplayPath | ConvertFrom-Json -AsHashtable)
if ($before.Count -ne 33 -or $after.Count -ne 33) { throw 'Expected 33 challenge records in each cohort' }

# Transform only the expected properties in memory. Never rewrite preserved
# review evidence; all other fields and the case order must remain identical.
$completionCases = @('R1-incomplete-then-unknown', 'R1-tool-incomplete-then-unknown', 'R1-routed-incomplete-then-unknown')
$ollamaCases = @('real-ollama-no-wire-id', 'R2-real-ollama-wire-id-present')
$changed = 0
for ($i = 0; $i -lt $before.Count; $i++) {
    $wanted = $before[$i]
    if ($wanted.name -cne $after[$i].name) { throw "Challenge order/name differs at $i" }
    if ($wanted.name -cin $completionCases) {
        $wanted.Succeeded = $false
        foreach ($contract in $wanted.contracts) {
            $contract.disposition = 'Failed'
            foreach ($check in $contract.checks) {
                if ($check.Id -ceq 'execution.completed') {
                    $check.status = 'Failed'
                    $check.Code = 'incomplete-or-unsuccessful-stream'
                }
                if ($check.Id -ceq 'stream.terminal') {
                    $check.status = 'Failed'
                    $check.Code = 'incomplete-terminal'
                }
            }
        }
        $changed++
    }
    if ($wanted.name -cin $ollamaCases) {
        $wanted.identity.applicability = 'Undetermined'
        $wanted.identity.ProviderIdentityCode = 'protocol-revision-unbound'
        $changed++
    }
    $expectedJson = ConvertTo-Json -InputObject $wanted -Depth 30 -Compress
    $actualJson = ConvertTo-Json -InputObject $after[$i] -Depth 30 -Compress
    if ($expectedJson -cne $actualJson) { throw "Unexpected challenge evidence: $($wanted.name)" }
}
if ($changed -ne 5) { throw 'Expected exactly 3 R1 and 2 Ollama records to change' }
'Verified 33/33 challenge outcomes: exactly the expected R1/R2 fields change; 28 records are unchanged.'
