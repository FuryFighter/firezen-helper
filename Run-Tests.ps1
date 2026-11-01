#requires -Version 5.1
[CmdletBinding()]
param([string]$PackageDirectory = '')

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Run this script with Windows PowerShell 5.1 (powershell.exe).' }
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) { $PackageDirectory = Join-Path $PSScriptRoot 'work\test-packages' }
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)

# Compile the complete product without opening a window or modifying a browser.
$sources = @('FireZen.Core.cs','FireZen.Sine.cs','FireZen.UI.cs','FireZen.SineUI.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
Add-Type -Path $sources -ReferencedAssemblies System.Windows.Forms,System.Drawing,System.Web.Extensions,System.Core,System.IO.Compression,System.IO.Compression.FileSystem
Write-Output 'PASS Full Windows interface and engine compilation'

# Downloads only to the test cache; verified existing files are reused.
[FireZenHelper.SineInstaller]::PrepareDownloads($PackageDirectory, $true, $null)
Write-Output 'PASS Official test packages match pinned SHA-256 fingerprints'

# Separate processes allow each suite to load its isolated test assembly.
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tests\Test-Borderless.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Browser/borderless fixture tests failed.' }
& $powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tests\Test-Sine.ps1') -PackageDirectory $PackageDirectory
if ($LASTEXITCODE -ne 0) { throw 'Sine fixture tests failed.' }
Write-Output 'SUCCESS: full compilation, verified packages, and all 36 fixture tests passed.'
