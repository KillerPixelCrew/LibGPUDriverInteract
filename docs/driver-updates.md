# Graphics driver updates

`GpuInstalledDriver.ReadAll()` reads present PCI display adapters and their installed Windows driver
version/date. `GpuDriverUpdateCatalog.CheckAsync(adapter)` selects a compatible package. It performs
bounded HTTPS requests and follows redirects only to the explicitly allowed official hosts. A missing
or changed catalog, ambiguous product match, unsupported adapter or unknown version is a refusal,
never an up-to-date result.

| Vendor | Official source and matching | Integrity | Installer |
| --- | --- | --- | --- |
| Intel | DSA's `https://dsadata.intel.com/data/en` ZIP, `software-configurations.json`; Graphics component PCI detection values, Windows x64 file, no beta or OEM-only board/model package | Catalog byte size and published `Hash`, currently SHA1; Authenticode `Intel Corporation` | Extracted `Installer.exe -s`; no reboot, overwrite, clean-install, unsigned or process-termination switch |
| AMD | AMD's driver selector's embedded product JSON, exact normalized product name, that product's Adrenalin download article and release notes | Official download server's exact byte size and Authenticode `Advanced Micro Devices` (including its older legal-name certificates); this source publishes no package checksum | Extracted `Setup.exe -INSTALL -LOG <private-result-file>`; parse both overall and package result codes, including partial failure and reboot |
| NVIDIA | Official `lookupValueSearch.aspx` product type/series/product lists, exact normalized adapter name; `gfwsl.geforce.com` DriverManualLookup with product/series, Windows OS, WHQL and DCH | Official download server's exact byte size and Authenticode `NVIDIA Corporation`; this source publishes no package checksum | Extracted `setup.exe -s -n Display.Driver`; exit 0 succeeds, 1 succeeds with a restart, other codes fail |

The DSA feed supplies a SHA1 hash rather than the SHA512 shown on Intel's HTML Download Center pages.
The library verifies the checksum its official machine-readable source actually publishes. Its hash
reader supports SHA1, SHA256 and SHA512. Authenticode independently verifies the signature, trusted
chain, timestamp and chain revocation for every vendor. The downloaded file is held against writes
and deletion while its checksum/signature are verified, and verified again immediately before use.
The extracted installer must also carry the expected vendor's valid signature. Packages and their
extracted payloads stay under an administrator-protected Program Files directory.

Extraction reads the self-extracting package as a 7-Zip archive; the download stub is never executed.
Paths, links and expanded sizes are checked. `InstallAsync` calls the host's admission callback
after preparation and immediately before process creation. The host owns explicit confirmation,
AC/battery/game/OEM checks, recording the previous version and releasing/recreating driver controls.
Cancellation stops preparation; a started installer is awaited without cancellation or automatic
retry. Its temporary extraction directory is removed after it exits. The host removes the download.

Intel's packaged `installation_readme.txt` documents exit 14 as success requiring a reboot, 18 as
success requiring shutdown/power-on, 13 as a failed attempt requiring a reboot first, and 1/5–11/15/16
as failures. The library preserves that distinction. AMD's result log can report package failure even
when its launcher exits successfully; that is never reported as a successful installation.

Consumers must honor handheld/OEM restrictions before selecting a generic package. AMD's Adrenalin
release notes explicitly exclude handheld gaming devices. LibHandheld records this and the
model-specific OEM source; WSGM declines those generic AMD updates with the reason shown in both UIs.

Sources: [Intel command line](https://www.intel.com/content/www/us/en/support/articles/000006773/graphics.html),
[Intel packaged readme](https://www.intel.com/content/www/us/en/support/articles/000098051/graphics.html),
[AMD installation](https://www.amd.com/en/resources/support-articles/faqs/RSX2-Install.html),
[AMD command line](https://drivers.amd.com/relnotes/command-line-installation.pdf),
[NVIDIA silent install](https://docs.nvidia.com/datacenter/tesla/driver-installation-guide/windows.html).

These endpoints are public vendor update/download sources. No vendor application is installed,
account emulated, payload redistributed or undocumented OEM bypass attempted. The host presents the
official license/details URL and requires explicit acceptance before downloading/installing. No
claim is made that these public endpoints carry a separately supported third-party API contract.
