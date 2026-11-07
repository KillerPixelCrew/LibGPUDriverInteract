# Graphics driver updates

`GpuInstalledDriver.ReadAll()` reads present PCI display adapters and their installed Windows driver
version/date. `GpuDriverUpdateCatalog.CheckAsync(adapter, includeNonWhql: false)` selects a compatible package. It performs
bounded HTTPS requests and follows redirects only to the explicitly allowed official hosts. A missing
or changed catalog, ambiguous product match, unsupported adapter or unknown version is a refusal,
never an up-to-date result.

| Vendor | Official source and matching | Integrity | Installer |
| --- | --- | --- | --- |
| Intel | DSA's `https://dsadata.intel.com/data/en` ZIP, `software-configurations.json`; Graphics component PCI detection values, Windows x64 file, no OEM-only board/model package; its matching Download Center product page supplies the current release, including non-WHQL when opted in | Official server byte size and Download Center SHA512; Authenticode `Intel Corporation` | Extracted `Installer.exe -s`; no reboot, overwrite, clean-install, unsigned or process-termination switch |
| AMD | AMD's driver selector's embedded product JSON, exact normalized product name, that product's Adrenalin download article and release notes | Official download server's exact byte size and Authenticode `Advanced Micro Devices` (including its older legal-name certificates); this source publishes no package checksum | Extracted `Setup.exe -INSTALL -LOG <private-result-file>`; parse both overall and package result codes, including partial failure and reboot |
| NVIDIA | Official `lookupValueSearch.aspx` product type/series/product lists, exact normalized adapter name; `gfwsl.geforce.com` DriverManualLookup with product/series, Windows OS and DCH; WHQL-only by default, official all-release/beta lookup when opted in | Official download server's exact byte size and Authenticode `NVIDIA Corporation`; this source publishes no package checksum | Extracted `setup.exe -s -n Display.Driver`; exit 0 succeeds, 1 succeeds with a restart, other codes fail |

DSA is used for adapter/OS/branch applicability, not as the final release-version authority: its
snapshot can lag Download Center. The selected product page's unpinned URL supplies `DownloadVersion`,
`RecommendedDownloadUrl`, `LastUpdate`, release-notes certification and SHA512. A blocked US page is
retried through Intel's official `www.intel.cn` regional mirror with the same downloadmirror payload.
A newer version-selector entry is checked when non-WHQL is enabled; when the current release is
non-WHQL and the option is off, the certified version-selector entry or DSA-pinned page is used.
Unknown certification or a missing SHA512 is a refusal. The DSA PCI match remains mandatory, so an
11th–14th Gen UHD adapter is never moved to the Arc/Core Ultra branch merely for a larger version.

`GpuDriverPackage.IsWhql` and `Certification` carry the vendor's status through confirmation identity
and UI. Intel's release-notes filename distinguishes `_WHQL` from `_Non-WHQL`; AMD's product articles
label WHQL independently of Recommended/Optional (Optional may itself be WHQL). NVIDIA's official
`DriverManualLookup` supports all releases with `isWHQL=0`; its `IsBeta`/`IsWHQL` fields and WHQL file
name determine certification. Studio records are not substituted for Game Ready by enabling beta.
When the current adapter catalog lists no newer non-WHQL release, the result says so. The switch does
not enable unsigned packages, unofficial mirrors, automatic installs or relaxed integrity checks.

The hash reader supports SHA1, SHA256 and SHA512, including omitted leading zeroes at each width.
It hashes bytes as they are written and uses WindowsDeviceControl's shared Authenticode primitive
with chain revocation and the vendor's expected signer names. `DownloadAsync` returns a
`GpuDriverDownload` retaining the same file handle against writers/deletion through `InstallAsync`;
the package is not reopened, rehashed or reverified between those operations. The extracted installer
is a different file and must independently carry the expected vendor's valid signature. The caller
supplies the administrator-protected root for downloads and extraction.

Extraction opens a non-owning seekable window over the held file at its embedded 7-Zip/RAR archive;
it makes no second archive copy. Candidates in the PE stub are parsed as archives and must contain
the documented root installer before extraction is admitted. An entry named `payload.7z` cannot
collide with staging, because no such staging file exists. The download stub is never executed.
Paths, links and declared/actual expanded sizes are checked. `InstallAsync` calls the host's admission callback
after preparation and immediately before process creation. The host owns explicit confirmation,
AC/battery/game/OEM checks, recording the previous version and releasing/recreating driver controls.
Cancellation stops preparation; a started installer is awaited without cancellation or automatic
retry. Its temporary extraction directory is removed after it exits. The host disposes the held
download before removing the temporary package. Package metadata is an immutable public record with
a stable identity for confirmation; vendor-specific comparison, executable, arguments, signer names
and result-code policy live in the vendor profile.

Installed Windows dates use the registry's exact month-day-year format. NVIDIA's lookup date uses
the English `ddd MMM dd, yyyy` format. If AMD publishes several architecture branches and an
installed version lies between them with no driver date, `IsNewer` returns null: the host reports
that comparison as unavailable, rather than a failure or an invented install offer.

Intel's packaged `installation_readme.txt` documents exit 14 as success requiring a reboot, 18 as
success requiring shutdown/power-on, 13 as a failed attempt requiring a reboot first, and 1/5–11/15/16
as failures. The library preserves that distinction. AMD's result log can report package failure even
when its launcher exits successfully; that is never reported as a successful installation.

Consumers must honor handheld/OEM restrictions before selecting a generic package. AMD's Adrenalin
release notes explicitly exclude handheld gaming devices. LibHandheld records this and the
model-specific OEM source; the host declines those generic AMD updates with the supplied reason.

Sources: [Intel command line](https://www.intel.com/content/www/us/en/support/articles/000006773/graphics.html),
[Intel packaged readme](https://www.intel.com/content/www/us/en/support/articles/000098051/graphics.html),
[AMD installation](https://www.amd.com/en/resources/support-articles/faqs/RSX2-Install.html),
[AMD command line](https://drivers.amd.com/relnotes/command-line-installation.pdf),
[NVIDIA silent install](https://docs.nvidia.com/datacenter/tesla/driver-installation-guide/windows.html).

These endpoints are public vendor update/download sources. No vendor application is installed,
account emulated, payload redistributed or undocumented OEM bypass attempted. The host presents the
official license/details URL and requires explicit acceptance before downloading/installing. No
claim is made that these public endpoints carry a separately supported third-party API contract.
