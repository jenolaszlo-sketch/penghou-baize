param([string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath($RepositoryRoot)
$sourceRoot = Join-Path $repository 'src'
$projects = @(Get-ChildItem -LiteralPath $sourceRoot -Directory | ForEach-Object {
    Get-ChildItem -LiteralPath $_.FullName -Filter '*.csproj' -File
})
if ($projects.Count -eq 0) { throw 'No Baize source projects found.' }
$checked = 0
foreach ($project in $projects) {
    [xml] $projectXml = Get-Content -LiteralPath $project.FullName
    foreach ($reference in @($projectXml.Project.ItemGroup.PackageReference)) {
        if ($reference.Include -match '^Penghou\.Hufu(?:\.|$)') {
            throw "Forbidden direct Hufu dependency in $($project.Name): $($reference.Include)"
        }
    }
    $assetsPath = Join-Path $project.DirectoryName 'obj/project.assets.json'
    if (-not (Test-Path -LiteralPath $assetsPath)) { throw "Restore first: $assetsPath" }
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $libraryNames = @($assets.libraries.PSObject.Properties.Name)
    $forbidden = @($libraryNames | Where-Object { $_ -match '^Penghou\.Hufu(?:\.|/)' })
    if ($forbidden.Count) { throw "Forbidden transitive Hufu dependency in $($project.Name): $forbidden" }
    foreach ($package in @('Penghou.Model.Abstractions', 'Penghou.Http.Abstractions')) {
        $matches = @($libraryNames | Where-Object { $_ -like "$package/*" })
        if ($matches.Count -ne 1 -or $matches[0] -cne "$package/0.1.0-preview.1") {
            throw "Expected exact published $package/0.1.0-preview.1 in $($project.Name); found $matches"
        }
        $packageRoot = $assets.packageFolders.PSObject.Properties.Name
        $library = $assets.libraries.PSObject.Properties[$matches[0]].Value
        $foundPublicSource = $false
        foreach ($folder in $packageRoot) {
            $metadata = Join-Path (Join-Path $folder $library.path) '.nupkg.metadata'
            if (Test-Path -LiteralPath $metadata) {
                $origin = Get-Content -LiteralPath $metadata -Raw | ConvertFrom-Json
                if ($origin.source.TrimEnd('/') -eq 'https://api.nuget.org/v3/index.json') { $foundPublicSource = $true }
            }
        }
        if (-not $foundPublicSource) { throw "Cannot prove public NuGet source for $package in $($project.Name)." }
    }
    $checked++
}
Write-Output "Verified $checked Baize source project dependency closures: exact public transport packages; no Hufu."
