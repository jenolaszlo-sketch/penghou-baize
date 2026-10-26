param(
    [Parameter(Mandatory = $true)][string]$Plan,
    [Parameter(Mandatory = $true)][string]$Authorization,
    [Parameter(Mandatory = $true)][string]$ArtifactDestination,
    [Parameter(Mandatory = $true)][string]$Output
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$runner = Join-Path $repoRoot 'eng\Penghou.Baize.Qualification\bin\Release\net10.0\Penghou.Baize.Qualification.dll'
if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) {
    throw 'release-runner-missing'
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
foreach ($argument in @(
    $runner, 'validate-live', '--plan', $Plan, '--authorization', $Authorization,
    '--out', $ArtifactDestination, '--evidence-out', $Output
)) {
    [void]$startInfo.ArgumentList.Add($argument)
}

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
[void]$process.Start()
$stdout = $process.StandardOutput.ReadToEnd()
$stderr = $process.StandardError.ReadToEnd()
$process.WaitForExit()

$json = $stdout.Trim()
try {
    [void][System.Text.Json.JsonDocument]::Parse($json)
} catch {
    throw 'validate-live-output-invalid-json'
}

if ($process.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $Output -PathType Leaf)) {
    throw 'validate-live-evidence-output-missing'
}

if ($stderr.Length -gt 0) {
    [System.Console]::Error.Write($stderr)
}
exit $process.ExitCode
