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
Write-Output 'SYNTHETIC CACHE FIXTURES: only in-memory process and Mozilla-signature guards are substituted; shipped guards remain active.'
$root = Join-Path (Join-Path $product 'work\tests') ('cache-refresh-'+[guid]::NewGuid().ToString('N'))
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
function Profile($p,$name='Profile') {
    $path=Join-Path ([IO.Path]::GetDirectoryName($p.F)) $name
    Put (Join-Path $path 'prefs.js') '// preferences sentinel'
    Put (Join-Path $path 'cookies.sqlite') 'cookies sentinel'
    Put (Join-Path $path 'compatibility.ini') "[Compatibility]`r`nLastVersion=144.0_build/old`r`nLastOSABI=WINNT_x86_64-msvc`r`nLastPlatformDir=C:\old`r`nLastAppDir=C:\old`r`nEncryptedDatabases=keep-compatibility`r`nInvalidateCaches=0`r`n[Other]`r`nEncryptedDatabases=sentinel`r`nInvalidateCaches=other-section`r`n"
    return $path
}
function ApplyProfiles($p,$profiles,$callback=$null) { [FireZenHelper.Engine]::Apply($p.Z,$p.F,$p.B,$false,$false,$true,[string[]]$profiles,$callback) }
function RestoreProfiles($p,$backup,$profiles,$callback=$null) { [FireZenHelper.Engine]::Restore($backup,$p.F,[string[]]$profiles,$callback) }
function Flagged($profile) { return (Read (Join-Path $profile 'compatibility.ini')) -match '(?m)^InvalidateCaches=1\r?$' }
function Consume($profile) { $path=Join-Path $profile 'compatibility.ini'; Put $path ((Read $path).Replace('InvalidateCaches=1','InvalidateCaches=0')) }
Test 'All selected profiles are flagged before the first browser file replacement' {
    $p=Fixture 'multi'; $profiles=@((Profile $p 'One'),(Profile $p 'Two')); $script:observed=$false
    $cb=[Action[FireZenHelper.ProgressUpdate]] {param($u) if($u.Stage -eq 'AppliedFile') { foreach($profile in $profiles) {Assert (Flagged $profile) 'profile not flagged before browser edits'}; $script:observed=$true}}
    $backup=ApplyProfiles $p $profiles $cb
    Assert $script:observed 'browser apply callback absent'; Assert ((Read (Join-Path $p.F 'browser\omni.ja')) -eq 'Zen browser') 'browser payload absent'
}
Test 'Only Compatibility cache flag changes while metadata and profile data survive' {
    $p=Fixture 'metadata'; $profile=Profile $p; $path=Join-Path $profile 'compatibility.ini'; $original=Read $path
    $backup=ApplyProfiles $p @($profile)
    $expected=$original.Replace('InvalidateCaches=0','InvalidateCaches=1').Replace("`r`n","`n").TrimEnd("`n")
    Assert ((Read $path).Replace("`r`n","`n").TrimEnd("`n") -ceq $expected) 'unrelated cache metadata or section changed'
    Assert ((Read (Join-Path $profile 'prefs.js')) -ceq '// preferences sentinel') 'preferences changed'
    Assert ((Read (Join-Path $profile 'cookies.sqlite')) -ceq 'cookies sentinel') 'cookies changed'
}
Test 'Missing compatibility metadata is skipped without creating it on apply or restore' {
    $p=Fixture 'missing'; $profile=Profile $p; Remove-Item -LiteralPath (Join-Path $profile 'compatibility.ini'); $original=Snapshot $profile
    $backup=ApplyProfiles $p @($profile); Assert ((Snapshot $profile) -ceq $original) 'apply created metadata'
    RestoreProfiles $p $backup @($profile); Assert ((Snapshot $profile) -ceq $original) 'restore created metadata'
}
Test 'Restore rearms a consumed flag while preserving the current Firefox metadata' {
    $p=Fixture 'restore'; $profile=Profile $p; $original=Snapshot $p.F; $backup=ApplyProfiles $p @($profile); Consume $profile
    $path=Join-Path $profile 'compatibility.ini'; Put $path ((Read $path).Replace('144.0_build/old','144.0_new/current'))
    $current=Read $path; $script:observed=$false
    $cb=[Action[FireZenHelper.ProgressUpdate]] {param($u) if($u.Stage -eq 'Restoring') {Assert (Flagged $profile) 'restore edited browser before flagging'; $script:observed=$true}}
    RestoreProfiles $p $backup @($profile) $cb
    Assert $script:observed 'restore callback absent'; Assert ((Read $path) -ceq $current.Replace('InvalidateCaches=0','InvalidateCaches=1')) 'restore reverted current metadata'
    Assert ((Snapshot $p.F) -ceq $original) 'browser not restored exactly'
}
Test 'Old backups created without profile selection still flag profiles on restore' {
    $p=Fixture 'old-backup'; $profile=Profile $p; $original=Snapshot $p.F; $backup=ApplyKeep $p
    Assert (!(Flagged $profile)) 'old overload unexpectedly touched profile'
    RestoreProfiles $p $backup @($profile); Assert (Flagged $profile) 'old backup restore omitted refresh'; Assert ((Snapshot $p.F) -ceq $original) 'old backup did not restore browser'
}
Test 'Locked cache metadata aborts apply before any browser file changes' {
    $p=Fixture 'locked-apply'; $first=Profile $p 'One'; $profile=Profile $p 'Two'; $original=Snapshot $p.F; $path=Join-Path $profile 'compatibility.ini'; $metadata=Read $path
    $held=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
    try {Reject {ApplyProfiles $p @($first,$profile)} '(?i)(process|access|used|cache|compatibility|sharing|restored)'} finally {$held.Dispose()}
    Assert ((Snapshot $p.F) -ceq $original) 'locked profile allowed browser mutation'; Assert ((Read $path) -ceq $metadata) 'locked metadata changed'
}
Test 'Locked cache metadata aborts restore before any browser file changes' {
    $p=Fixture 'locked-restore'; $profile=Profile $p; $backup=ApplyProfiles $p @($profile); Consume $profile; $before=Snapshot $p.F
    $path=Join-Path $profile 'compatibility.ini'; $held=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
    try {Reject {RestoreProfiles $p $backup @($profile)} '(?i)(process|access|used|cache|compatibility|sharing)'} finally {$held.Dispose()}
    Assert ((Snapshot $p.F) -ceq $before) 'locked profile allowed partial restore'
}
Test 'Incompatible engine rejection leaves profile metadata untouched' {
    $p=Fixture 'bad-engine'; $profile=Profile $p; $before=Snapshot $profile; Put (Join-Path $p.F 'xul.dll') 'Zen xul'
    Reject {ApplyProfiles $p @($profile)} '(?i)(repair|reinstall).*Firefox'; Assert ((Snapshot $profile) -ceq $before) 'rejected engine flagged profile'
}
Test 'Source mutation during staging leaves profile metadata untouched' {
    $p=Fixture 'source-race'; $profile=Profile $p; $before=Snapshot $profile; $original=Snapshot $p.F
    $cb=[Action[FireZenHelper.ProgressUpdate]] {param($u) if($u.Stage -eq 'BackingUp') {Put (Join-Path $p.Z 'browser\omni.ja') ([guid]::NewGuid().ToString())}}
    Reject {ApplyProfiles $p @($profile) $cb} 'Zen changed'; Assert ((Snapshot $profile) -ceq $before) 'source mismatch flagged profile'; Assert ((Snapshot $p.F) -ceq $original) 'source mismatch changed browser'
}
Test 'Missing, file and overlapping profile paths are rejected before browser writes' {
    foreach($kind in 'missing','file','firefox','zen','backup') {
        $p=Fixture ('invalid-'+$kind); $original=Snapshot $p.F
        switch($kind) {
            'missing' {$path=Join-Path ([IO.Path]::GetDirectoryName($p.F)) 'absent'}
            'file' {$path=Join-Path ([IO.Path]::GetDirectoryName($p.F)) 'file.txt'; Put $path 'sentinel'}
            'firefox' {$path=Join-Path $p.F 'Profile'; Put (Join-Path $path 'compatibility.ini') '[Compatibility]'; $original=Snapshot $p.F}
            'zen' {$path=Join-Path $p.Z 'Profile'; Put (Join-Path $path 'compatibility.ini') '[Compatibility]'}
            'backup' {$path=Join-Path $p.B 'Profile'; Put (Join-Path $path 'compatibility.ini') '[Compatibility]'}
        }
        Reject {ApplyProfiles $p @($path)} '(?i)(profile|folder|directory|outside|overlap|exist)'; Assert ((Snapshot $p.F) -ceq $original) ('unsafe '+$kind+' profile changed browser')
    }
}
Test 'Junction profiles are rejected without changing the linked target' {
    $p=Fixture 'junction'; $profile=Profile $p; $before=Snapshot $profile; $link=Join-Path ([IO.Path]::GetDirectoryName($p.F)) 'LinkedProfile'
    New-Item -ItemType Junction -Path $link -Target $profile|Out-Null
    Reject {ApplyProfiles $p @($link)} '(?i)(junction|symbolic|reparse)'; Assert ((Snapshot $profile) -ceq $before) 'linked profile changed'; Assert (!(Test-Path $p.B)) 'backup created before path validation'
}
Test 'Rollback restores browser bytes and rearms cache flags consumed during apply' {
    $p=Fixture 'rollback'; $profile=Profile $p; $original=Snapshot $p.F
    $cb=[Action[FireZenHelper.ProgressUpdate]] {param($u) if($u.Stage -eq 'AppliedFile') {Consume $profile; throw 'Injected browser failure after cache flag consumption'}}
    Reject {ApplyProfiles $p @($profile) $cb} 'Any changed files were restored'
    Assert ((Snapshot $p.F) -ceq $original) 'rollback did not restore browser'; Assert (Flagged $profile) 'rollback did not rearm cache flag'
    Assert ((Read (Join-Path $profile 'cookies.sqlite')) -ceq 'cookies sentinel') 'rollback changed profile data'
}
Write-Output "CACHE REFRESH RESULT passed=$script:passed failed=$script:failed"
Write-Output "Fixtures retained: $root"
if ($script:failed) {exit 1}
