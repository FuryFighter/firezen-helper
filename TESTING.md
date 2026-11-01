# Validation

On Windows, open Windows PowerShell 5.1 in the repository and run:

```powershell
.\Run-Tests.ps1
```

The runner compiles all four C# files without opening the interface. It downloads and verifies the pinned official Sine archives and FireZEN mod files, then runs all 60 browser/borderless, engine-mode, cache-refresh and Sine fixtures in separate processes. An existing verified download cache is reused. To use another cache, pass `-PackageDirectory 'C:\path\to\verified-packages'`.

The browser/borderless and Sine fixture suites replace only the browser process detector in a test compilation so that synthetic installations can be exercised while real browsers remain open. The engine-mode and cache-refresh suites additionally substitute the Mozilla signature guard with a synthetic engine identity check. These changes exist only in memory; the shipped files retain both production guards. Tests use fake executables, preferences and cookie files under the ignored `work/tests` folder. They never modify a real browser installation or profile and never execute downloaded JavaScript.

## Coverage

- 24 browser/borderless scenarios cover matching versions/architectures, copy/merge, preservation of unrelated files, update-policy handling, backup/restore, source changes, path protections, tampering, rollback and interrupted-operation recovery.
- 12 engine-mode scenarios cover payload counts, preserved engine/executable hashes, option handling, byte-exact restore, reapplication, rollback, rejection of an existing Zen engine, engine mutation during staging, restore refusal after engine changes, legacy manifest compatibility, classic overload behavior, and version/architecture mismatches.
- 12 cache-refresh scenarios cover all selected profiles being flagged before browser replacement, preservation of unrelated compatibility metadata (including `EncryptedDatabases`) and profile data, missing metadata, consumed flags on restore, older backups, locked metadata on apply/restore, rejected engines/sources, unsafe profile paths, junctions and rollback rearming. Cache flags intentionally remain queued after restore or rollback; stale cache metadata is never restored over the current profile state.
- 12 Sine scenarios cover verified packages, fresh installation, FireZEN metadata merging, existing-mod preservation, cache metadata, exact restoration, configuration conflicts, tampering, traversal, profile mismatch and recovery.

## Limits

The synthetic fixtures do not exercise genuine Authenticode signatures or signed binary metadata. Keeping the engine byte-exact also preserves its signature bytes, but these fixtures cannot certify trust validation. Cache-refresh tests verify that the helper queues invalidation; they do not launch Firefox to prove it consumes that flag or rebuilds startup caches. They do not certify DRM playback, website compatibility, Firefox loading Sine at runtime, Windows elevation prompts or protected installation writes. The interface has also been compiled and visually checked on Windows. The helper automates an experimental upstream method; matching engine versions do not guarantee Netflix or other DRM playback.

Separate read-only checks exercised the shipped signature/version/architecture validation against real Firefox 159.0 files: it accepted the original Mozilla engine and a mixed Zen-interface/Mozilla-engine copy, and rejected Zen's engine. Independent fresh-profile headless checks confirmed delayed interface startup, Zen startup readiness and local page loading with the original Mozilla engine. Netflix playback in that separate experimental copy was reported working by the tester. Those checks are not part of the portable synthetic runner, and do not establish compatibility for all sites, Zen features or future builds. Session-startup and missing crash-service errors remain in the mixed copy.

A separate disposable-profile experiment reproduced the stale-interface mismatch by warming stock Firefox's cache, then replacing the interface archives without changing the engine or build identity. Queuing `InvalidateCaches=1` with the shipped metadata builder restored complete Zen startup and local page loading without the `-purgecaches` argument. Real Firefox consumed the queued flag. Separately, the tester confirmed that the installed broken interface recovered when launched with `-purgecaches`. The experiment did not use existing user profiles or account data.

Downloads come from the official Sine/bootloader releases and a pinned FireZEN repository commit. The packages are not redistributed with this repository. See the installer source for version pins and fingerprints.
