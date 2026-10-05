param(
    [ValidateSet('net8.0', 'net10.0')][string] $Framework = 'net8.0',
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath($RepositoryRoot)
$qualification = Join-Path ([IO.Path]::GetTempPath()) ('baize-transport-' + [Guid]::NewGuid().ToString('N'))
$feed = Join-Path $qualification 'feed'
$cache = Join-Path $qualification 'packages'
$consumer = Join-Path $qualification 'consumer'
[void] (New-Item -ItemType Directory -Path $feed, $cache, $consumer)
$candidate = '0.3.0-transportqualification.1'
dotnet pack (Join-Path $repository 'src/Penghou.Baize/Penghou.Baize.csproj') --configuration Release --no-build --output $feed "-p:PackageVersion=$candidate"
if ($LASTEXITCODE -ne 0) { throw 'Baize candidate pack failed.' }
$configuration = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="candidate" value="$([System.Security.SecurityElement]::Escape($feed))"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
"@
[IO.File]::WriteAllText((Join-Path $consumer 'NuGet.Config'), $configuration)
$project = @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>$Framework</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup><PackageReference Include="Penghou.Baize" Version="[$candidate]"/><PackageReference Include="Penghou.Model.Abstractions" Version="[0.1.0-preview.1]"/><PackageReference Include="Penghou.Http.Abstractions" Version="[0.1.0-preview.1]"/></ItemGroup></Project>
"@
[IO.File]::WriteAllText((Join-Path $consumer 'Consumer.csproj'), $project)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'transport-consumer.cs') -Destination (Join-Path $consumer 'Program.cs')
dotnet restore (Join-Path $consumer 'Consumer.csproj') --packages $cache --configfile (Join-Path $consumer 'NuGet.Config') --no-cache
if ($LASTEXITCODE -ne 0) { throw 'Isolated public-package consumer restore failed.' }
$assets = Get-Content -LiteralPath (Join-Path $consumer 'obj/project.assets.json') -Raw | ConvertFrom-Json
$names = @($assets.libraries.PSObject.Properties.Name)
if (@($names | Where-Object { $_ -match '^Penghou\.Hufu(?:\.|/)' }).Count) { throw 'Hufu found in consumer closure.' }
foreach ($package in @('penghou.model.abstractions', 'penghou.http.abstractions')) {
    $metadata = Join-Path $cache "$package/0.1.0-preview.1/.nupkg.metadata"
    $source = (Get-Content -LiteralPath $metadata -Raw | ConvertFrom-Json).source
    if ($source.TrimEnd('/') -ne 'https://api.nuget.org/v3/index.json') { throw "$package did not come from public NuGet." }
}
dotnet run --project (Join-Path $consumer 'Consumer.csproj') --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Isolated transport consumer failed.' }
Write-Output "Qualified $Framework candidate Baize consumer with fresh public transport packages: $qualification"
