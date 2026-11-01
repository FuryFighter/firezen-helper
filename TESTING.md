# Validation

On Windows, open Windows PowerShell 5.1 in the repository and run:

```powershell
.\Run-Tests.ps1
```

The runner compiles all four C# files without opening the interface. It downloads and verifies the pinned official Sine archives and FireZEN mod files, then runs the browser/borderless and Sine test suites in separate processes. An existing verified download cache is reused. To use another cache, pass `-PackageDirectory 'C:\path\to\verified-packages'`.

The fixture suites replace only the browser process detector in a test compilation so that synthetic installations can be exercised while real browsers remain open. The shipped files retain the process guard. Tests use fake executables, preferences and cookie files under the ignored `work/tests` folder. They never modify a real browser installation or profile and never execute downloaded JavaScript.

## Coverage

- 24 browser/borderless scenarios cover matching versions/architectures, copy/merge, preservation of unrelated files, update-policy handling, backup/restore, source changes, path protections, tampering, rollback and interrupted-operation recovery.
- 12 Sine scenarios cover verified packages, fresh installation, FireZEN metadata merging, existing-mod preservation, cache metadata, exact restoration, configuration conflicts, tampering, traversal, profile mismatch and recovery.

## Limits

The fixture tests do not certify DRM playback, website compatibility, Firefox loading Sine at runtime, Windows elevation prompts or protected installation writes. The interface has also been compiled and visually checked on Windows. The helper automates an experimental upstream method; matching engine versions do not guarantee Netflix or other DRM playback.

Downloads come from the official Sine/bootloader releases and a pinned FireZEN repository commit. The packages are not redistributed with this repository. See the installer source for version pins and fingerprints.
