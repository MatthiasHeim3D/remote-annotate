# Windows client deployment

Remote Annotate is packaged as a self-contained x64 application in an Inno Setup installer that can install for one user or for the whole machine. Setup asks which on its first page and defaults to the current user, so the normal path writes to `%LocalAppData%\Programs\Remote Annotate`, creates a current-user Start menu shortcut, and never requests administrator rights. Choosing **Install for all users** triggers a UAC prompt and installs to `%ProgramFiles%\Remote Annotate` with an all-users Start menu shortcut.

Either way the client's own data stays per-user under `%LocalAppData%\RemoteAnnotate` — settings, client identity, DPAPI-protected credentials, calibrations, and audit logs. An all-users install therefore shares only the program files: each account still gets its own first-run setup, its own relay address and server password, and its own "Launch at startup" registration under `HKCU`.

The relay URL is not built into the installer. On first launch, the client opens
Settings and asks the user to enter the HTTPS relay address. Tell users the server
password for your relay at the same time: they enter it in the same screen, and by
default a relay refuses clients that have none. See
[server-deployment.md](server-deployment.md#server-passwords) for what the password
does. If the relay's HTTPS
certificate chains to a publicly trusted CA (for example, a hostname fronted by a
Cloudflare Tunnel), omit `-RelayRootCertificatePath` — Windows already trusts that
certificate and no root needs installing:

```powershell
.\build\Build-Installer.ps1
```

If the relay instead uses Caddy's private CA (see [server-deployment.md](server-deployment.md)), export its root certificate and pass it so the installer can trust it:

```powershell
.\build\Build-Installer.ps1 `
  -RelayRootCertificatePath .\relay-root.crt
```

Build prerequisites are the .NET 10 SDK and Inno Setup 6. Nerdbank.GitVersioning calculates the installer version from the repository's shared root `version.json`; no version argument is required. The output is:

```text
artifacts\installer\RemoteAnnotate.Client-<version>-x64-Setup.exe
artifacts\installer\RemoteAnnotate.Client-<version>-x64-Setup.exe.sha256
```

No MSI, WiX, signing certificate, machine configuration, service, driver, or inbound firewall rule is involved. Because the installer is intentionally unsigned, distribute it and its SHA-256 file from a restricted internal share or another authenticated internal channel.

## Publish a release

Normal branch pushes do not publish anything. To release, open **Actions → Release → Run workflow** on GitHub, leave the commit empty to release the tip of `main` or paste the SHA of an earlier commit on `main`, and run it. The workflow uses `nbgv tag` to calculate that commit's version tag (for example, `v1.0.14`), refuses a commit that is not on `main` or a tag that already exists, pushes the tag, and then builds both outputs for it:

- **Relay image:** the **Publish relay image** workflow verifies the tag against Nerdbank.GitVersioning and publishes the versioned relay image and `latest` to GitHub Container Registry.
- **Client release:** the **Publish client release** workflow runs on a GitHub-hosted Windows runner. It builds and smoke-tests the installer and the portable zip (`build/Build-Portable.ps1`, `build/Test-Portable.ps1`), and creates a **draft** GitHub release with the setup exe, the zip and their `.sha256` files attached. Review the generated notes and assets, then publish the draft from the Releases page.

To embed a private-CA relay root in the installer, store the base64 of the public `.crt` in the repository secret `RELAY_ROOT_CERTIFICATE`; leave it unset for a publicly trusted relay. The build is unsigned, like a local one. Pushing a `v*` tag by hand (`dotnet nbgv tag HEAD`, then `git push origin <tag>`) still starts both workflows directly.

## Install

Run the setup executable as the user who will use Remote Annotate and pick an install mode on the first page. Pick **Install for me only** unless you are setting up a shared PC and hold local administrator rights; it is the preselected option and needs no elevation.

If the installer was built with `-RelayRootCertificatePath`, leave the HTTPS certificate task selected — it adds only Caddy's **public** root certificate; the CA private key never leaves the Docker server. The store follows the install mode: a per-user install writes to `Cert:\CurrentUser\Root`, an all-users install writes to `Cert:\LocalMachine\Root` so every account on the PC trusts the relay. Installers built without that flag (public-CA relay hostnames) have no certificate task at all, since Windows already trusts the relay's certificate chain.

The client uses normal Windows certificate validation and still refuses non-HTTPS relay URLs. Changing the relay hostname is done in the client's Settings. Replacing Caddy's data volume for a Caddy-fronted relay requires exporting the new root and rebuilding the installer.

For a quiet current-user install:

```powershell
.\RemoteAnnotate.Client-1.0.0-x64-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CURRENTUSER
```

For a quiet machine-wide install, run the same command with `/ALLUSERS` from an already elevated session — silent setup cannot show a UAC prompt:

```powershell
.\RemoteAnnotate.Client-1.0.0-x64-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /ALLUSERS
```

Uninstall from Windows Settings, or run the uninstaller from wherever the install landed:

```powershell
& "$env:LOCALAPPDATA\Programs\Remote Annotate\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
& "$env:ProgramFiles\Remote Annotate\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

An interactive uninstall asks whether to also delete `%LocalAppData%\RemoteAnnotate` (saved settings, profile picture cache, audit and protected recovery data); answering No, or uninstalling silently (`/SUPPRESSMSGBOXES`), leaves it in place so another installed version keeps working. Because that data and the `HKCU` startup registration are per-account, uninstalling an all-users install only clears them for the account running the uninstaller; other accounts keep their own copies, and their startup entries simply stop resolving. The uninstaller never removes the trusted relay root from either certificate store — remove that manually, and only after no internal service depends on it.

## Portable (no-installer) build

The release workflow above builds this zip and attaches it, with its `.sha256`, to the draft release. For locked-down PCs, one-off support sessions, or running from a USB stick or network share, you can also build a zip of the same self-contained publish locally instead of using the installer. It needs no setup, no administrator rights, and no .NET install on the target PC:

```powershell
.\build\Build-Portable.ps1
```

```text
artifacts\portable\RemoteAnnotate.Client-<version>-x64-Portable.zip
artifacts\portable\RemoteAnnotate.Client-<version>-x64-Portable.zip.sha256
```

The zip holds one `RemoteAnnotate` folder. Extract it anywhere writable (not into a protected folder you cannot launch from) and run `RemoteAnnotate.Client.exe`. A single-file exe is not offered: WPF with WinForms interop still ships native libraries beside the exe, so a folder in a zip is the supported form. Only the .NET 10 SDK is needed to build it; Inno Setup is not.

How the installer's extras map to the portable case:

- **Relay address:** not built in, same as the installer. Users enter it and the server password on first launch. To save them typing, put a copy of `appsettings.json` with `Server:BaseUrl` set into the extracted folder before handing it out; the client reads `appsettings.json` next to the exe. Never put the server password in it.
- **Relay root certificate:** the portable build does not trust a private CA. For a relay on a publicly trusted CA nothing is needed. For a Caddy private-CA relay, a user (or admin) must first import `root.crt` themselves, for example `certutil -user -addstore Root relay-root.crt`, which shows a Windows confirmation prompt. The client still refuses non-HTTPS relay URLs and uses normal Windows certificate validation.
- **Data location:** unchanged. Settings, DPAPI-protected credentials, calibrations, and audit logs stay in `%LocalAppData%\RemoteAnnotate` on each PC, never next to the exe. Credentials are bound to the Windows account and would not travel between PCs anyway.
- **Launch at startup:** still available. It registers the exe's current path under `HKCU`, so if you move or delete the folder the entry dangles; turn the option off before removing the app.
- **Single instance:** the portable and installed builds use the same single-instance guard, so starting one while the other runs just activates the running copy.
- **Update:** extract the new zip over the old folder, or into a new one.
- **Remove:** turn off "Launch at startup", delete the folder, and optionally delete `%LocalAppData%\RemoteAnnotate`. A relay root you imported by hand stays until you remove it from the certificate store.
- **SmartScreen and Mark of the Web:** the exe is unsigned, like the installer. A zip downloaded through a browser marks the extracted files as from the internet and may show a SmartScreen warning. Verify the SHA-256 file, then right-click the zip, choose Properties, and tick **Unblock** before extracting (or run `Unblock-File` on it). Distribute it over the same restricted internal channel as the installer.

Smoke test, which verifies the hash, extracts, checks for an empty relay URL, and launches the client for ten seconds:

```powershell
.\build\Test-Portable.ps1 `
  -ArchivePath .\artifacts\portable\RemoteAnnotate.Client-1.0.0-x64-Portable.zip
```

## Installer smoke test

The following installs, validates that no relay address is preconfigured, and uninstalls without elevation:

```powershell
.\build\Test-Installer.ps1 `
  -SetupPath .\artifacts\installer\RemoteAnnotate.Client-1.0.0-x64-Setup.exe
```

To cover the machine-wide path, run the same script with `-Scope AllUsers` from an elevated session.
