#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ZenPath = '',
    [string]$FirefoxPath = '',
    [string]$BackupRoot = '',
    [int]$PauseUpdates = 0,
    [int]$RemoveBorder = 1,
    [string]$SineProfilePath = '',
    [int]$SineFireZen = 1,
    [switch]$OpenSineSetup,
    [switch]$Preview,
    [switch]$PreviewSine,
    [string]$PreviewPath = ''
)

$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    if ($PSVersionTable.PSEdition -eq 'Core') {
        throw 'Use Run FireZEN.cmd, which opens Windows PowerShell 5.1.'
    }
    $sourceFiles = @('FireZen.Core.cs','FireZen.Sine.cs','FireZen.UI.cs','FireZen.SineUI.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    Add-Type -Path $sourceFiles -ReferencedAssemblies System.Windows.Forms,System.Drawing,System.Web.Extensions,System.Core,System.IO.Compression,System.IO.Compression.FileSystem
    [System.Windows.Forms.Application]::EnableVisualStyles()
    [System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
    $form = New-Object FireZenHelper.MainForm($PSCommandPath, $ZenPath, $FirefoxPath, $BackupRoot, ($PauseUpdates -eq 1), ($RemoveBorder -eq 1), $SineProfilePath, ($SineFireZen -eq 1), [bool]$OpenSineSetup)
    if ($PreviewSine) {
        $form.SaveSinePreview($PreviewPath)
        $form.Dispose()
    } elseif ($Preview) {
        $form.SavePreview($PreviewPath)
        $form.Dispose()
    } else {
        [System.Windows.Forms.Application]::Run($form)
    }
} catch {
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'FireZEN Helper could not start', 'OK', 'Error') | Out-Null
    exit 1
}
