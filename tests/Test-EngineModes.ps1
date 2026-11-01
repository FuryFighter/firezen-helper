#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$product = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = [IO.File]::ReadAllText((Join-Path $product 'FireZen.Core.cs'))
# Change only two methods in the in-memory fixture compilation. Shipped source is never patched.
function Replace-TestMethod($text,$signature,$replacement) {
    $start = $text.IndexOf($signature)
    if ($start -lt 0) { throw ('Missing fixture isolation boundary: '+$signature) }
    $next = [regex]::Match($text.Substring($start+$signature.Length), '(?m)^        (?:private|public|internal) static ')
    if (!$next.Success) { throw ('Missing next method after '+$signature) }
    $end = $start+$signature.Length+$next.Index
    return $text.Substring(0,$start)+$replacement+"`r`n`r`n"+$text.Substring($end)
}
$source = Replace-TestMethod $source '        private static void EnsureClosed()' '        private static void EnsureClosed() { }'
$fakeGuard = @'
        private static void RequireMozillaEngine(string firefoxPath, string expectedEngine)
        {
            if (File.ReadAllText(System.IO.Path.Combine(firefoxPath, "xul.dll")) != "Firefox xul")
                throw new InvalidOperationException("Keep Firefox engine requires genuine Mozilla Firefox files. Repair or reinstall Firefox first.");
        }
