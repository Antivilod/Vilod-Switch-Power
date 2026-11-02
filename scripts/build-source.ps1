$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
foreach ($project in @('tests/QuietPC.Tests','src/QuietPC.App','src/QuietPC.Monitor','src/QuietPC.Service')) {
    dotnet restore $project --configfile NuGet.public.config
    if ($LASTEXITCODE) { throw "Restore failed: $project" }
}
dotnet build tests/QuietPC.Tests -c Release --no-restore
if ($LASTEXITCODE) { throw 'Core test build failed' }
dotnet tests/QuietPC.Tests/bin/Release/net10.0/QuietPC.Tests.dll
if ($LASTEXITCODE) { throw 'Core tests failed' }
foreach ($part in @(@('QuietPC.App','app'),@('QuietPC.Monitor','monitor'),@('QuietPC.Service','service'))) {
    dotnet publish "src/$($part[0])" -c Release -r win-x64 --self-contained true -o "artifacts/source-build/$($part[1])" -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE) { throw "Publish failed: $($part[0])" }
}
Write-Host 'Build only: artifacts/source-build. No service was installed or started.'
