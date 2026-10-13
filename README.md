# LibGPUDriverInteract

**A .NET library for Intel, AMD and NVIDIA graphics driver controls.**

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](src/LibGPUDriverInteract/LibGPUDriverInteract.csproj)
[![Windows x64](https://img.shields.io/badge/Windows-x64-0078D4)](docs/getting-started.md)
[![MIT license](https://img.shields.io/badge/License-MIT-green)](LICENSE)

Discover the settings offered by an installed graphics driver, read their state, apply typed changes
and manage native application profiles. One API covers all three vendors, including adapter and
connected-display controls. Your application chooses the profiles and presents the UI.

[Getting started](docs/getting-started.md) · [Documentation](docs/README.md) ·
[API reference](docs/reference.md) · [Contributing](CONTRIBUTING.md)

## Vendor support

| Vendor | Native interfaces | Supported areas                                                                                                                                                               |
| ------ | ----------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Intel  | IGCL / ControlLib | Driver-reported 3D settings, frame pacing, filtering, sharpening, scaling, shared GPU memory, Arc Sync, display colour/power and executable profiles                          |
| AMD    | ADLX / ADL        | Anti-Lag, Boost, sharpening, Enhanced Sync, RSR, AFMF, supported FidelityFX upgrades, FreeSync, scaling, colour, Vari-Bright and dithering                                    |
| NVIDIA | NVAPI / DRS       | Performance and quality settings, synchronization, G-SYNC, supported DLSS overrides, frame generation, Smooth Motion, RTX HDR, Digital Vibrance, output formats and dithering |

Available settings and values depend on the installed driver, adapter and output. The library
publishes supported controls with their bounds, choices, profile scope and apply timing. Use those
descriptors when building controls; a feature listed here is not a promise for every GPU.

## Features

- **Typed controls:** Boolean, integer, choice and action requests with explicit write outcomes.
- **Multiple adapters and vendors:** one owner per vendor, with separate adapter/output identities.
- **Application profiles:** Intel and NVIDIA native executable profiles where supported; metadata
  for applying GPU-wide settings when the active application changes.
- **Display identity:** physical monitor information alongside the current Windows output route.
- **Live notifications:** descriptor, state and health updates, with explicit topology refresh.
- **Owned cleanup:** serialized native calls and restoration that preserves unrelated driver
  settings.

The library loads native DLLs from the installed vendor driver. Vendor binaries are not bundled.
Historical hardware observations and source attribution are documented in
[PROVENANCE.md](PROVENANCE.md).

## Getting started

Requirements: **.NET 10**, **Windows 10 build 19041 or newer**, an **x64 application**, and the
matching installed graphics driver.

```powershell
git clone --recurse-submodules https://github.com/KillerPixelCrew/LibGPUDriverInteract.git
cd LibGPUDriverInteract
dotnet build LibGPUDriverInteract.slnx -c Release
```

The submodule supplies WindowsDeviceControl for display identity. Add a project reference to
`src/LibGPUDriverInteract/LibGPUDriverInteract.csproj`, then subscribe before starting a vendor:

```csharp
using LibGPUDriverInteract;

var vendors = GpuDriver.DetectVendors();
if (vendors.Count == 0)
{
    Console.WriteLine("No supported graphics vendor detected.");
    return;
}
var vendor = vendors[0];
var stateDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MyApplication", "Graphics", vendor.ToString());

await using var driver = GpuDriver.Create(vendor, stateDirectory);
driver.DescriptorsChanged += set =>
{
    foreach (var control in set.Descriptors)
    {
        Console.WriteLine($"{control.Label}: {control.CapabilityId}");
    }
};
driver.StatusChanged += status => Console.WriteLine(status.Detail);

try
{
    var health = await driver.StartAsync();
    Console.WriteLine($"{vendor}: {health}");
}
finally
{
    await driver.StopAsync();
}
```

Use `ExecuteAsync` to apply a `GpuCommand` built from a current descriptor. Use
`SyncApplicationProfilesAsync` for complete native profile sets and `RefreshTopologyAsync` after
adapter or monitor changes. Events run on the publishing thread; marshal UI updates in your
application. Keep an owner alive for each vendor you want to use.

The [getting-started guide](docs/getting-started.md) includes a write example. The
[profile guide](docs/profiles.md) explains native profiles and GPU-wide settings.

## Documentation

| Guide                                           | Read it for                                                 |
| ----------------------------------------------- | ----------------------------------------------------------- |
| [Controls and vendor support](docs/controls.md) | Descriptors, values, availability and write outcomes        |
| [Application profiles](docs/profiles.md)        | Native executable profiles, switched values and restoration |
| [Display identity](docs/displays.md)            | Physical monitors, hotplug and changing output routes       |
| [Lifetime and events](docs/lifecycle.md)        | Startup, cancellation, driver loss and safe cleanup         |
| [Troubleshooting](docs/troubleshooting.md)      | Missing controls, failed sync and uncertain writes          |
| [API reference](docs/reference.md)              | Public types, methods, properties and enum values           |

## Sources and license

The implementation uses **public vendor documentation and open-source references**, including Intel
IGCL, AMD ADLX/ADL, NVIDIA NVAPI, HandheldCompanion, ColorControl and NoVidiaApp. Source attribution
is retained in [PROVENANCE.md](PROVENANCE.md).

LibGPUDriverInteract is [MIT licensed](LICENSE). Preserve its license and attribution notices when
redistributing it. See [Contributing](CONTRIBUTING.md) to add a control or improve a backend.

Used by [WSGM](https://github.com/KillerPixelCrew/WSGM). For handheld hardware, see
[LibHandheld](https://github.com/KillerPixelCrew/LibHandheld); for generic Windows display, audio,
radio and power controls, see
[WindowsDeviceControl](https://github.com/KillerPixelCrew/windows-device-control).
