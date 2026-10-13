# LibGPUDriverInteract

A Windows-first .NET library for Intel, AMD and NVIDIA graphics driver interaction. The native
engines, serialized runtime and profile ownership mechanisms were extracted from WSGM's vendor
implementations. Applications consume a direct driver API without WSGM SDK dependencies.

The source includes the library, migrated regression tests and public API. Research uses public
vendor documentation and open-source references, including Intel IGCL, AMD ADLX/ADL, NVIDIA NVAPI,
HandheldCompanion, ColorControl and NoVidiaApp. [Provenance](PROVENANCE.md) records attribution and
separates existing hardware observations from validation of the extraction.

## Scope

| Backend | Driver interfaces                              | Responsibilities                                                                                 |
| ------- | ---------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| Intel   | IGCL and the documented existing registry path | Graphics features, display controls, shared-memory configuration and native application settings |
| AMD     | ADLX and ADL                                   | Radeon graphics features, connected-display controls and supported GPU-wide settings             |
| NVIDIA  | NVAPI and DRS                                  | Graphics settings, native application profiles, output colour, dithering and HDR output modes    |

The library owns driver loading, native session lifetime, support discovery, adapters and outputs,
supported values, read/write operations and explicit command outcomes. It loads the installed vendor
driver's libraries; vendor binaries are not redistributed.

The consuming application owns game detection, desired profiles, activation policy and UI. Native
application-profile operations and restoration records may live in the library, with explicit
caller-owned storage and no implicit WSGM configuration paths.

## Documentation

Start with the [documentation index](docs/README.md):

- [Getting started](docs/getting-started.md): build, reference, detect, subscribe, start and stop.
- [Lifetime and events](docs/lifecycle.md): cancellation, topology and driver loss.
- [Controls and vendor support](docs/controls.md): descriptors, values and write outcomes.
- [Application profiles](docs/profiles.md): native profiles, switched values and restoration.
- [Display identity](docs/displays.md): physical monitors and changing Windows routes.
- [Troubleshooting](docs/troubleshooting.md): missing controls, uncertain writes and cleanup.
- [Complete API reference](docs/reference.md): public types, members and enum values.
- [Contributing](CONTRIBUTING.md): native contracts, support checks and regression cases.

The current Intel and NVIDIA plugins have confirmed hardware testing; AMD testing was confirmed by
the maintainer from another repository contributor. That evidence belongs to the existing
implementation. Migration into this library must preserve the behavior and its tests.

## Other libraries

- [LibHandheld](https://github.com/KillerPixelCrew/LibHandheld) owns handheld-specific hardware,
  including OEM power limits, fans, charging, physical input and sensors.
- [WindowsDeviceControl](https://github.com/KillerPixelCrew/windows-device-control) owns generic
  Windows audio, radios, display topology, modes, Windows HDR and power policy.

WSGM consumes this library through its native graphics publisher and retains application profile
policy. NVIDIA uses fresh owned profile names and removes exact legacy WSGM profiles once. Its
common Plugin SDK remains for independent extensions such as IR and third-party integrations.

## Build and use

Clone with submodules so standalone builds have the pinned WindowsDeviceControl source dependency.
Standalone and WSGM builds use this repository's same pinned nested dependency.

```powershell
git clone --recurse-submodules https://github.com/KillerPixelCrew/LibGPUDriverInteract.git
dotnet build LibGPUDriverInteract.slnx -c Release
```

```csharp
using LibGPUDriverInteract;

var callerOwnedStateDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MyApplication", "Graphics", "Intel");
await using var driver = GpuDriver.Create(GpuVendor.Intel, callerOwnedStateDirectory);
driver.DescriptorsChanged += descriptors => Console.WriteLine(descriptors.Descriptors.Count);
driver.StateChanged += state => Console.WriteLine(state.Quality);
try
{
    var health = await driver.StartAsync();
}
finally
{
    await driver.StopAsync();
}
```

[API.md](API.md) describes owner admission, events, cancellation, native profile sync and safe
retirement. Notifications run on the publishing thread; marshal UI work in the application. The
caller supplies the state directory and chooses the driver/profile policy.

## Contributions

Document native API sources, supported driver behavior and the exact adapter/output identity. Keep
capability discovery separate from user writes, preserve unedited state and external changes, and
add regression cases for encoding, profile ownership and resource lifetime. Record hardware
observations independently from compilation and fixtures.

## WSGM checkout

WSGM tracks this repository as `external/libgpu-driver-interact`. Commit and push library changes
first, then commit the updated gitlink in WSGM.