'@
$source = Replace-TestMethod $source '        private static void RequireMozillaEngine(string firefoxPath, string expectedEngine)' $fakeGuard
Add-Type -TypeDefinition $source -ReferencedAssemblies System.Web.Extensions,System.Core
Write-Output 'SYNTHETIC ENGINE FIXTURES: only in-memory process and Mozilla-signature guards are substituted; shipped guards remain active.'
$root = Join-Path (Join-Path $product 'work\tests') ('engine-modes-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$script:passed=0; $script:failed=0
function Put($path,$value) { [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))|Out-Null; [IO.File]::WriteAllText($path,$value) }
function Read($path) { [IO.File]::ReadAllText($path) }
function Assert($condition,$message) { if (!$condition) { throw $message } }
function Reject($action,$pattern) { $caught=$false; try { & $action } catch { $caught=$true; Assert ($_.Exception.ToString() -match $pattern) ('Unexpected error: '+$_) }; Assert $caught 'Expected rejection' }
function Test($name,$action) { try { & $action; $script:passed++; Write-Output "PASS $name" } catch { $script:failed++; Write-Output "FAIL $name : $_" } }
function Fixture($name) {
    $base=Join-Path $root $name; $z=Join-Path $base 'Zen'; $f=Join-Path $base 'Firefox'; $b=Join-Path $base 'Backups'
    foreach ($item in @(@($z,'Zen','zen.exe'),@($f,'Firefox','firefox.exe'))) {
        $path=$item[0]
        Put (Join-Path $path 'application.ini') ("[App]`r`nName="+$item[1]+"`r`nVersion=1.0")
        Put (Join-Path $path 'platform.ini') "[Build]`r`nMilestone=144.0"
        $pe=New-Object byte[] 128; $pe[0]=77; $pe[1]=90; $pe[60]=64; $pe[64]=80; $pe[65]=69; $pe[68]=100; $pe[69]=134
        [IO.File]::WriteAllBytes((Join-Path $path $item[2]),$pe)
        Put (Join-Path $path 'browser\omni.ja') ($item[1]+' browser')
        Put (Join-Path $path 'omni.ja') ($item[1]+' omni')
        Put (Join-Path $path 'xul.dll') ($item[1]+' xul')
    }
    Put (Join-Path $z 'browser\new\asset.txt') 'introduced'
    Put (Join-Path $f 'browser\firefox-only.txt') 'preserve target-only browser file'
    Put (Join-Path $f 'unrelated.dll') 'preserve unrelated root file'
    return @{Z=$z; F=$f; B=$b}
}
function Snapshot($folder) {
    return (@(Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($folder.Length)+'='+[FireZenHelper.Engine]::HashFile($_.FullName)
    }) -join "`n")
}
function ApplyKeep($p,$pause=$false,$borderless=$false,$callback=$null) { [FireZenHelper.Engine]::Apply($p.Z,$p.F,$p.B,$pause,$borderless,$true,$callback) }
function Restore($p,$backup) { [FireZenHelper.Engine]::Restore($backup,$p.F,$null) }
Test 'Keep inspection excludes only the root engine from payload totals' {
    $p=Fixture 'inspect'; $classic=[FireZenHelper.Engine]::Inspect($p.Z,$p.F); $keep=[FireZenHelper.Engine]::Inspect($p.Z,$p.F,$true)
    Assert ($keep.FileCount -eq $classic.FileCount-1) 'wrong retained-engine payload count'
    Assert ($keep.SourceBytes -eq $classic.SourceBytes-(Get-Item (Join-Path $p.Z 'xul.dll')).Length) 'engine bytes still included'
}
Test 'Keep application copies Zen browser and omni while preserving engine and executable hashes' {
    $p=Fixture 'keep'; $engine=[FireZenHelper.Engine]::HashFile((Join-Path $p.F 'xul.dll')); $exe=[FireZenHelper.Engine]::HashFile((Join-Path $p.F 'firefox.exe'))
    $backup=ApplyKeep $p; $m=(Read (Join-Path $backup 'manifest.json'))|ConvertFrom-Json
    Assert ((Read (Join-Path $p.F 'browser\omni.ja')) -ceq 'Zen browser') 'browser not copied'
    Assert ((Read (Join-Path $p.F 'omni.ja')) -ceq 'Zen omni') 'root omni not copied'
    Assert ((Read (Join-Path $p.F 'browser\new\asset.txt')) -ceq 'introduced') 'new browser asset missing'
    Assert ((Read (Join-Path $p.F 'browser\firefox-only.txt')) -ceq 'preserve target-only browser file') 'target-only browser file changed'
    Assert ((Read (Join-Path $p.F 'unrelated.dll')) -ceq 'preserve unrelated root file') 'unrelated root file changed'
    Assert ([FireZenHelper.Engine]::HashFile((Join-Path $p.F 'xul.dll')) -eq $engine) 'Firefox engine changed'
    Assert ([FireZenHelper.Engine]::HashFile((Join-Path $p.F 'firefox.exe')) -eq $exe) 'Firefox executable changed'
    Assert ($m.KeepFirefoxEngine -and $m.PreservedEngineHash -eq $engine) 'preserved engine not recorded'
    Assert (@($m.Files|Where-Object {$_.RelativePath -ieq 'xul.dll'}).Count -eq 0) 'engine entered replacement journal'
    Assert (!(Test-Path (Join-Path $backup 'payload\xul.dll')) -and !(Test-Path (Join-Path $backup 'original\xul.dll'))) 'engine unnecessarily staged or backed up'
}
Test 'Keep options merge policies and borderless defaults then restore every original file exactly' {
    $p=Fixture 'options'; $policy=Join-Path $p.F 'distribution\policies.json'; $defaults=Join-Path $p.F ([FireZenHelper.Engine]::BorderPreferencePath)
    Put $policy '{"policies":{"DisableTelemetry":true,"DisableAppUpdate":false},"custom":17}'
    Put $defaults '// existing defaults'; $original=Snapshot $p.F
    $backup=ApplyKeep $p $true $true; $m=(Read (Join-Path $backup 'manifest.json'))|ConvertFrom-Json
    $json=(Read $policy)|ConvertFrom-Json
    Assert ($json.policies.DisableAppUpdate -and $json.policies.DisableTelemetry -and $json.custom -eq 17) 'policy merge lost settings'
    Assert ((Read $defaults).Contains('pref("zen.theme.content-element-separation", 0);')) 'borderless preference absent'
    Assert ($m.PausedUpdates -and $m.BorderlessDefault) 'options absent from manifest'
    Restore $p $backup; Assert ((Snapshot $p.F) -ceq $original) 'directory not restored byte-exactly'
}
Test 'Keep reapplication and both restores preserve the original engine' {
    $p=Fixture 'repeat'; $original=Snapshot $p.F; $first=ApplyKeep $p; $applied=Snapshot $p.F
    $second=ApplyKeep $p; Restore $p $second; Assert ((Snapshot $p.F) -ceq $applied) 'second restore changed first application'
    Restore $p $first; Assert ((Snapshot $p.F) -ceq $original) 'first restore did not recover original'
}
Test 'Keep injected copy failure rolls back exactly including optional files' {
    $p=Fixture 'rollback'; $original=Snapshot $p.F
    $cb=[Action[FireZenHelper.ProgressUpdate]] {param($u) if($u.Stage -eq 'AppliedFile') {throw 'Injected keep-mode failure'}}
    Reject {ApplyKeep $p $true $true $cb} 'Any changed files were restored'
    Assert ((Snapshot $p.F) -ceq $original) 'rollback changed original files'
    $manifest=Get-ChildItem $p.B -Recurse -Filter manifest.json|Select-Object -First 1
    Assert (((Read $manifest.FullName)|ConvertFrom-Json).Status -eq 'RolledBack') 'rollback not journaled'
}
Test 'Keep rejects an existing Zen engine with repair guidance before backup or writes' {
    $p=Fixture 'zen-engine'; Put (Join-Path $p.F 'xul.dll') 'Zen xul'; $original=Snapshot $p.F
    Reject {ApplyKeep $p} '(?i)(repair|reinstall).*Firefox'
    Assert (!(Test-Path $p.B)) 'backup created for rejected engine'; Assert ((Snapshot $p.F) -ceq $original) 'rejected install changed'
}
Test 'Engine changes during staging reject before any payload is applied' {
    $p=Fixture 'engine-race'; $script:changed=$false
    $cb=[Action[FireZenHelper.ProgressUpdate]] {param($u) if($u.Stage -eq 'BackingUp' -and !$script:changed) {$script:changed=$true; Put (Join-Path $p.F 'xul.dll') 'external engine replacement'}}
    Reject {ApplyKeep $p $false $false $cb} '(?i)(engine|Firefox).*(changed|updated|replaced|repair|reinstall)'
    Assert ((Read (Join-Path $p.F 'browser\omni.ja')) -ceq 'Firefox browser') 'payload applied before engine rejection'
    Assert ((Read (Join-Path $p.F 'omni.ja')) -ceq 'Firefox omni') 'root payload applied'
    Assert (!(Test-Path (Join-Path $p.F 'browser\new\asset.txt'))) 'new payload applied'
    Assert ((Read (Join-Path $p.F 'xul.dll')) -ceq 'external engine replacement') 'external engine mutation overwritten'
}
Test 'Restore refuses a changed preserved engine before any browser edits' {
    $p=Fixture 'restore-engine'; $backup=ApplyKeep $p; Put (Join-Path $p.F 'xul.dll') 'external engine replacement'; $before=Snapshot $p.F
    Reject {Restore $p $backup} '(?i)(engine|Firefox).*(changed|updated|replaced)'
    Assert ((Snapshot $p.F) -ceq $before) 'restore partially modified browser'
}
Test 'Legacy manifest without mode fields restores the classic engine replacement' {
    $p=Fixture 'legacy'; $original=Snapshot $p.F; $backup=[FireZenHelper.Engine]::Apply($p.Z,$p.F,$p.B,$false,$null)
    $path=Join-Path $backup 'manifest.json'; $m=(Read $path)|ConvertFrom-Json
    $m.PSObject.Properties.Remove('KeepFirefoxEngine'); $m.PSObject.Properties.Remove('PreservedEngineHash'); Put $path ($m|ConvertTo-Json -Depth 15)
    Restore $p $backup; Assert ((Snapshot $p.F) -ceq $original) 'legacy manifest no longer restores'
}
Test 'Existing explicit borderless overload still uses classic Zen engine replacement' {
    $p=Fixture 'classic'; $backup=[FireZenHelper.Engine]::Apply($p.Z,$p.F,$p.B,$false,$true,$null)
    Assert ((Read (Join-Path $p.F 'xul.dll')) -ceq 'Zen xul') 'existing overload changed mode'
    $m=(Read (Join-Path $backup 'manifest.json'))|ConvertFrom-Json
    Assert (!$m.KeepFirefoxEngine) 'classic manifest claims to keep Firefox engine'
    Assert (@($m.Files|Where-Object {$_.RelativePath -ieq 'xul.dll'}).Count -eq 1) 'classic engine omitted from journal'
}
Test 'Keep mode still rejects mismatched Firefox engine versions before writes' {
    $p=Fixture 'version'; Put (Join-Path $p.F 'platform.ini') "[Build]`r`nMilestone=143.0"; $original=Snapshot $p.F
    Reject {ApplyKeep $p} 'Version mismatch'; Assert (!(Test-Path $p.B)) 'backup created'; Assert ((Snapshot $p.F) -ceq $original) 'version rejection changed files'
}
Test 'Keep mode still rejects mismatched architectures before writes' {
    $p=Fixture 'architecture'; $path=Join-Path $p.F 'firefox.exe'; $pe=[IO.File]::ReadAllBytes($path); $pe[68]=76; $pe[69]=1; [IO.File]::WriteAllBytes($path,$pe); $original=Snapshot $p.F
    Reject {ApplyKeep $p} 'Architecture mismatch'; Assert (!(Test-Path $p.B)) 'backup created'; Assert ((Snapshot $p.F) -ceq $original) 'architecture rejection changed files'
}
Write-Output "ENGINE MODES RESULT passed=$script:passed failed=$script:failed"
Write-Output "Fixtures retained: $root"
if ($script:failed) { exit 1 }
