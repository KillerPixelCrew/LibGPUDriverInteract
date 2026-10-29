# Implementation provenance

The Intel, AMD and NVIDIA engines were extracted from KillerPixelCrew's MIT-licensed first-party GPU
implementations in WSGM, at source commit
[`1df3f4d8987fcff5f1c8f2554c489195f1cb8b13`](https://github.com/KillerPixelCrew/WSGM/tree/1df3f4d8987fcff5f1c8f2554c489195f1cb8b13).
The extraction preserves native entry points, layouts, setting identities, support checks and native
application-profile ownership while replacing WSGM SDK dependencies with library-owned records and a
direct driver API.

## Vendor references

Scaled source-mode additions use Intel's public IGCL custom-mode ABI and independently authored
AMD/NVIDIA registry codecs. The GPL-3.0-or-later SRE 1.0 source was inspected only as format
evidence; no SRE implementation code is incorporated. [Display mechanisms](docs/displays.md) records
the upstream references, exact native layouts, preservation boundaries and deferred hardware proof.

- [Intel implementation provenance](https://github.com/KillerPixelCrew/WSGM/blob/1df3f4d8987fcff5f1c8f2554c489195f1cb8b13/src/WSGM.Plugin.IntelGpu/PROVENANCE.md):
  Intel Graphics Control Library headers/samples and dated observations.
- [AMD implementation provenance](https://github.com/KillerPixelCrew/WSGM/blob/1df3f4d8987fcff5f1c8f2554c489195f1cb8b13/src/WSGM.Plugin.AmdGpu/PROVENANCE.md):
  AMD ADLX/ADL, Handheld Companion behavioral references and ColorControl output-control references.
- [NVIDIA implementation provenance](https://github.com/KillerPixelCrew/WSGM/blob/1df3f4d8987fcff5f1c8f2554c489195f1cb8b13/src/WSGM.Plugin.NvidiaGpu/PROVENANCE.md):
  NVIDIA NVAPI, NoVidiaApp settings and ColorControl output-control references.

Vendor driver libraries are loaded from the installed driver and are not redistributed. Generic
Windows display identity and HDR operations use WindowsDeviceControl, pinned as a source dependency
for standalone builds. WSGM reuses its existing sibling checkout of the same dependency.

AMD 3D controls reacquire the system's 3D service, its required service interface and the feature
for each operation, releasing the complete chain afterwards. This follows the ownership sequence in
Handheld Companion's shipped `ADLX_Wrapper.dll` (SHA-256
`7dda38f5365e4b61ab07fe788e29b9a4bbef3529b1634325d9c2997ea48dd3b8`) and AMD's native sample. The C#
interop contract is cross-checked against the SDK headers and
[AMD's binding guide](https://gpuopen.com/manuals/adlx/programming-with-adlx/adlx-programming-guide/quick-start/building-csharp-bindings-for-adlx/).
The scoped-chain regressions establish acquisition, release order and no replay after a failed
setter; they do not establish AFMF acceptance on a contributor's AMD device. The settle-by-readback
path for a setter that reports failure after dispatch is established with runtime fixtures only; no
AMD device has yet shown whether its AFMF setter settles or stays refused, and the ADLX version line
logged at session open exists so the next device log says which driver build answered.

## Validation provenance

Per-output NVIDIA Adaptive Sync uses NVIDIA's public
[`nvapi.h`](https://github.com/NVIDIA/nvapi/blob/main/nvapi.h) structures
`NV_GET_ADAPTIVE_SYNC_DATA_V1` and `NV_SET_ADAPTIVE_SYNC_DATA_V2` (40 bytes each), with the function
IDs `0xb73d1ee9` and `0x3eebba1d` from
[`nvapi_interface.h`](https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h). These controls
have compilation and isolated output-routing regressions; their live write behavior awaits manual
acceptance.

The maintainer confirmed Intel and NVIDIA hardware testing and contributor AMD testing on
2026-10-09. That confirmation describes the original engines. The extraction retains their tests and
requires compilation and application parity verification; it does not relabel an automated fixture
as a new hardware observation.

The public [control guide](docs/controls.md), [profile guide](docs/profiles.md) and
[API reference](docs/reference.md) document the native controls and application boundary.
