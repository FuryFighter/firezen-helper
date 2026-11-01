$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-Additional.ps1')

Test 'Borderless option writes the default and removes it on restore' {
    $p = Fixture 'borderless-new'
    $path = Join-Path $p.F ([FireZenHelper.Engine]::BorderPreferencePath)
    $backup = [FireZenHelper.Engine]::Apply($p.Z,$p.F,$p.B,$false,$true,$null)
    Assert ((Read $path).Contains('pref("zen.theme.content-element-separation", 0);')) 'Missing default'
    Assert (!(Read $path).Contains('border-radius')) 'Rounded corners were changed'
    $m = (Read (Join-Path $backup 'manifest.json')) | ConvertFrom-Json
    Assert $m.BorderlessDefault 'Manifest did not record selected option'
    Restore $p $backup
    Assert (!(Test-Path $path)) 'Introduced preference file remains after restore'
    VerifyOriginal $p
}
Test 'Existing helper defaults are backed up and exactly restored' {
    $p = Fixture 'borderless-existing'
    $path = Join-Path $p.F ([FireZenHelper.Engine]::BorderPreferencePath)
    $original = "// Prior defaults`r`npref(`"zen.theme.content-element-separation`", 5);"
    Put $path $original
    $backup = [FireZenHelper.Engine]::Apply($p.Z,$p.F,$p.B,$true,$true,$null)
    Assert ((Read $path).Contains('pref("zen.theme.content-element-separation", 0);')) 'Default not changed'
    Restore $p $backup
    Assert ((Read $path) -ceq $original) 'Original defaults were not exactly restored'
}
Test 'Unchecked borderless option preserves existing settings' {
    $p = Fixture 'borderless-off'
    $path = Join-Path $p.F ([FireZenHelper.Engine]::BorderPreferencePath)
    Put $path '// existing helper defaults'
    $backup = [FireZenHelper.Engine]::Apply($p.Z,$p.F,$p.B,$false,$false,$null)
    Assert ((Read $path) -ceq '// existing helper defaults') 'Unchecked option changed defaults'
    Restore $p $backup
    Assert ((Read $path) -ceq '// existing helper defaults') 'Restore changed an unrelated default'
}
Test 'Failure after default installation rolls back all files' {
    $p = Fixture 'borderless-rollback'
    $callback = [Action[FireZenHelper.ProgressUpdate]] { param($u)
        if ($u.Stage -eq 'AppliedFile' -and $u.Message.EndsWith('firezen-helper-defaults.js')) { throw 'Injected default failure' }
    }
    Reject { [FireZenHelper.Engine]::Apply($p.Z,$p.F,$p.B,$false,$true,$callback) } 'Any changed files were restored'
    Assert (!(Test-Path (Join-Path $p.F ([FireZenHelper.Engine]::BorderPreferencePath)))) 'Failed default remains'
    VerifyOriginal $p
}

Write-Output "BORDERLESS RESULT passed=$script:passed failed=$script:failed skipped=$script:skipped"
if ($script:failed) { exit 1 }
