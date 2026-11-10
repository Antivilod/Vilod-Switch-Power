$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location (Split-Path $PSScriptRoot -Parent)
if (-not $IsWindows) { throw 'Build the Windows installer on Windows x64.' }
& (Join-Path $PSScriptRoot 'build-source.ps1')
dotnet restore src/QuietPC.Setup --configfile NuGet.public.config
if ($LASTEXITCODE) { throw 'Setup restore failed' }
dotnet publish src/QuietPC.Setup -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/source-build/setup
if ($LASTEXITCODE) { throw 'Setup publish failed' }
$packageCache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
foreach ($package in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $source = Join-Path $packageCache $package
    $licenses = @(Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.(TXT|txt))?$' })
    if (-not $licenses.Count) { throw "Missing runtime license notices: $package" }
    foreach ($file in $licenses) {
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
        $destination = Join-Path 'artifacts/source-build/runtime-licenses' (Join-Path $package $relative)
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}
$forbidden = @(Get-ChildItem artifacts/source-build -Recurse -File | Where-Object { $_.Extension -in @('.pdb','.pfx','.key','.log') -or $_.Name -like '.env*' -or $_.Name -in @('profiles.json','recovery.json','portable.flag') })
if ($forbidden.Count) { throw 'Unexpected local data, symbols or credentials in build payload.' }
$compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Inno Setup 6 is required. Do not download an unverified replacement.' }
& $compiler "/DRepoRoot=$((Get-Location).Path)" installer/VSP.iss
if ($LASTEXITCODE) { throw 'Installer compilation failed' }
$setup = Get-Item artifacts/release/VSP-0.8.3-Setup-x64.exe
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $setup.FullName).Hash.ToLowerInvariant()
"$hash  $($setup.Name)" | Set-Content -Encoding utf8NoBOM artifacts/release/SHA256SUMS.txt
Write-Host 'Installer built. Installation and driver checks are still required.'
