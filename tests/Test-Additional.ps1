$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-FireZen.ps1') -IsolateProcessGuard

Test 'LatestBackup uses recorded chronology rather than random folder suffix' {
    $p = Fixture 'latest-order'
    $old = @{ Format=1; FirefoxPath=$p.F; Status='Applied'; CreatedUtc='2026-10-31T10:00:00.0000000Z' }
    $new = @{ Format=1; FirefoxPath=$p.F; Status='Applied'; CreatedUtc='2026-10-31T10:00:00.9000000Z' }
    Put (Join-Path $p.B 'zz-old\manifest.json') ($old | ConvertTo-Json)
    Put (Join-Path $p.B 'aa-new\manifest.json') ($new | ConvertTo-Json)
    Assert ([FireZenHelper.Engine]::LatestBackup($p.B,$p.F) -eq (Join-Path $p.B 'aa-new')) 'Selected an older backup'
}
Test 'LatestBackup skips incomplete manifest metadata' {
    $p = Fixture 'metadata-null'
    Put (Join-Path $p.B 'zz-invalid\manifest.json') '{"Status":"Applied","FirefoxPath":null}'
    Assert ($null -eq [FireZenHelper.Engine]::LatestBackup($p.B,$p.F)) 'Incomplete manifest was selected'
}
Test 'Source changes during staging reject before transformation' {
    $p = Fixture 'changed-source'
    $callback = [Action[FireZenHelper.ProgressUpdate]] { param($u)
        if ($u.Stage -eq 'BackingUp' -and $u.Message.EndsWith('xul.dll')) {
            Put (Join-Path $p.Z 'browser\omni.ja') 'source updated mid-operation'
        }
    }
    Reject { Apply $p $false $callback } 'Zen changed during backup'
    VerifyOriginal $p
}
Test 'Backup cannot be inside a browser installation' {
    $p = Fixture 'backup-overlap'
    $p.B = Join-Path $p.F 'Backups'
    Reject { Apply $p } 'outside both browser'
    VerifyOriginal $p
    Assert (!(Test-Path $p.B)) 'Overlapping backup folder was created'
}
Test 'Junction source folder is rejected without following it' {
    $p = Fixture 'junction'
    $linked = Join-Path ([IO.Path]::GetDirectoryName($p.Z)) 'LinkedAssets'
    Put (Join-Path $linked 'asset.txt') 'must remain unchanged'
    New-Item -ItemType Junction -Path (Join-Path $p.Z 'browser\linked') -Target $linked | Out-Null
    Reject { [FireZenHelper.Engine]::Inspect($p.Z,$p.F) } 'symbolic link or junction'
    Assert ((Read (Join-Path $linked 'asset.txt')) -eq 'must remain unchanged') 'Junction target changed'
}
Test 'Executable paths with spaces are normalized to install folders' {
    $p = Fixture 'paths with spaces'
    $r = [FireZenHelper.Engine]::Inspect((Join-Path $p.Z 'zen.exe'),(Join-Path $p.F 'firefox.exe'))
    Assert ($r.Firefox.Path -eq $p.F) 'Firefox executable path not normalized'
    Assert ($r.Zen.Path -eq $p.Z) 'Zen executable path not normalized'
}

Write-Output "FINAL RESULT passed=$script:passed failed=$script:failed skipped=$script:skipped"
if ($script:failed) { exit 1 }
