#requires -Version 5.1
$ErrorActionPreference='Stop'
$product=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Add-Type -Path @((Join-Path $product 'FireZen.Core.cs'),(Join-Path $product 'FireZen.Sine.cs')) -ReferencedAssemblies System.Core,System.Web.Extensions,System.IO.Compression,System.IO.Compression.FileSystem
$root=Join-Path (Join-Path $product 'work\tests') ('profile-discovery-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
$script:passed=0; $script:failed=0; $script:entry=0
function Put($path,$value) {[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))|Out-Null; [IO.File]::WriteAllText($path,$value)}
function Assert($condition,$message) {if(!$condition) {throw $message}}
function Test($name,$action) {try {& $action; $script:passed++; Write-Output "PASS $name"} catch {$script:failed++; Write-Output "FAIL $name : $_"}}
function Fixture($name) {
    $base=Join-Path $root $name; $f=Join-Path $base 'Installed Firefox'; $r=Join-Path $base 'Mozilla Firefox'; $other=Join-Path $base 'Test Copy'
    [IO.Directory]::CreateDirectory($f)|Out-Null; [IO.Directory]::CreateDirectory($r)|Out-Null; [IO.Directory]::CreateDirectory($other)|Out-Null
    return @{F=$f; R=$r; Other=$other}
}
function Profile($p,$name,$owner='') {
    $path=Join-Path $p.R ('Profiles\'+$name); Put (Join-Path $path 'prefs.js') '// initialized fixture profile'
    if($owner) {Put (Join-Path $path 'compatibility.ini') ("[Compatibility]`r`nLastPlatformDir="+$owner+"`r`nLastVersion=159.0")}
    return $path
}
function Entry($name,$relative=$true,$default=$false) {
    $path=$name; $rel='0'; if($relative) {$path='Profiles/'+$name; $rel='1'}
    $flag=''; if($default) {$flag="`r`nDefault=1"}
    $script:entry++; return "[Profile$script:entry]`r`nName=fixture`r`nIsRelative=$rel`r`nPath=$path$flag`r`n"
}
function Ini($p,$text) {Put (Join-Path $p.R 'profiles.ini') $text}
function Discover($p,$hash='INSTALLED') {[FireZenHelper.SineInstaller]::DiscoverProfiles($p.F,$p.R,$hash)}
function Recommended($result,$path) {
    Assert ($result.RecommendedProfile -ieq $path) ('Wrong recommendation: '+$result.RecommendedProfile+'; '+$result.RecommendationReason)
    Assert ($result.Profiles.Count -gt 0 -and $result.Profiles[0] -ieq $path) 'Recommended profile not first in manual list'
    Assert (![string]::IsNullOrWhiteSpace($result.RecommendationReason)) 'Recommendation reason missing'
}
function Manual($result) {Assert ([string]::IsNullOrWhiteSpace($result.RecommendedProfile)) ('Unsafe automatic choice: '+$result.RecommendedProfile); Assert (![string]::IsNullOrWhiteSpace($result.RecommendationReason)) 'Manual-selection reason missing'}
Test 'Selected installation uses its own default despite alphabetically earlier test-copy default' {
    $p=Fixture 'scoped'; $a=Profile $p 'aaa-test-copy' $p.Other; $z=Profile $p 'zzz-installed' $p.F
    Ini $p ((Entry 'aaa-test-copy')+(Entry 'zzz-installed')+"[InstallTESTCOPY]`r`nDefault=Profiles/aaa-test-copy`r`n[InstallINSTALLED]`r`nDefault=Profiles/zzz-installed`r`n")
    $r=Discover $p; Recommended $r $z; Assert ($r.Profiles.Count -eq 2 -and $r.Profiles.Contains($a)) 'Other install profile unavailable for manual selection'
}
Test 'Modern profiles.ini install map overrides conflicting legacy installs.ini' {
    $p=Fixture 'modern'; $a=Profile $p 'old'; $z=Profile $p 'modern'
    Ini $p ("[General]`r`nVersion=2`r`n"+(Entry 'old')+(Entry 'modern')+"[InstallINSTALLED]`r`nDefault=Profiles/modern`r`n")
    Put (Join-Path $p.R 'installs.ini') "[INSTALLED]`r`nDefault=Profiles/old`r`n"
    Recommended (Discover $p) $z
    Ini $p ((Entry 'old')+(Entry 'modern')+"[InstallINSTALLED]`r`nDefault=Profiles/modern`r`n")
    Recommended (Discover $p) $z
    Ini $p ("[General]`r`nVersion=2`r`n"+(Entry 'old')+(Entry 'modern'))
    Manual (Discover $p)
}
Test 'Legacy installs.ini mapping is honored before profiles.ini migration' {
    $p=Fixture 'legacy'; $a=Profile $p 'other'; $z=Profile $p 'legacy'
    Ini $p ((Entry 'other')+(Entry 'legacy')); Put (Join-Path $p.R 'installs.ini') "[INSTALLED]`r`nDefault=Profiles/legacy`r`n"
    Recommended (Discover $p) $z
}
Test 'Absolute profile and install-default paths coexist with relative registrations' {
    $p=Fixture 'absolute'; $a=Profile $p 'relative'; $z=Profile $p 'absolute'
    Ini $p ((Entry 'relative')+(Entry $z $false)+"[InstallINSTALLED]`r`nDefault=$z`r`n")
    $r=Discover $p; Recommended $r $z; Assert ($r.Profiles.Contains($a)) 'Relative profile lost'
}
Test 'Global Profile Default flag cannot override a scoped installation default' {
    $p=Fixture 'global'; $a=Profile $p 'global'; $z=Profile $p 'scoped'
    Ini $p ((Entry 'global' $true $true)+(Entry 'scoped')+"[InstallINSTALLED]`r`nDefault=Profiles/scoped`r`n")
    Recommended (Discover $p) $z
}
Test 'Unknown installation with ambiguous registered profiles requires manual choice' {
    $p=Fixture 'ambiguous'; $a=Profile $p 'a'; $b=Profile $p 'b'
    Ini $p ((Entry 'a' $true $true)+(Entry 'b')+"[InstallOTHER]`r`nDefault=Profiles/a`r`n")
    $r=Discover $p 'UNKNOWN'; Manual $r; Assert ($r.Profiles.Count -eq 2) 'Manual choices lost'
}
Test 'Unique compatibility owner identifies selected installation without a mapped hash' {
    $p=Fixture 'owner'; $a=Profile $p 'aaa-other' $p.Other; $z=Profile $p 'zzz-owned' $p.F
    Ini $p ((Entry 'aaa-other' $true $true)+(Entry 'zzz-owned'))
    Recommended (Discover $p '') $z
}
Test 'Unique scoped default among same-owner profiles resolves owner fallback' {
    $p=Fixture 'owner-default'; $a=Profile $p 'a' $p.F; $b=Profile $p 'b' $p.F; $c=Profile $p 'other' $p.Other
    Ini $p ((Entry 'a')+(Entry 'b')+(Entry 'other')+"[InstallOWNER]`r`nDefault=Profiles/b`r`n[InstallOTHER]`r`nDefault=Profiles/other`r`n")
    Recommended (Discover $p 'UNKNOWN') $b
}
Test 'Multiple defaults owned by selected installation remain ambiguous' {
    $p=Fixture 'owner-ambiguous'; $a=Profile $p 'a' $p.F; $b=Profile $p 'b' $p.F
    Ini $p ((Entry 'a' $true $true)+(Entry 'b' $true $true)+"[InstallA]`r`nDefault=Profiles/a`r`n[InstallB]`r`nDefault=Profiles/b`r`n")
    Manual (Discover $p '')
}
Test 'Stale mapped defaults do not choose unrelated registered profiles' {
    $p=Fixture 'stale'; $a=Profile $p 'available' $p.Other
    Ini $p ((Entry 'available' $true $true)+"[InstallINSTALLED]`r`nDefault=Profiles/deleted`r`n")
    $r=Discover $p; Manual $r; Assert ($r.Profiles.Count -eq 1 -and $r.Profiles.Contains($a)) 'Manual valid profile lost'
}
Test 'Malformed registration and unregistered mapped folder do not select a wrong profile' {
    $p=Fixture 'malformed'; $a=Profile $p 'valid' $p.Other; $unregistered=Profile $p 'unregistered' $p.F
    Ini $p ((Entry 'valid')+"[ProfileBad]`r`nIsRelative=1`r`nPath=invalid|path`r`n[InstallINSTALLED]`r`nDefault=Profiles/unregistered`r`n")
    $r=Discover $p; Manual $r; Assert ($r.Profiles.Count -eq 1 -and $r.Profiles.Contains($a)) 'Malformed/unregistered folder entered choices'
}
Test 'Case and normalized paths match hash registrations and LastAppDir browser ownership' {
    $p=Fixture 'normalized'; $z=Profile $p 'CaseProfile'; $a=Profile $p 'other' $p.Other
    Put (Join-Path $z 'compatibility.ini') ("[Compatibility]`r`nLastAppDir="+(Join-Path $p.F 'browser').ToUpperInvariant()+"\`r`n")
    Ini $p ((Entry 'CaseProfile')+(Entry 'other')+"[Installinstalled]`r`nDefault=Profiles/./caseprofile`r`n")
    Recommended (Discover $p 'INSTALLED') $z
    Ini $p ((Entry 'CaseProfile')+(Entry 'other')); Recommended (Discover $p '') $z
    Put (Join-Path $z 'compatibility.ini') ("[Compatibility]`r`nLastPlatformDir="+$p.Other+"`r`nLastAppDir="+(Join-Path $p.F 'browser')+"`r`n")
    Manual (Discover $p '')
}
Test 'Missing registration files return no recommendation or guessed folders' {
    $p=Fixture 'missing'; $unused=Profile $p 'abandoned' $p.F
    $r=Discover $p; Manual $r; Assert ($r.Profiles.Count -eq 0) 'Abandoned folder guessed'
}
Test 'Only registered existing initialized profiles appear once in manual choices' {
    $p=Fixture 'initialized'; $a=Profile $p 'valid'; $empty=Join-Path $p.R 'Profiles\empty'; [IO.Directory]::CreateDirectory($empty)|Out-Null; $unused=Profile $p 'unregistered'
    Ini $p ((Entry 'valid')+(Entry $a $false)+(Entry 'empty')+(Entry 'missing'))
    $r=Discover $p; Assert ($r.Profiles.Count -eq 1 -and $r.Profiles.Contains($a)) 'Profile list contains stale/empty/duplicate/unregistered entries'
}
Write-Output "PROFILE DISCOVERY RESULT passed=$script:passed failed=$script:failed"
Write-Output "Fixtures retained: $root"
if($script:failed) {exit 1}
