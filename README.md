# FireZEN Helper for Windows

Reapply Zen's interface to an existing Firefox installation using the file-copy method in [CosmoCreeper's FireZEN guide](https://github.com/CosmoCreeper/FireZen#installation).

## Which button should I use?

**Apply FireZEN does not install Sine.** The two actions are independent:

| Action | What it does |
| --- | --- |
| **Apply FireZEN** | Copies Zen's browser files into Firefox and applies the selected borderless/update options. |
| **Sine Mods > Install / repair Sine** | Installs or repairs Sine in the selected Firefox profile, and optionally installs the FireZEN visual-fix mod. |

For a fresh setup: close both browsers, apply FireZEN, open Sine Mods, select your Firefox profile, install Sine with the FireZEN mod option enabled, and restart Firefox. You can complete both operations before reopening the browser.

After a Firefox update: first check that Zen and Firefox have matching engine versions, then reapply FireZEN. If Sine and the FireZEN mod still work, there is no need to reinstall them. Use Sine Mods to repair Sine when its settings tab or mods have stopped loading.

## Open the app

1. Extract the entire ZIP to a normal folder. Keep all extracted files together.
2. Double-click **Run FireZEN.cmd**.
3. Check the detected Zen and Firefox folders. You can select another detected installation from the lists, type a path, or click **Browse...**. Select installation folders containing `zen.exe` / `firefox.exe`, rather than profile folders.
4. Close **all Firefox and Zen windows**, including background processes.
5. Leave **Use borderless layout by default** checked to remove the outer content gap. Click **Check setup** if you changed any options, then **Apply FireZEN**. Windows may ask for administrator access and reopen the app; click Apply again in the reopened window. Review the destination and confirm.
6. Wait for the success message in the window before reopening Firefox.

To install or repair Sine and the FireZEN visual-fix mod, use **Sine Mods** in the helper. See the steps below. This can also be run separately without repeating the FireZEN file copy.

Requires Windows 10/11 with Windows PowerShell 5.1 and .NET Framework 4.8, normally included in Windows. Python, Node.js, PowerShell 7 and downloaded modules are not required. The launcher uses a process-only script-policy override; it does not change the computer's execution-policy setting. The source is included for inspection.

## What it changes

The app checks that both browsers use the **same exact Firefox engine version and CPU architecture**, then copies Zen's `browser` folder contents, `xul.dll` and `omni.ja` into Firefox. Folder copying merges files, matching Windows Explorer's overwrite behavior. Firefox-only files are retained.

The FireZEN copy leaves `firefox.exe`, the other root-level Firefox components, and browser profile data in place. It does not transfer bookmarks, passwords, cookies, extensions, preferences or history. The optional Sine installer separately changes Sine's loader, the selected profile's Sine/mod files and a startup-cache refresh flag. Like the underlying experimental FireZEN project, this helper cannot guarantee UI compatibility or DRM playback for every release/service.

Before replacing files, the app stages the source and saves every destination file it will overwrite. SHA-256 checks verify the staged files, originals, replacements and restored files. If applying fails, it attempts to restore changed files automatically. It refuses linked folders, incompatible builds, or operations while either browser is running. It never forces a browser to close.

## Borderless layout

**Use borderless layout by default** is checked on first launch. It adds `browser/defaults/preferences/firezen-helper-defaults.js` in Firefox with:

```js
pref("zen.theme.content-element-separation", 0);
```

This removes Zen's default 8-pixel outer content gap. Rounded-corner settings are unchanged. The file is included in the same backup, verification, rollback and restore operation as the copied browser files. No profile files are edited.

An explicit profile setting in `about:config` or `user.js` takes precedence over this default. If a frame remains, open `about:config`, search for `zen.theme.content-element-separation`, and set the number to **0**. Restart Firefox if necessary. The setting also controls spacing between some Zen UI elements.

Unchecking the option leaves an existing helper defaults file in place. To undo an earlier default, restore the backup that introduced it, remove `browser/defaults/preferences/firezen-helper-defaults.js`, or choose another value in `about:config`.

## Sine and the FireZEN mod

The helper now automates [Sine's documented ZIP installation method](https://github.com/sineorg/docs/blob/main/src/installation.md):

1. Select the Firefox installation in the main window and click **Sine Mods**.
2. Check the detected **Firefox profile**. Select another from the list, paste its path, or use Browse. Firefox's `about:support` page shows the active Profile Folder. Start Firefox once before installing Sine if it has never created a profile.
3. Leave **Also install the FireZEN visual-fix mod** checked to install `CosmoCreeper/FireZen` along with Sine. Uncheck it to install or repair only Sine.
4. Close Firefox and Zen, then click **Install / repair Sine**. Windows may reopen the helper with administrator access, preserving the chosen profile and option; click Install again and review the destinations.
5. Wait for the success message, then restart Firefox normally. Its startup cache will refresh automatically. If you selected a different profile from your usual one, use **Copy Firefox launch**, press **Win+R**, paste and press Enter.
6. Open **Settings > Sine** to manage your mods. If you previously disabled all Sine mods, re-enable them there.

The helper downloads Sine **2.3.3**, its matching bootloader **0.1.4**, and optionally a pinned FireZEN mod snapshot. Every download must match its recorded SHA-256 fingerprint before installation. Existing valid downloads are reused from `<backup folder>/SineDownloads/2.3.3`. No third-party installer executable is run, and the package does not bundle third-party code.

Installation adds the official `config.js` and `defaults/pref/config-prefs.js` to Firefox, extracts Sine into the chosen profile's `chrome/JS` and `chrome/utils` folders, and merges FireZEN into `chrome/sine-mods/mods.json`. Existing mods and their files are retained; duplicate entries for the same FireZEN repository are consolidated using the upstream mod ID. Existing files under JS/utils that are not in the official packages are retained. The helper stops on malformed mod metadata or a conflicting AutoConfig loader rather than replacing an unrelated configuration.

It also sets `InvalidateCaches=1` under `[Compatibility]` in the selected profile's `compatibility.ini`, preserving other keys/sections. Firefox recognizes this flag on the next normal startup. No cookie, password, history, `prefs.js` or `user.js` file is changed by the installer.

Sine operations have a separate verified backup under `<backup folder>/Sine`. **Restore Sine backup** in the Sine window restores the latest applied or interrupted operation for the selected Firefox installation and profile. It restores overwritten files and removes introduced files, including the changed registry and cache flag. **After restoring, use Copy Firefox launch and paste into Win+R** to refresh caches when opening the restored profile. Reinstalling Sine after a Firefox update can repair its loader without repeating the main browser-file transformation.

The tested versions are deliberately pinned; the helper does not silently adopt future installer layouts. Sine's own update and mod-management features remain available inside Firefox. This installation step does not diagnose Netflix sign-in problems or establish DRM compatibility.

## Updates

**Pause Firefox updates** is optional and off by default. When selected, it sets Mozilla's `DisableAppUpdate` policy in Firefox's `distribution/policies.json`, preserving unrelated policy entries. Invalid existing JSON is rejected before files are changed.

Pausing stops **security updates too**. Plan to update Firefox and Zen together and reapply when their engine versions match. Leaving the box unchecked preserves existing update settings; it does not remove an earlier pause. Restoring a backup that introduced the pause returns that policy file to its previous state. Alternatively, remove only the `DisableAppUpdate` entry from `distribution/policies.json` with administrator rights, then restart Firefox; check `about:policies` to confirm. Device-wide administrator policies may also control updates.

See [Mozilla's update policy reference](https://firefox-admin-docs.mozilla.org/reference/policies/disableappupdate/).

## Backups and restore

The default backup folder is `%LOCALAPPDATA%\FireZenHelper\Backups`. You can choose a different folder outside both browser installations before applying. Allow roughly 1 GB of free space per operation for typical installations; the actual requirement is checked before copying. Backups include staged Zen files as well as the original Firefox files, so repeated operations use additional space.

**Restore latest backup** selects the latest applied or interrupted backup for the chosen Firefox folder. Close both browsers first. Restore returns modified files, any borderless defaults file and any changed update policy to their saved state, removes files introduced by that operation, and retains unrelated files. Empty created directories can remain. The backup's `manifest.json` records its installation, checksums, status and interrupted-operation progress.

Restore is deliberately blocked if `firefox.exe` has changed since the backup, such as after a Firefox update: old engine files must not be mixed with a newer executable. In that situation, install matching browser versions and reapply, or reinstall standard Firefox. Do not force an old backup into a newly updated installation.

If an interrupted operation reports that automatic restore could not finish, keep Firefox closed, keep the backup, and retry Restore latest backup. Do not delete backups while an operation or recovery is in progress.

## Files

- `Run FireZEN.cmd`: double-click launcher.
- `FireZen.ps1`: opens the Windows interface and loads the included source.
- `FireZen.Core.cs`: detection, checks, copy transactions and restore.
- `FireZen.UI.cs`: Windows interface.
- `FireZen.Sine.cs`: official downloads, profile detection, Sine/mod installation and restore.
- `FireZen.SineUI.cs`: Sine installation window.
- `README.md`: this guide.
- `LICENSE.txt`: license for the helper.
- `TESTING.md`: validation workflow and testing limits.
- `Run-Tests.ps1` and `tests/`: repeatable Windows fixture tests.

This is an independent helper, not an official Mozilla, Zen or FireZEN release. The FireZEN method and Sine are by CosmoCreeper and their contributors. Their respective projects retain their own licenses. No browser binaries or third-party mod code are bundled.

## Development

Use Windows PowerShell 5.1. Run `./Run-Tests.ps1` to compile the complete helper and exercise 36 temporary-folder checks. The test runner downloads the pinned official Sine packages into the ignored `work/` folder; no third-party JavaScript is executed. See [TESTING.md](TESTING.md).
