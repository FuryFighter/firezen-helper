param([string]$PackageDirectory = '')
$ErrorActionPreference='Stop'
$product=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) { $PackageDirectory = Join-Path $product 'work\test-packages' }
$packages=[IO.Path]::GetFullPath($PackageDirectory)
$root=Join-Path (Join-Path $product 'work\tests') ('sine-fixtures-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
$source=[IO.File]::ReadAllText((Join-Path $product 'FireZen.Core.cs'))
$start=$source.IndexOf('        private static void EnsureClosed()'); $end=$source.IndexOf('        public static string HashFile',$start)
if($start -lt 0 -or $end -lt 0) { throw 'Process guard stub boundary missing' }
$source=$source.Substring(0,$start)+"        private static void EnsureClosed() { }`r`n"+$source.Substring($end)
$testCore=Join-Path $root 'Core.test.cs'; [IO.File]::WriteAllText($testCore,$source)
Add-Type -Path @($testCore,(Join-Path $product 'FireZen.Sine.cs')) -ReferencedAssemblies System.Core,System.Web.Extensions,System.IO.Compression,System.IO.Compression.FileSystem
Write-Output 'PROCESS-ISOLATED FIXTURES: only in-memory test compilation bypasses process detection; no downloaded JS executes.'
$script:pass=0; $script:fail=0
function Put($p,$v) { [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($p))|Out-Null; [IO.File]::WriteAllText($p,$v) }
function Read($p) { [IO.File]::ReadAllText($p) }
function Assert($c,$m) { if(!$c) { throw $m } }
function Reject($a,$pattern) { $caught=$false; try { & $a } catch { $caught=$true; Assert ($_.Exception.ToString() -match $pattern) ('Wrong error: '+$_) }; Assert $caught 'Expected rejection' }
function Test($name,$a) { try { & $a; $script:pass++; Write-Output "PASS $name" } catch { $script:fail++; Write-Output "FAIL $name : $_" } }
function Fixture($name) {
  $base=Join-Path $root $name; $f=Join-Path $base 'Firefox'; $p=Join-Path $base 'Profile'; $b=Join-Path $base 'Backups'
  Put (Join-Path $f 'application.ini') "[App]`r`nName=Firefox`r`nVersion=159.0"
  Put (Join-Path $f 'platform.ini') "[Build]`r`nMilestone=159.0"
  $pe=New-Object byte[] 128; $pe[0]=77; $pe[1]=90; $pe[60]=64; $pe[64]=80; $pe[65]=69; $pe[68]=100; $pe[69]=134
  [IO.File]::WriteAllBytes((Join-Path $f 'firefox.exe'),$pe)
  Put (Join-Path $p 'prefs.js') '// sentinel preferences'
  Put (Join-Path $p 'cookies.sqlite') 'sentinel fake cookies'
  Put (Join-Path $f 'distribution\policies.json') '{"policies":{"DisableTelemetry":true}}'
  return @{F=$f; P=$p; B=$b}
}
function Install($p,$mod=$false,$cb=$null,$pkg=$packages) { [FireZenHelper.SineInstaller]::InstallFromPackages($p.F,$p.P,$p.B,$pkg,$mod,$cb) }
function Restore($p,$backup,$cb=$null) { [FireZenHelper.SineInstaller]::Restore($backup,$p.F,$p.P,$cb) }
function Untouched($p) { Assert ((Read (Join-Path $p.P 'prefs.js')) -ceq '// sentinel preferences') 'prefs changed'; Assert ((Read (Join-Path $p.P 'cookies.sqlite')) -ceq 'sentinel fake cookies') 'cookies changed'; Assert ((Read (Join-Path $p.F 'distribution\policies.json')) -ceq '{"policies":{"DisableTelemetry":true}}') 'policy changed' }
Test 'Verified package cache performs no download requests' { $script:downloaded=$false; $cb=[Action[FireZenHelper.ProgressUpdate]] {param($u) if($u.Stage -eq 'Downloading') {$script:downloaded=$true; throw 'Network path unexpectedly entered'} }; [FireZenHelper.SineInstaller]::PrepareDownloads($packages,$true,$cb); Assert (!$script:downloaded) 'download attempted' }
Test 'Compatibility cache flag preserves unrelated sections and restores exactly' { $p=Fixture 'compatibility'; $path=Join-Path $p.P 'compatibility.ini'; $original="[Compatibility]`r`nLastVersion=159.0_build/old`r`nLastOSABI=WINNT_x86_64-msvc`r`n[Other]`r`nSentinel=unchanged`r`n"; Put $path $original; $backup=Install $p; $actual=Read $path; Assert ($actual -match '(?m)^InvalidateCaches=1\r?$') 'cache invalidation flag absent'; Assert ($actual.Contains('LastVersion=159.0_build/old') -and $actual.Contains('LastOSABI=WINNT_x86_64-msvc') -and $actual.Contains('[Other]') -and $actual.Contains('Sentinel=unchanged')) 'existing compatibility keys lost'; Restore $p $backup; Assert ((Read $path) -ceq $original) 'compatibility original not restored byte-exactly' }
Test 'Fresh Sine only and introduced files removed on restore' { $p=Fixture 'fresh'; $backup=Install $p; Assert (Test-Path (Join-Path $p.F 'config.js')) 'config missing'; Assert (Test-Path (Join-Path $p.P 'chrome\JS\sine.sys.mjs')) 'engine missing'; Assert (Test-Path (Join-Path $p.P 'chrome\sine-mods\mods.json')) 'first-run mod registry parent not initialized'; Assert ((Read (Join-Path $p.P 'chrome\sine-mods\mods.json')).Trim() -eq '{}') 'mod unexpectedly installed'; Untouched $p; Restore $p $backup; Assert (!(Test-Path (Join-Path $p.F 'config.js'))) 'introduced config retained'; Assert (!(Test-Path (Join-Path $p.P 'chrome\JS\sine.sys.mjs'))) 'introduced engine retained'; Assert (!(Test-Path (Join-Path $p.P 'chrome\sine-mods\mods.json'))) 'introduced registry retained'; Untouched $p }
Test 'FireZen merges other mods and exactly restores existing registry/loader' {
  $p=Fixture 'mod'; $registry=Join-Path $p.P 'chrome\sine-mods\mods.json'; $original='{"other":{"id":"other","enabled":false,"homepage":"Example/Other","custom":123}}'
  Put $registry $original; Put (Join-Path $p.P 'chrome\sine-mods\other\chrome.css') 'other mod css'; Put (Join-Path $p.F 'config.js') '// existing sine.sys.mjs loader'; Put (Join-Path $p.P 'chrome\JS\core\main.mjs') 'old engine'
  $backup=Install $p $true; $m=(Read $registry)|ConvertFrom-Json; Assert ($m.other.custom -eq 123 -and !$m.other.enabled) 'other metadata changed'; $id=[FireZenHelper.SineInstaller]::ModId; Assert ($m.$id.enabled -and $m.$id.style.chrome -eq 'userChrome.css') 'FireZen metadata wrong'; Assert ((Read (Join-Path $p.P 'chrome\sine-mods\other\chrome.css')) -eq 'other mod css') 'other mod changed'; Untouched $p
  Restore $p $backup; Assert ((Read $registry) -ceq $original) 'registry not exact'; Assert ((Read (Join-Path $p.F 'config.js')) -ceq '// existing sine.sys.mjs loader') 'loader not exact'; Assert ((Read (Join-Path $p.P 'chrome\JS\core\main.mjs')) -ceq 'old engine') 'engine not exact'; Assert (!(Test-Path (Join-Path $p.P ('chrome\sine-mods\'+$id+'\userChrome.css')))) 'introduced mod retained'; Untouched $p
}
Test 'Malformed mod registry rejected before backup or writes' { $p=Fixture 'badregistry'; Put (Join-Path $p.P 'chrome\sine-mods\mods.json') '{bad'; Reject {Install $p $true} 'mods.json is invalid'; Assert (!(Test-Path $p.B)) 'backup created'; Assert (!(Test-Path (Join-Path $p.F 'config.js'))) 'loader written' }
Test 'Unrelated AutoConfig refused and preserved' { $p=Fixture 'autoconfig'; Put (Join-Path $p.F 'config.js') '// enterprise config'; Reject {Install $p} 'different config.js'; Assert ((Read (Join-Path $p.F 'config.js')) -ceq '// enterprise config') 'config changed'; Assert (!(Test-Path $p.B)) 'backup created' }
Test 'Tampered package checksum rejected without browser writes' { $p=Fixture 'checksum'; $cache=Join-Path $root 'badpackages'; [IO.Directory]::CreateDirectory($cache)|Out-Null; foreach($name in 'engine.zip','profile.zip','program.zip') {Copy-Item -LiteralPath (Join-Path $packages $name) -Destination $cache}; [IO.File]::AppendAllText((Join-Path $cache 'engine.zip'),'bad'); Reject {Install $p $false $null $cache} 'checksum mismatch'; Assert (!(Test-Path $p.B)) 'backup created' }
Test 'Corrupt original rejected before any restore writes' { $p=Fixture 'corrupt'; Put (Join-Path $p.F 'config.js') '// old sine.sys.mjs'; $backup=Install $p; Put (Join-Path $backup 'original\Program\config.js') 'tampered'; Reject {Restore $p $backup} 'damaged'; Assert (Test-Path (Join-Path $p.P 'chrome\JS\sine.sys.mjs')) 'partial restore removed engine'; Assert ((Read (Join-Path $p.F 'config.js')) -match 'Loads Sine') 'partial restore changed config' }
Test 'Traversal backup rejected before writes' { $p=Fixture 'traversal'; $backup=Install $p; $path=Join-Path $backup 'sine-manifest.json'; $m=(Read $path)|ConvertFrom-Json; $m.Files[0].RelativePath='..\escaped.txt'; Put $path ($m|ConvertTo-Json -Depth 15); Reject {Restore $p $backup} 'Unexpected Sine backup path'; Assert (Test-Path (Join-Path $p.P 'chrome\JS\sine.sys.mjs')) 'partial restore' }
Test 'Mismatched profile refused' { $p=Fixture 'wrongprofile'; $backup=Install $p; $other=Fixture 'otherprofile'; Reject {[FireZenHelper.SineInstaller]::Restore($backup,$p.F,$other.P,$null)} 'not an applied Sine backup'; Assert (Test-Path (Join-Path $p.F 'config.js')) 'loader changed' }
Test 'Injected apply failure rolls back without touching profile data' { $p=Fixture 'rollback'; $cb=[Action[FireZenHelper.ProgressUpdate]] {param($u) if($u.Stage -eq 'SineAppliedFile') {throw 'Injected failure'}}; Reject {Install $p $true $cb} 'Changed files were restored'; Assert (!(Test-Path (Join-Path $p.F 'config.js'))) 'loader retained'; $manifest=Get-ChildItem $p.B -Recurse -Filter sine-manifest.json|Select-Object -First 1; Assert (((Read $manifest.FullName)|ConvertFrom-Json).Status -eq 'RolledBack') 'wrong rollback status'; Untouched $p }
Test 'Failed rollback records RecoveryRequired and retry recovers' { $p=Fixture 'recovery'; $script:held=$null; $cb=[Action[FireZenHelper.ProgressUpdate]] {param($u) if($u.Stage -eq 'SineAppliedFile') {$script:held=[IO.File]::Open((Join-Path $p.F 'config.js'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None); throw 'Locked target'}}; try {Reject {Install $p $false $cb} 'automatic recovery could not finish'} finally {if($script:held) {$script:held.Dispose()}}; $backup=[FireZenHelper.SineInstaller]::LatestBackup($p.B,$p.F,$p.P); Assert ($null -ne $backup) 'recovery not discoverable'; Assert (((Read (Join-Path $backup 'sine-manifest.json'))|ConvertFrom-Json).Status -eq 'RecoveryRequired') 'wrong recovery status'; Restore $p $backup; Assert (!(Test-Path (Join-Path $p.F 'config.js'))) 'loader retained'; Untouched $p }
Write-Output "RESULT passed=$script:pass failed=$script:fail"
Write-Output "Fixtures retained: $root"
if($script:fail) {exit 1}
