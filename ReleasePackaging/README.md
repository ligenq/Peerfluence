# Velopack release packaging

Peerfluence uses Velopack for direct-download installation and updates.

## Prerequisites

Install the Velopack CLI:

```powershell
dotnet tool install -g vpk
```

Confirm it is available:

```powershell
vpk pack --help
```

## Build a release

From the repo root:

```powershell
.\ReleasePackaging\build-velopack.ps1 -Version 1.0.0
```

The script publishes Peerfluence for `win-x64`, then runs `vpk pack`.

Output is written to:

```text
artifacts\velopack\releases\win-x64
```

The important user-facing installer is:

```text
Peerfluence.Desktop-win-Setup.exe
```

The default Velopack package ID is `Peerfluence.Desktop`. Keep this stable once a build has been shared, because Velopack uses it for the install identity and update continuity. The visible app title remains `Peerfluence`.

Upload the full contents of the release output directory to the update host. Do not upload only the setup exe, because the update feed files and package files are required for automatic updates.

Velopack will refuse to create a release if the output directory already contains an equal or newer version for the same channel. For local rebuilds of the same version, use:

```powershell
.\ReleasePackaging\build-velopack.ps1 -Version 1.0.0 -CleanReleaseOutput
```

## Optional arguments

```powershell
.\ReleasePackaging\build-velopack.ps1 `
  -Version 1.0.1 `
  -RuntimeIdentifier win-x64 `
  -Channel win `
  -ReleaseNotes .\release-notes.md
```

Generate an MSI bootstrapper as well:

```powershell
.\ReleasePackaging\build-velopack.ps1 -Version 1.0.1 -Msi
```

## Signing

The current release flow intentionally creates an unsigned installer. Build it without signing arguments:

```powershell
.\ReleasePackaging\build-velopack.ps1 -Version 1.0.0 -CleanReleaseOutput
```

Unsigned installers work, but Windows can show SmartScreen or unknown-publisher warnings. That is acceptable for early releases and trusted testers. Revisit signing when Peerfluence is ready for broader public distribution.

The script still supports signing options for later use.

For local testing, the script can create and trust a self-signed code-signing certificate:

```powershell
.\ReleasePackaging\build-velopack.ps1 `
  -Version 1.0.0 `
  -CleanReleaseOutput `
  -CreateTestCertificate `
  -TrustTestCertificate
```

This signs the generated app files and setup bundle with a local `CN=ligenq` certificate. It is useful for local install verification, but it is not a substitute for a public code-signing certificate. Public users can still see SmartScreen warnings for self-signed builds.

Use `-SignParams` for `signtool.exe` parameters:

```powershell
.\ReleasePackaging\build-velopack.ps1 `
  -Version 1.0.1 `
  -SignParams "/fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f C:\certs\peerfluence.pfx"
```

Or use `-SignTemplate` for a custom signing command. Velopack replaces `{{file}}` with the file being signed.

## GitHub Releases update feed

Peerfluence is configured to use GitHub Releases from:

```text
https://github.com/ligenq/Peerfluence
```

For each public release:

1. Build the Velopack release.
2. Create a GitHub release on `ligenq/Peerfluence`.
3. Attach every file from `artifacts\velopack\releases\win-x64`, not just the setup exe.
4. Publish the GitHub release.

Users download `Peerfluence.Desktop-win-Setup.exe`. Installed copies check the same GitHub repository for later releases and download the Velopack package assets from there.

The Settings update URL can still point to another Velopack-compatible source. If a GitHub release page URL is pasted, Peerfluence normalizes it back to the repository URL before checking for updates.
