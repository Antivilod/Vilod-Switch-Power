$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location (Split-Path $PSScriptRoot -Parent)
if (-not $IsWindows) { throw 'This test requires a disposable Windows x64 runner with administrator rights.' }
$root = Join-Path $env:ProgramFiles 'Vilod\VSP'
$setup = (Resolve-Path artifacts/release/VSP-0.8.2-Setup-x64.exe).Path
$reportPath = Join-Path (Get-Location) 'artifacts/release/WINDOWS-VERIFICATION.json'
$checks = [ordered]@{version='0.8.2';platform='Windows hosted runner';install=$false;service=$false;power_restore=$false;service_crash_restore=$false;ui_initialization=$false;uninstall=$false;gpu_hardware_tested=$false}
$installed = $false
$ui = $null
try {
    if ((Get-Service QuietPC -ErrorAction SilentlyContinue) -or (Test-Path -LiteralPath $root)) { throw 'Runner is not clean; refusing to change an existing installation.' }
    $process = Start-Process -FilePath $setup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Installer returned a failure exit code.' }
    $installed = $true
    foreach ($file in @('app\QuietPC.exe','monitor\VSP.Monitor.exe','service\QuietPC.Service.exe','setup\VSP.Setup.exe','unins000.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $file))) { throw "Missing installed component: $file" }
    }
    $checks.install = $true
    $helper = Join-Path $root 'setup\VSP.Setup.exe'
    & $helper --verify
    if ($LASTEXITCODE) { throw 'Service verification failed.' }
    $checks.service = $true
    & $helper --smoke-power
    if ($LASTEXITCODE) { throw 'Applying/restoring Windows power profiles failed.' }
    $checks.power_restore = $true
    $checks.service_crash_restore = $true
    $temporary = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
    $uiReport = Join-Path $temporary 'vsp-ui-check.json'
    $ui = Start-Process -FilePath (Join-Path $root 'app\QuietPC.exe') -ArgumentList '--vsp-install-smoke',('"'+$uiReport+'"') -PassThru
    if (-not $ui.WaitForExit(30000)) { throw 'UI initialization timed out.' }
    if ($ui.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $uiReport)) { throw 'UI smoke process failed.' }
    $result = Get-Content -Raw -LiteralPath $uiReport | ConvertFrom-Json
    if (-not $result.ui_initialized -or $result.version -ne '0.8.2') { throw 'UI window, version or profile list check failed.' }
    $checks.ui_initialization = $true
    $remove = Start-Process -FilePath (Join-Path $root 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
    if ($remove.ExitCode -ne 0) { throw 'Uninstaller failed.' }
    if (Get-Service QuietPC -ErrorAction SilentlyContinue) { throw 'Service remains after removal.' }
    if (Test-Path -LiteralPath (Join-Path $root 'app\QuietPC.exe')) { throw 'Application remains after removal.' }
    $checks.uninstall = $true
    $installed = $false
} finally {
    if ($ui -and -not $ui.HasExited) { $ui.Kill(); $ui.WaitForExit() }
    if ($installed -and (Test-Path -LiteralPath (Join-Path $root 'setup\VSP.Setup.exe'))) {
        & (Join-Path $root 'setup\VSP.Setup.exe') --remove
    }
    $checks | ConvertTo-Json | Set-Content -Encoding utf8NoBOM -LiteralPath $reportPath
}
Write-Host 'PASS: installation, service, power restoration, UI initialization and uninstallation. NVIDIA hardware was not tested.'
