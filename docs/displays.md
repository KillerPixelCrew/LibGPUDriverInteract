# Display identity

## Scaled source resolutions

`GpuDriver.ReadScaledResolutionsAsync`, `SetScaledResolutionAsync` and
`RestartScaledResolutionDriverAsync` share the owner's existing serialized lane and shutdown
admission. A catalog carries the physical display, driving adapter's PnP identity, list scope and
support detail. A stale catalog cannot target another adapter or another vendor's lane.

Intel binds optional `ctlGetSetCustomMode` and uses its output handle. The native version-0 ABI is
`uint Size`, `byte Version`, aligned enum operation, `uint NumOfModes`, aligned source-list pointer:
24 bytes on x64 (pointer offset 16), 20 on x86. Each source record is exactly two uints, `SourceX`
and `SourceY`, with no timing or refresh. A count read precedes a bounded read of up to 20 records;
add/remove uses one record and no automatic retries. See Intel's
[official header](https://github.com/intel/drivers.gpu.control-library/blob/master/include/igcl_api.h),
[API](https://intel.github.io/drivers.gpu.control-library/Control/api.html#ctlGetSetCustomMode) and
[custom-mode sample](https://github.com/intel/drivers.gpu.control-library/blob/master/Samples/Custom_Mode_Samples/CustomMode_Sample_App.cpp).
The reviewed local header snapshot is `_ref/intel-gcl/include/igcl_api.h` in WSGM, structures at
lines 4121–4139. This ABI source is distinct from the historical hardware experiment in issue #233.

AMD edits `DALNonStandardModesBCD` and numbered suffixes in the exact adapter driver key. It
preserves partitions, other values, `DALRestrictedModesBCD` lists, unknown records, refresh/flags
and trailing malformed bytes. NVIDIA edits `NV_Modes` `REG_MULTI_SZ`, preserving unknown groups and
qualifiers and deleting source tokens rather than inserting dummy modes. Unsupported storage kinds
are refused before mutation. The independent codecs implement the observed file formats; they do not
copy GPL source. The primary format evidence is the
[SRE 1.0 source archive](https://www.monitortests.com/download/sre/sre-1.0-src.zip), specifically
`SRE_Gpu.cpp` list loading/value types and `SRE_Resolution.cpp`/`SRE_ResolutionList.cpp`
serialization.
[SRE's author](https://www.monitortests.com/forum/Thread-Scaled-Resolution-Editor-SRE) documents a
driver restart for these registry lists. GPL-3.0-or-later SRE code is not included in this MIT
library.

Accepted Intel operations report Immediate. AMD/NVIDIA report DriverRestart; this does not mean the
new Windows mode is already advertised. Restart is a separate explicit operation using Windows'
[PnPUtil `/restart-device`](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax#restart-device)
for the freshly resolved PnP instance, without `/reboot`. Uncertain writes/restarts are never
retried. List reads are preservation inputs, not mandatory readback confirmation of a successful
setter.

Source fixtures cover native ABI, BCD data preservation, NVIDIA group removal, malformed extensions
and storage kinds. Their execution and new vendor hardware acceptance are deferred to the owning
application's manual-first validation batch. No new hardware result is claimed here.

## Fixed refresh rates

`ReadRefreshRateRangeAsync`, `SetRefreshRateAsync` and `RestoreRefreshRateAsync` hold one display at a
refresh rate without a Windows mode change. They run on the owner's serialized lane. Only Intel
implements them, through the Arc Sync Custom profile (`CTL_INTEL_ARC_SYNC_PROFILE_CUSTOM` and its parameters at `igcl_api.h`
lines 3933-3953, setter at line 3994): `ctlSetIntelArcSyncProfile` with `MinRefreshRateInHz` and `MaxRefreshRateInHz` both at
the requested rate makes the driver run the panel at that rate, fractional rates such as 59.94
included. The range comes from `ctlGetIntelArcSyncInfoForMonitor` (`MinimumRefreshRateInHz` and
`MaximumRefreshRateInHz`), and a request outside it is clamped into it. Every fixed write logs the
profile, its bounds and the driver result under `arcsync`.

The first fixed write reads the profile in use and records it in `arc-sync-fixed-refresh.v1.json` in
the state directory before the setter runs. Without a readable profile nothing is written. Restore
writes that profile back, or the baseline the owning policy supplied for the display through
`SetRefreshRateBaselineAsync` (for example a variable refresh range it keeps), and removes the
record. A profile changed since by anyone else is left alone and the record dropped. A write through
an Arc Sync row (variable refresh, profile or a Custom value) is the user's and ends the fixed rate.
Reads of the fixed profile are never remembered as the profile variable refresh returns to.

The Arc Sync profile is persistent driver state, so a fixed rate must not outlive its owner. Closing the
Intel session restores every fixed rate. A record a previous process left behind is restored once
when its display is next discovered, if the driver still runs that rate. A lost session cannot write;
its record waits for the next session. This path follows Intel's header and the maintainer's earlier
manual use of the Custom profile; it has no recorded hardware acceptance yet.

## Arc Sync for the owning policy and Intel's frame rate solutions

`ReadArcSyncAsync` returns one display's monitor range, the profile the driver runs and, while a rate is
fixed, the profile captured beneath it (`GpuArcSyncState`). `SetArcSyncProfileAsync` writes a profile
for the one policy that owns Arc Sync, such as a variable refresh range it keeps, with its values as
given. It is not a user's row: it ends no fixed rate and hands nothing to the user, and enabling
variable refresh again returns to it. While the driver still runs a rate this library fixed it writes
nothing and says so; the policy supplies the profile through `SetRefreshRateBaselineAsync`, and the
release writes it. Both log the profile, its bounds and the driver result under `arcsync`.

`ReadVrrSolutionsAsync` and `SetVrrSolutionAsync` use Intel's private D3DKMT VRR escape on the adapter
of the output that drives the display, opened by its LUID: a 124-byte driver-private payload with
display escape major 1 and VRR minor 10 at offsets 8 and 12 and the operation at 16. Operation 0
reads; the answer's bytes 20 to 23 are support, enabled, high and low frame rate solution. Operations
3 and 4 turn the low frame rate solution (LFC) on and off, 5 and 6 the high one. The layout and
operations follow ClawLab's MSI Claw Intel VRR Range Fix 3.0.0 (MIT, see
[third-party notices](../THIRD_PARTY_NOTICES.md)), which opens the same adapter through the device
context of the single active Intel display. The flags are adapter-wide driver state that outlives the
process; the caller owns putting them back, and no write is retried. The payload is covered by tests
as bytes; hardware behavior is the Atlas8Tweaks and ClawLab testing's, not this library's.

## Physical output controls

NVIDIA publishes `display.gsync` separately for each supported physical output, using
[`NvAPI_DISP_GetAdaptiveSyncData` and `NvAPI_DISP_SetAdaptiveSyncData`](https://docs.nvidia.com/nvapi/group__dispcontrol.html)
with that output's display ID. Writes preserve its frame interval and frame-splitting setting. The
global DRS G-SYNC driver policy is separate. Output toggles use per-instance graphics persistence
rather than a consumer's single performance-profile VRR Boolean. This write path still needs manual
hardware acceptance; compilation does not prove the NVIDIA Control Panel checkbox changed.

Physical output sections can carry GpuSection.DisplayTarget. Adapter-wide policies have no physical
target. A driver output identifier is diagnostic metadata, not a Windows monitor route.

| Field                                       | Meaning                                     | Persist for remembered choices?     |
| ------------------------------------------- | ------------------------------------------- | ----------------------------------- |
| DevicePath                                  | Exact Windows monitor device-interface path | Yes, with current resolution checks |
| EdidIdentity                                | EDID manufacturer/product/serial identity   | Yes, as physical identity evidence  |
| SourceName                                  | Current GDI name such as `\\.\DISPLAY1`     | No                                  |
| AdapterLowPart / AdapterHighPart / TargetId | Current CCD route                           | No                                  |
| NativeOutputId                              | Vendor-native diagnostic output identity    | No                                  |

Hotplug, reboot and driver changes can alter routes and instance IDs. Persist physical identity,
signal RefreshTopologyAsync, then resolve remembered choices against the new descriptors and target.
Do not route by a friendly title, display number or duplicate EDID serial alone.

Discovery includes connected inactive monitors when the native API resolves them. Absence is not an
instruction to write to a guessed output. Match the latest capability and instance before dispatch.

Windows topology, resolution, generic HDR and layout policy belong to WindowsDeviceControl or the
host. This library exposes vendor output controls and read-only identity; it does not activate a
saved desktop layout. Display writes can retrain the link and must be explicit operations.
