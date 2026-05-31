# Microsoft Store packaging

This folder contains the MSIX packaging path for Microsoft Store builds.

The Store build must be compiled with:

```powershell
/p:DistributionChannel=MicrosoftStore
```

That switch disables Velopack startup/update behavior and registers the Store-managed update service.

## Build a local MSIX

From the repo root:

```powershell
.\StorePackaging\build-msix.ps1 `
  -PackageName "Peerfluence" `
  -Publisher "CN=Your Partner Center Publisher" `
  -PublisherDisplayName "Your Publisher Name"
```

The generated package is written to `artifacts\store\msix`.

Partner Center provides the final `PackageName` and `Publisher` identity values after the app name is reserved. Use those values for Store submission builds.

## Local signing

The Microsoft Store re-signs submitted packages after certification, but local sideload testing requires a trusted signing certificate. If you have a `.pfx`, pass it to the script:

```powershell
.\StorePackaging\build-msix.ps1 `
  -Publisher "CN=Your Test Certificate Subject" `
  -SignCertificatePath C:\certs\peerfluence-test.pfx
```

## Before submission

Run the Windows App Certification Kit against the generated package, then test install, launch, networking, downloads, notifications, and settings persistence on a clean Windows user profile.
