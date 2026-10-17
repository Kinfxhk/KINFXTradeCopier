<#
KINFX Trade Copier - beginner install helper (Windows PowerShell 5+).
Builds the SIM TEST file (source + "#define KINFX_SIM_TEST") and copies it into NinjaTrader 8's Custom\AddOns.
It does NOT connect to any account, place any order, or touch your account list.

  powershell -ExecutionPolicy Bypass -File scripts\install-simtest.ps1            # install
  powershell -ExecutionPolicy Bypass -File scripts\install-simtest.ps1 -WhatIf    # show what it would do
  powershell -ExecutionPolicy Bypass -File scripts\install-simtest.ps1 -Remove    # remove it again

After installing: press F5 in the NinjaScript Editor to compile, then New > KINFX Trade Copier.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param([switch]$Remove, [string]$NinjaDocs = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'NinjaTrader 8'))

$ErrorActionPreference = 'Stop'
$repo   = Split-Path -Parent $PSScriptRoot
$src    = Join-Path $repo 'src\AddOns\KINFXTradeCopier.cs'
$dstDir = Join-Path $NinjaDocs 'bin\Custom\AddOns'
$dst    = Join-Path $dstDir 'KINFXTradeCopier.cs'
$backup = Join-Path $NinjaDocs 'KINFX-backups'      # OUTSIDE bin\Custom: NinjaTrader compiles every .cs it finds there

function Fail($m) { Write-Host "STOP: $m" -ForegroundColor Red; exit 1 }

if (-not (Test-Path $NinjaDocs)) { Fail "NinjaTrader folder not found: $NinjaDocs`n(Start NinjaTrader 8 once so it creates it, or pass -NinjaDocs <path>.)" }
if (-not (Test-Path $dstDir))    { Fail "Folder not found: $dstDir" }
if (-not $Remove -and -not (Test-Path $src)) { Fail "Source not found: $src (run this from a full clone of the repo)" }

if (Get-Process NinjaTrader -ErrorAction SilentlyContinue) {
    Write-Host 'Note: NinjaTrader is running. That is fine; it only reads the file when you press F5 in the NinjaScript Editor.' -ForegroundColor Yellow
}

# NinjaTrader compiles EVERY .cs under Custom. Two copies of this add-on would collide.
$others = @(Get-ChildItem (Join-Path $NinjaDocs 'bin\Custom') -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -ne $dst -and (Select-String -Path $_.FullName -Pattern 'static class DashboardSafety' -SimpleMatch -Quiet) })
if ($others.Count -gt 0) { Fail ("Another copy of this add-on exists:`n  " + ($others.FullName -join "`n  ") + "`nMove it out of bin\Custom first.") }

if ($Remove) {
    if (Test-Path $dst) {
        if ($PSCmdlet.ShouldProcess($dst, 'remove')) {
            New-Item -ItemType Directory -Force $backup | Out-Null
            Copy-Item $dst (Join-Path $backup ("KINFXTradeCopier." + (Get-Date -Format yyyyMMdd-HHmmss) + '.cs.bak'))
            Remove-Item $dst
            Write-Host 'Removed. Press F5 in the NinjaScript Editor to recompile. Your settings under templates\KINFXTradeCopier are kept.' -ForegroundColor Green
        }
    } else { Write-Host 'Nothing to remove.' }
    exit 0
}

# Build the SIM TEST text: define on the first line, keep CRLF line endings.
$text = [IO.File]::ReadAllText($src, [Text.Encoding]::UTF8) -replace "`r`n", "`n"
if ($text -match '(?m)^\s*#define\s+KINFX_SIM_TEST') { Fail 'Source already contains a KINFX_SIM_TEST define; refusing to change it.' }
$out = ("#define KINFX_SIM_TEST`n" + $text) -replace "`n", "`r`n"
$bytes = (New-Object Text.UTF8Encoding($true)).GetPreamble() + [Text.Encoding]::UTF8.GetBytes($out)
$sha = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($bytes)) -replace '-', '').ToLower()

if ($PSCmdlet.ShouldProcess($dst, "install SIM TEST build (sha256 $sha)")) {
    if (Test-Path $dst) {
        New-Item -ItemType Directory -Force $backup | Out-Null
        Copy-Item $dst (Join-Path $backup ("KINFXTradeCopier." + (Get-Date -Format yyyyMMdd-HHmmss) + '.cs.bak'))
    }
    [IO.File]::WriteAllBytes($dst, $bytes)
    $check = (Get-FileHash $dst -Algorithm SHA256).Hash.ToLower()
    if ($check -ne $sha) { Fail "Written file hash differs ($check). Nothing was verified; remove it with -Remove." }
    Write-Host "Installed SIM TEST build: $dst" -ForegroundColor Green
    Write-Host "sha256 $sha"
    Write-Host ''
    Write-Host 'Next: (1) NinjaTrader > New > NinjaScript Editor, press F5 (wait for "Compile succeeded").'
    Write-Host '      (2) Create the allow-list file ONLY if your accounts are prop-firm simulated accounts:'
    Write-Host "          $NinjaDocs\templates\KINFXTradeCopier\sim-allow.txt   (or answer Yes to the risk warning; see README, "Write sim-allow.txt by hand")"
    Write-Host '      (3) New > KINFX Trade Copier.'
}
