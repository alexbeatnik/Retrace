# Builds and runs the unit tests with the compiler built into Windows — the same
# zero-toolchain approach as build.ps1. The app sources and tests\*.cs are
# compiled together into a console Retrace.Tests.exe (the /main switch picks the
# test runner's entry point over the app's), then executed; a non-zero exit code
# means failures, which is what CI keys off.
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$outExe = Join-Path $PSScriptRoot 'Retrace.Tests.exe'
$sources = @(Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter *.cs) +
           @(Get-ChildItem (Join-Path $PSScriptRoot 'tests') -Filter *.cs) |
    Sort-Object Name | ForEach-Object { $_.FullName }

$cscArgs = @(
    '/nologo'
    '/target:exe'
    '/platform:anycpu'
    '/codepage:65001'
    '/main:Retrace.Tests.Program'
    "/out:$outExe"
    '/r:System.dll'
    '/r:System.Core.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
) + $sources

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Test build FAILED' -ForegroundColor Red
    exit 1
}

& $outExe
$code = $LASTEXITCODE
if ($code -eq 0) { Write-Host 'All tests passed' -ForegroundColor Green }
else { Write-Host 'Tests FAILED' -ForegroundColor Red }
exit $code
