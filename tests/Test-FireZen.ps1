param([switch]$IsolateProcessGuard)
$ErrorActionPreference = 'Stop'
$core = Join-Path $PSScriptRoot '..\FireZen.Core.cs'
if ($IsolateProcessGuard) {
  $source=[IO.File]::ReadAllText($core)
  $start=$source.IndexOf('        private static void EnsureClosed()')
  $end=$source.IndexOf('        public static string HashFile', $start)
  if ($start -lt 0 -or $end -lt 0) { throw 'Cannot isolate process guard' }
  $source=$source.Substring(0,$start)+"        private static void EnsureClosed() { }`r`n`r`n"+$source.Substring($end)
  Add-Type -TypeDefinition $source -ReferencedAssemblies System.Web.Extensions,System.Core
  Write-Output 'PROCESS-ISOLATED FIXTURE TESTS: in-memory EnsureClosed stub; shipped Core unchanged.'
} else { Add-Type -Path $core -ReferencedAssemblies System.Web.Extensions,System.Core }
$root = [IO.Path]::GetFullPath((Join-Path (Join-Path $PSScriptRoot '..\work\tests') ('fixtures-' + [guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory $root | Out-Null
$script:passed = 0; $script:failed = 0; $script:skipped = 0
function Assert($condition, $message) { if (!$condition) { throw $message } }
function Put($path,$value) { [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null; [IO.File]::WriteAllText($path,$value) }
function Read($path) { [IO.File]::ReadAllText($path) }
function Fixture($name) {
  $base = Join-Path $root $name
  $z=Join-Path $base 'Zen'; $f=Join-Path $base 'Firefox'; $b=Join-Path $base 'Backups'
  foreach($item in @(@($z,'Zen','zen.exe'),@($f,'Firefox','firefox.exe'))) {
    $path=$item[0]; [IO.Directory]::CreateDirectory($path)|Out-Null
    Put (Join-Path $path 'application.ini') ("[App]`r`nName="+$item[1]+"`r`nVersion=1.0")
    Put (Join-Path $path 'platform.ini') "[Build]`r`nMilestone=144.0"
    $pe=New-Object byte[] 128; $pe[0]=0x4d; $pe[1]=0x5a; $pe[60]=64; $pe[64]=0x50; $pe[65]=0x45; $pe[68]=0x64; $pe[69]=0x86
    [IO.File]::WriteAllBytes((Join-Path $path $item[2]),$pe)
    Put (Join-Path $path 'browser\omni.ja') ($item[1]+' browser')
    Put (Join-Path $path 'xul.dll') ($item[1]+' xul')
    Put (Join-Path $path 'omni.ja') ($item[1]+' omni')
  }
  Put (Join-Path $z 'browser\new\asset.txt') 'introduced'
  Put (Join-Path $f 'browser\firefox-only.txt') 'untouched'
  Put (Join-Path $f 'unrelated.dll') 'untouched root'
  return @{ Z=$z; F=$f; B=$b }
}
function Reject($action,$pattern) { $caught=$false; try { & $action } catch { $caught=$true; Assert ($_.Exception.ToString() -match $pattern) ('Unexpected error: '+$_) }; Assert $caught 'Expected rejection' }
function Test($name,$action) { try { & $action; $script:passed++; Write-Output "PASS $name" } catch { $script:failed++; Write-Output "FAIL $name : $_" } }
function Apply($p,$pause=$false,$callback=$null) { [FireZenHelper.Engine]::Apply($p.Z,$p.F,$p.B,$pause,$callback) }
function Restore($p,$backup) { [FireZenHelper.Engine]::Restore($backup,$p.F,$null) }
function VerifyOriginal($p) { Assert ((Read (Join-Path $p.F 'xul.dll')) -eq 'Firefox xul') 'xul not original'; Assert ((Read (Join-Path $p.F 'browser\omni.ja')) -eq 'Firefox browser') 'browser not original'; Assert (!(Test-Path (Join-Path $p.F 'browser\new\asset.txt'))) 'introduced file remains' }
Test 'Inspect accepts matching synthetic x64 builds' { $p=Fixture 'inspect'; $r=[FireZenHelper.Engine]::Inspect($p.Z,$p.F); Assert ($r.FileCount -eq 4) 'wrong count'; Assert ($r.Zen.Architecture -eq 'x64') 'wrong architecture' }
Test 'Version mismatch rejected' { $p=Fixture 'version'; Put (Join-Path $p.F 'platform.ini') "[Build]`r`nMilestone=143.0"; Reject { [FireZenHelper.Engine]::Inspect($p.Z,$p.F) } 'Version mismatch' }
Test 'Architecture mismatch rejected' { $p=Fixture 'arch'; $path=Join-Path $p.F 'firefox.exe'; $pe=[IO.File]::ReadAllBytes($path); $pe[68]=0x4c; $pe[69]=1; [IO.File]::WriteAllBytes($path,$pe); Reject { [FireZenHelper.Engine]::Inspect($p.Z,$p.F) } 'Architecture mismatch' }
Test 'LatestBackup ignores malformed JSON manifests' { $p=Fixture 'corrupt-latest'; Put (Join-Path $p.B '20261031-corrupt\manifest.json') '{bad'; Assert ($null -eq [FireZenHelper.Engine]::LatestBackup($p.B,$p.F)) 'invalid backup selected' }
$running=@(Get-Process -Name firefox,zen -ErrorAction SilentlyContinue)
if ($running.Count -and !$IsolateProcessGuard) {
  Test 'Apply refuses while real browser runs, without fixture writes' { $p=Fixture 'process'; Reject { Apply $p } 'Close Firefox and Zen'; VerifyOriginal $p; Assert (!(Test-Path $p.B)) 'backup was created before guard' }
  $script:skipped=10; Write-Output ('SKIP 10 mutation integration cases: '+$running.Count+' browser processes running; none stopped.')
} else {
  Test 'Copy/merge preserves unrelated files and full restore' {
    $p=Fixture 'roundtrip'; $exe=[FireZenHelper.Engine]::HashFile((Join-Path $p.F 'firefox.exe')); $backup=Apply $p
    Assert ((Read (Join-Path $p.F 'xul.dll')) -eq 'Zen xul') 'copy missing'
    Assert ((Read (Join-Path $p.F 'browser\new\asset.txt')) -eq 'introduced') 'new file missing'
    Assert ((Read (Join-Path $p.F 'browser\firefox-only.txt')) -eq 'untouched') 'merge removed target-only file'
    Assert ((Read (Join-Path $p.F 'unrelated.dll')) -eq 'untouched root') 'unrelated file changed'
    Assert ([FireZenHelper.Engine]::HashFile((Join-Path $p.F 'firefox.exe')) -eq $exe) 'exe changed'
    Assert ([FireZenHelper.Engine]::LatestBackup($p.B,$p.F) -eq $backup) 'latest missing'
    Restore $p $backup; VerifyOriginal $p
    Assert ($null -eq [FireZenHelper.Engine]::LatestBackup($p.B,$p.F)) 'restored backup still latest'
  }
  Test 'Existing policies merged and exactly restored' { $p=Fixture 'policy'; $path=Join-Path $p.F 'distribution\policies.json'; $original='{"policies":{"DisableTelemetry":true,"DisableAppUpdate":false},"custom":17}'; Put $path $original; $backup=Apply $p $true; $json=(Read $path)|ConvertFrom-Json; Assert ($json.policies.DisableAppUpdate -and $json.policies.DisableTelemetry -and $json.custom -eq 17) 'policy fields lost'; Restore $p $backup; Assert ((Read $path) -ceq $original) 'policy bytes not restored' }
  Test 'New policies removed on restore' { $p=Fixture 'newpolicy'; $backup=Apply $p $true; Restore $p $backup; Assert (!(Test-Path (Join-Path $p.F 'distribution\policies.json'))) 'policy remains' }
  Test 'Malformed policy rejected before backup or writes' { $p=Fixture 'badpolicy'; Put (Join-Path $p.F 'distribution\policies.json') '{bad'; Reject { Apply $p $true } 'policies.json'; VerifyOriginal $p; Assert (!(Test-Path $p.B)) 'backup created' }
  Test 'Tampered original rejected before any restore writes' { $p=Fixture 'tamper'; $backup=Apply $p; Put (Join-Path $backup 'original\xul.dll') 'tamper'; Reject { Restore $p $backup } 'damaged'; Assert ((Read (Join-Path $p.F 'browser\omni.ja')) -eq 'Zen browser') 'partial restore occurred' }
  Test 'Traversal manifest rejected without outside writes' { $p=Fixture 'traversal'; $backup=Apply $p; $path=Join-Path $backup 'manifest.json'; $m=(Read $path)|ConvertFrom-Json; $m.Files[0].RelativePath='browser\..\..\escaped.txt'; Put $path ($m|ConvertTo-Json -Depth 10); Reject { Restore $p $backup } 'unexpected file path'; Assert (!(Test-Path (Join-Path ([IO.Path]::GetDirectoryName($p.F)) 'escaped.txt'))) 'escaped write' }
  Test 'Changed firefox.exe prevents restore' { $p=Fixture 'changedexe'; $backup=Apply $p; [IO.File]::AppendAllText((Join-Path $p.F 'firefox.exe'),'changed'); Reject { Restore $p $backup } 'updated or replaced'; Assert ((Read (Join-Path $p.F 'xul.dll')) -eq 'Zen xul') 'partial restore' }
  Test 'Injected failure after first AppliedFile rolls back' { $p=Fixture 'rollback'; $callback=[Action[FireZenHelper.ProgressUpdate]] { param($u) if($u.Stage -eq 'AppliedFile') { throw 'Injected failure' } }; Reject { Apply $p $false $callback } 'Any changed files were restored'; VerifyOriginal $p; $manifest=Get-ChildItem $p.B -Recurse -Filter manifest.json | Select-Object -First 1; Assert (((Read $manifest.FullName)|ConvertFrom-Json).Status -eq 'RolledBack') 'wrong journal status' }
  Test 'Failed rollback records RecoveryRequired and retry restores' {
    $p=Fixture 'recovery'; $script:held=$null
    $callback=[Action[FireZenHelper.ProgressUpdate]] { param($u) if($u.Stage -eq 'AppliedFile') { $relative=$u.Message.Substring(8); $script:held=[IO.File]::Open((Join-Path $p.F $relative),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None); throw 'Injected locked-file failure' } }
    try { Reject { Apply $p $false $callback } 'automatic restore could not finish' } finally { if($script:held) { $script:held.Dispose() } }
    $backup=[FireZenHelper.Engine]::LatestBackup($p.B,$p.F); Assert ($null -ne $backup) 'recovery backup not discoverable'
    Assert (((Read (Join-Path $backup 'manifest.json'))|ConvertFrom-Json).Status -eq 'RecoveryRequired') 'wrong recovery status'
    Restore $p $backup; VerifyOriginal $p
    Assert (((Read (Join-Path $backup 'manifest.json'))|ConvertFrom-Json).Status -eq 'Restored') 'wrong final status'
  }
  Test 'Interrupted restore remains discoverable and is safely repeatable' {
    $p=Fixture 'restore-retry'; $backup=Apply $p
    $callback=[Action[FireZenHelper.ProgressUpdate]] { param($u) if($u.Stage -eq 'Restoring') { throw 'Injected restore interruption' } }
    Reject { [FireZenHelper.Engine]::Restore($backup,$p.F,$callback) } 'Injected restore interruption'
    Assert ([FireZenHelper.Engine]::LatestBackup($p.B,$p.F) -eq $backup) 'retry backup not discoverable'
    Restore $p $backup; VerifyOriginal $p
  }
}
Write-Output "RESULT passed=$script:passed failed=$script:failed skipped=$script:skipped"
Write-Output "Fixtures retained: $root"
if ($script:failed) { exit 1 }
