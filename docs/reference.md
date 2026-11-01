# Complete API reference

Public types, declared members and enum values from the library's compiled metadata and XML
documentation. Native internals and compiler-generated record helpers are omitted. See
[integration guides](README.md) for usage and ownership.

- [GpuApplicationProfile](#gpuapplicationprofile)
- [GpuApplicationValue](#gpuapplicationvalue)
- [GpuApplyTiming](#gpuapplytiming)
- [GpuArcSyncBaseline](#gpuarcsyncbaseline)
- [GpuArcSyncProfile](#gpuarcsyncprofile)
- [GpuArcSyncState](#gpuarcsyncstate)
- [GpuCategory](#gpucategory)
- [GpuChoice](#gpuchoice)
- [GpuCommand](#gpucommand)
- [GpuCommandOutcome](#gpucommandoutcome)
- [GpuCommandResult](#gpucommandresult)
- [GpuDescriptor](#gpudescriptor)
- [GpuDescriptorSet](#gpudescriptorset)
- [GpuDisplayTarget](#gpudisplaytarget)
- [GpuDisplayWriteResult](#gpudisplaywriteresult)
- [GpuDriver](#gpudriver)
- [GpuHealth](#gpuhealth)
- [GpuLogEntry](#gpulogentry)
- [GpuLogLevel](#gpuloglevel)
- [GpuPersistence](#gpupersistence)
- [GpuProfileFailure](#gpuprofilefailure)
- [GpuProfileScope](#gpuprofilescope)
- [GpuProfileSync](#gpuprofilesync)
- [GpuProfileSyncResult](#gpuprofilesyncresult)
- [GpuReason](#gpureason)
- [GpuReasonCode](#gpureasoncode)
- [GpuRefreshRateOutcome](#gpurefreshrateoutcome)
- [GpuRefreshRateRange](#gpurefreshraterange)
- [GpuRefreshRateResult](#gpurefreshrateresult)
- [GpuRole](#gpurole)
- [GpuScaledResolution](#gpuscaledresolution)
- [GpuScaledResolutionActivation](#gpuscaledresolutionactivation)
- [GpuScaledResolutionCatalog](#gpuscaledresolutioncatalog)
- [GpuScaledResolutionResult](#gpuscaledresolutionresult)
- [GpuSection](#gpusection)
- [GpuSectionKind](#gpusectionkind)
- [GpuState](#gpustate)
- [GpuStateQuality](#gpustatequality)
- [GpuStatus](#gpustatus)
- [GpuUnit](#gpuunit)
- [GpuValue](#gpuvalue)
- [GpuValueKind](#gpuvaluekind)
- [GpuVendor](#gpuvendor)
- [GpuVrrSolution](#gpuvrrsolution)
- [GpuVrrSolutions](#gpuvrrsolutions)

## GpuApplicationProfile

Namespace: `LibGPUDriverInteract`.

One application's native overrides. `ProfileId`: Consumer-owned stable identity. `DisplayName`:
Plain application name. `Executables`: Executable file names as matched by the driver. `Values`:
Desired native overrides.

| Member                                                                                                                                      | Contract                                                                                                                                                                                                                   |
| ------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GpuApplicationProfile(String ProfileId, String DisplayName, IReadOnlyList<String> Executables, IReadOnlyList<GpuApplicationValue> Values)` | One application's native overrides. `ProfileId`: Consumer-owned stable identity. `DisplayName`: Plain application name. `Executables`: Executable file names as matched by the driver. `Values`: Desired native overrides. |
| `String ProfileId { get; init; }`                                                                                                           | Consumer-owned stable identity.                                                                                                                                                                                            |
| `String DisplayName { get; init; }`                                                                                                         | Plain application name.                                                                                                                                                                                                    |
| `IReadOnlyList<String> Executables { get; init; }`                                                                                          | Executable file names as matched by the driver.                                                                                                                                                                            |
| `IReadOnlyList<GpuApplicationValue> Values { get; init; }`                                                                                  | Desired native overrides.                                                                                                                                                                                                  |

## GpuApplicationValue

Namespace: `LibGPUDriverInteract`.

One native application override. `CapabilityId`: Setting identity. `InstanceId`: Adapter or display
discriminator. `Value`: Typed requested value.

| Member                                                                        | Contract                                                                                                                                           |
| ----------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GpuApplicationValue(String CapabilityId, String InstanceId, GpuValue Value)` | One native application override. `CapabilityId`: Setting identity. `InstanceId`: Adapter or display discriminator. `Value`: Typed requested value. |
| `String CapabilityId { get; init; }`                                          | Setting identity.                                                                                                                                  |
| `String InstanceId { get; init; }`                                            | Adapter or display discriminator.                                                                                                                  |
| `GpuValue Value { get; init; }`                                               | Typed requested value.                                                                                                                             |

## GpuApplyTiming

Namespace: `LibGPUDriverInteract`.

When a successfully written setting takes effect.

| Value                  | Numeric value | Meaning                                  |
| ---------------------- | ------------- | ---------------------------------------- |
| `Immediate`            | `0`           | Immediately.                             |
| `NextApplicationStart` | `1`           | When the target application next starts. |
| `SystemRestart`        | `2`           | After Windows restarts.                  |

## GpuArcSyncBaseline

Namespace: `LibGPUDriverInteract`.

The Arc Sync profile a released fixed refresh rate returns to on one display. `Profile`: The
profile. `MinimumHz`: The lowest refresh rate the profile uses. `MaximumHz`: The highest refresh
rate the profile uses. `MaxFrameTimeIncreaseUs`: The frame time increase limit, in microseconds.
`MaxFrameTimeDecreaseUs`: The frame time decrease limit, in microseconds.

| Member                                                                                                                                                    | Contract                                                                                                                                                                                                                                                                                                                                                                 |
| --------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `GpuArcSyncBaseline(GpuArcSyncProfile Profile, Double MinimumHz, Double MaximumHz, UInt32 MaxFrameTimeIncreaseUs = 0, UInt32 MaxFrameTimeDecreaseUs = 0)` | The Arc Sync profile a released fixed refresh rate returns to on one display. `Profile`: The profile. `MinimumHz`: The lowest refresh rate the profile uses. `MaximumHz`: The highest refresh rate the profile uses. `MaxFrameTimeIncreaseUs`: The frame time increase limit, in microseconds. `MaxFrameTimeDecreaseUs`: The frame time decrease limit, in microseconds. |
| `GpuArcSyncProfile Profile { get; init; }`                                                                                                                | The profile.                                                                                                                                                                                                                                                                                                                                                             |
| `Double MinimumHz { get; init; }`                                                                                                                         | The lowest refresh rate the profile uses.                                                                                                                                                                                                                                                                                                                                |
| `Double MaximumHz { get; init; }`                                                                                                                         | The highest refresh rate the profile uses.                                                                                                                                                                                                                                                                                                                               |
| `UInt32 MaxFrameTimeIncreaseUs { get; init; }`                                                                                                            | The frame time increase limit, in microseconds.                                                                                                                                                                                                                                                                                                                          |
| `UInt32 MaxFrameTimeDecreaseUs { get; init; }`                                                                                                            | The frame time decrease limit, in microseconds.                                                                                                                                                                                                                                                                                                                          |

## GpuArcSyncProfile

Namespace: `LibGPUDriverInteract`.

An Intel Arc Sync profile, as the driver names them.

| Value         | Numeric value | Meaning                                       |
| ------------- | ------------- | --------------------------------------------- |
| `Recommended` | `1`           | The driver's recommended profile.             |
| `Excellent`   | `2`           | The Excellent profile.                        |
| `Good`        | `3`           | The Good profile.                             |
| `Compatible`  | `4`           | The Compatible profile.                       |
| `Off`         | `5`           | Variable refresh off.                         |
| `Vesa`        | `6`           | The VESA profile.                             |
| `Custom`      | `7`           | A custom refresh range and frame time limits. |

## GpuArcSyncState

Namespace: `LibGPUDriverInteract`.

One display's Intel Arc Sync state, as the policy that owns the profile reads it. `Display`: The
display. `MonitorMinimumHz`: The lowest rate the monitor reports for variable refresh.
`MonitorMaximumHz`: The highest rate the monitor reports for variable refresh. `Profile`: The
profile the driver runs now; the Custom fixed profile while a rate is fixed. `FixedHz`: The rate
this driver owner holds now, or null when none is fixed. `BeforeFixed`: While a rate is fixed, the
profile in use before the first fixed write; otherwise null.

| Member                                                                                                                                                                     | Contract                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `GpuArcSyncState(GpuDisplayTarget Display, Double MonitorMinimumHz, Double MonitorMaximumHz, GpuArcSyncBaseline Profile, Double? FixedHz, GpuArcSyncBaseline BeforeFixed)` | One display's Intel Arc Sync state, as the policy that owns the profile reads it. `Display`: The display. `MonitorMinimumHz`: The lowest rate the monitor reports for variable refresh. `MonitorMaximumHz`: The highest rate the monitor reports for variable refresh. `Profile`: The profile the driver runs now; the Custom fixed profile while a rate is fixed. `FixedHz`: The rate this driver owner holds now, or null when none is fixed. `BeforeFixed`: While a rate is fixed, the profile in use before the first fixed write; otherwise null. |
| `GpuDisplayTarget Display { get; init; }`                                                                                                                                  | The display.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| `Double MonitorMinimumHz { get; init; }`                                                                                                                                   | The lowest rate the monitor reports for variable refresh.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `Double MonitorMaximumHz { get; init; }`                                                                                                                                   | The highest rate the monitor reports for variable refresh.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `GpuArcSyncBaseline Profile { get; init; }`                                                                                                                                | The profile the driver runs now; the Custom fixed profile while a rate is fixed.                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `Double? FixedHz { get; init; }`                                                                                                                                           | The rate this driver owner holds now, or null when none is fixed.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `GpuArcSyncBaseline BeforeFixed { get; init; }`                                                                                                                            | While a rate is fixed, the profile in use before the first fixed write; otherwise null.                                                                                                                                                                                                                                                                                                                                                                                                                                                                |

## GpuCategory

Namespace: `LibGPUDriverInteract`.

A plain subgroup within a driver section.

| Member                             | Contract                  |
| ---------------------------------- | ------------------------- |
| `GpuCategory()`                    |                           |
| `String CategoryId { get; init; }` | Stable subgroup identity. |
| `String Title { get; init; }`      | Plain subgroup title.     |
| `Int32 SortOrder { get; init; }`   | Relative subgroup order.  |

## GpuChoice

Namespace: `LibGPUDriverInteract`.

One legal named option. `Value`: Machine value. `Label`: Plain display label.

| Member                                  | Contract                                                                      |
| --------------------------------------- | ----------------------------------------------------------------------------- |
| `GpuChoice(String Value, String Label)` | One legal named option. `Value`: Machine value. `Label`: Plain display label. |
| `String Value { get; init; }`           | Machine value.                                                                |
| `String Label { get; init; }`           | Plain display label.                                                          |

## GpuCommand

Namespace: `LibGPUDriverInteract`.

A typed request validated against the owner's current controls before dispatch.

| Member                                   | Contract                                      |
| ---------------------------------------- | --------------------------------------------- |
| `GpuCommand()`                           |                                               |
| `String CapabilityId { get; init; }`     | Setting identity.                             |
| `String InstanceId { get; init; }`       | Adapter or display discriminator.             |
| `GpuValue RequestedValue { get; init; }` | Requested typed value, or null for an action. |

## GpuCommandOutcome

Namespace: `LibGPUDriverInteract`.

The truthful result of a command; uncertain results never authorize an automatic retry.

| Value               | Numeric value | Meaning                                                   |
| ------------------- | ------------- | --------------------------------------------------------- |
| `AppliedVerified`   | `0`           | A setter completed and independent readback matched.      |
| `AppliedUnverified` | `1`           | A setter completed without independent matching readback. |
| `Rejected`          | `2`           | Refused before a setter was dispatched.                   |
| `Indeterminate`     | `3`           | A write may have reached the driver before failure.       |

## GpuCommandResult

Namespace: `LibGPUDriverInteract`.

A command outcome; no rollback or automatic retry is implied.

| Member                                     | Contract                                                  |
| ------------------------------------------ | --------------------------------------------------------- |
| `GpuCommandResult()`                       |                                                           |
| `GpuCommandOutcome Outcome { get; init; }` | Truthful native-write outcome.                            |
| `GpuReason Reason { get; init; }`          | Reason for rejection, uncertainty or failed confirmation. |
| `GpuValue ReadbackValue { get; init; }`    | Matching independent readback for a verified write only.  |

## GpuDescriptor

Namespace: `LibGPUDriverInteract`.

One supported driver control, containing plain grouping and value metadata.

| Member                                            | Contract                                                        |
| ------------------------------------------------- | --------------------------------------------------------------- |
| `GpuDescriptor()`                                 |                                                                 |
| `String CapabilityId { get; init; }`              | Stable setting identity.                                        |
| `String InstanceId { get; init; }`                | Adapter or display discriminator, or null for a global setting. |
| `String Label { get; init; }`                     | Plain display label.                                            |
| `GpuRole Role { get; init; }`                     | Control meaning.                                                |
| `GpuValueKind ValueKind { get; init; }`           | Accepted value shape.                                           |
| `String SectionId { get; init; }`                 | Declared section containing the control.                        |
| `String CategoryId { get; init; }`                | Declared category within its section.                           |
| `Int32 SortOrder { get; init; }`                  | Relative order within its group.                                |
| `Boolean SupportsRead { get; init; }`             | Whether the driver exposes a read.                              |
| `Boolean SupportsWrite { get; init; }`            | Whether the driver exposes a setter.                            |
| `Int32? Minimum { get; init; }`                   | Inclusive integer minimum.                                      |
| `Int32? Maximum { get; init; }`                   | Inclusive integer maximum.                                      |
| `Int32? Step { get; init; }`                      | Legal integer step.                                             |
| `GpuUnit Unit { get; init; }`                     | Numeric unit.                                                   |
| `GpuPersistence Persistence { get; init; }`       | Setting lifetime.                                               |
| `GpuProfileScope ProfileScope { get; init; }`     | Available application-profile mechanism.                        |
| `GpuApplyTiming ApplyTiming { get; init; }`       | When an accepted setter becomes effective.                      |
| `IReadOnlyList<GpuChoice> Choices { get; init; }` | Legal named options.                                            |

## GpuDescriptorSet

Namespace: `LibGPUDriverInteract`.

A complete replacement set of current driver controls.

| Member                                                    | Contract                          |
| --------------------------------------------------------- | --------------------------------- |
| `GpuDescriptorSet()`                                      |                                   |
| `IReadOnlyList<GpuSection> Sections { get; init; }`       | Complete plain grouping metadata. |
| `IReadOnlyList<GpuDescriptor> Descriptors { get; init; }` | All currently supported controls. |

## GpuDisplayTarget

Namespace: `LibGPUDriverInteract`.

A physical driver output's observed Windows monitor identity and session route. Routes are refreshed
on topology discovery. Persist the monitor identity, never GDI numbering or native output IDs.

| Member                                  | Contract                                                                           |
| --------------------------------------- | ---------------------------------------------------------------------------------- |
| `GpuDisplayTarget()`                    |                                                                                    |
| `String DevicePath { get; init; }`      | Exact Windows monitor device-interface path, when resolved.                        |
| `String EdidIdentity { get; init; }`    | EDID manufacturer, product and serial identity, when available.                    |
| `String SourceName { get; init; }`      | Current GDI source name, such as \\.\DISPLAY1, when the output is active.          |
| `UInt32? AdapterLowPart { get; init; }` | Current CCD adapter LUID low part.                                                 |
| `Int32? AdapterHighPart { get; init; }` | Current CCD adapter LUID high part.                                                |
| `UInt32? TargetId { get; init; }`       | Current CCD target identifier.                                                     |
| `String NativeOutputId { get; init; }`  | Vendor-native output identity for diagnostics; never a cross-vendor Windows route. |

## GpuDisplayWriteResult

Namespace: `LibGPUDriverInteract`.

The result of a display write the owning policy made. `Applied`: Whether the driver accepted the
setter. `Detail`: A sentence for the log.

| Member                                                  | Contract                                                                                                                                   |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `GpuDisplayWriteResult(Boolean Applied, String Detail)` | The result of a display write the owning policy made. `Applied`: Whether the driver accepted the setter. `Detail`: A sentence for the log. |
| `Boolean Applied { get; init; }`                        | Whether the driver accepted the setter.                                                                                                    |
| `String Detail { get; init; }`                          | A sentence for the log.                                                                                                                    |

## GpuDriver

Namespace: `LibGPUDriverInteract`.

Owns one GPU driver's controls and native lifetime, serialized on one lane. Events run on the
publishing thread. Native writes are never retried.

| Member                                                                                                                                                                                             | Contract                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `event Action<GpuDescriptorSet> DescriptorsChanged`                                                                                                                                                | Raised for a complete detached descriptor replacement.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `event Action<GpuState> StateChanged`                                                                                                                                                              | Raised when an observation changes.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `event Action<GpuStatus> StatusChanged`                                                                                                                                                            | Raised when driver availability changes.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| `ValueTask DisposeAsync()`                                                                                                                                                                         | Awaits complete native cleanup. The driver library remains loaded until owned calls finish.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `GpuDriver Create(GpuVendor vendor, String stateDirectory, Action<GpuLogEntry> diagnostics = null/default)`                                                                                        | Creates a stopped owner. Start opens the native driver.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `IReadOnlyList<GpuVendor> DetectVendors()`                                                                                                                                                         | Detects present Intel, AMD and NVIDIA display adapter vendors without opening driver sessions.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `ValueTask<GpuHealth> StartAsync(CancellationToken cancellationToken = null/default)`                                                                                                              | Opens the native session and discovers controls once. Cancelling a wait does not stop startup.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `ValueTask<Boolean> StopAsync(CancellationToken cancellationToken = null/default)`                                                                                                                 | Closes admission immediately and awaits the owned native call before unloading. False if the caller stops waiting; cleanup continues and a later start awaits it.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `ValueTask<GpuCommandResult> ExecuteAsync(GpuCommand command, CancellationToken cancellationToken = null/default)`                                                                                 | Dispatches one typed request without automatic retry or rollback.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `ValueTask<GpuProfileSyncResult> SyncApplicationProfilesAsync(GpuProfileSync sync, CancellationToken cancellationToken = null/default)`                                                            | Synchronizes the complete desired set of native application profiles.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `ValueTask RefreshTopologyAsync(CancellationToken cancellationToken = null/default)`                                                                                                               | Rediscovers adapters and displays after an explicit topology notification. A lost session reopens here or on the next explicit command, never on a polling pass.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `ValueTask<GpuRefreshRateRange> ReadRefreshRateRangeAsync(GpuDisplayTarget display, CancellationToken cancellationToken = null/default)`                                                           | Reads the range a refresh rate can be fixed in on one display, on the serialized driver lane. The range, or null when this vendor or display cannot fix a rate. Intel only: the monitor's Arc Sync range from ctlGetIntelArcSyncInfoForMonitor.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `ValueTask<GpuRefreshRateResult> SetRefreshRateAsync(GpuDisplayTarget display, Double refreshHz, CancellationToken cancellationToken = null/default)`                                              | Holds one display at a refresh rate without changing its Windows mode. `display`: The display. `refreshHz`: The rate, fractions allowed; clamped to the range the monitor reports. `cancellationToken`: Cancels the wait for the lane; not a dispatched setter. The outcome and the rate the request carried. Intel writes the Custom Arc Sync profile with its minimum and maximum both at the rate. The first write captures the profile in use, and LibGPUDriverInteract.GpuDriver.RestoreRefreshRateAsync(LibGPUDriverInteract.GpuDisplayTarget,System.Threading.CancellationToken), a stop, or the next start after a crash puts it back. A write through an Arc Sync control hands the profile back to the user and ends the fixed rate.                                                     |
| `ValueTask<GpuRefreshRateResult> RestoreRefreshRateAsync(GpuDisplayTarget display, CancellationToken cancellationToken = null/default)`                                                            | Puts back the profile in use before a refresh rate was fixed on one display. Applied when a profile was written back, Unchanged when no rate was fixed.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `ValueTask<Boolean> SetRefreshRateBaselineAsync(GpuDisplayTarget display, GpuArcSyncBaseline baseline, CancellationToken cancellationToken = null/default)`                                        | Supplies the Arc Sync profile a released fixed refresh rate returns to on one display, instead of the profile captured before the first fixed rate. `display`: The display. `baseline`: The profile, or null to return to the captured one again. `cancellationToken`: Cancels the wait for the lane. Whether the display has Arc Sync and took the baseline; false on another vendor or display. For the one policy that owns the Arc Sync profile beneath a fixed rate, such as a variable refresh range it keeps. Nothing is written now: the baseline is what the next release, the session's close or the next start's recovery writes. It lasts for the session.                                                                                                                             |
| `ValueTask<GpuArcSyncState> ReadArcSyncAsync(GpuDisplayTarget display, CancellationToken cancellationToken = null/default)`                                                                        | Reads one display's Arc Sync range and profile, on the serialized driver lane. `display`: The display. `cancellationToken`: Cancels the wait for the lane. The state, or null when this vendor or display has no Arc Sync or the profile could not be read. Intel only: ctlGetIntelArcSyncInfoForMonitor and ctlGetIntelArcSyncProfile.                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `ValueTask<GpuDisplayWriteResult> SetArcSyncProfileAsync(GpuDisplayTarget display, GpuArcSyncBaseline profile, CancellationToken cancellationToken = null/default)`                                | Writes one display's Arc Sync profile for the policy that owns it, not for a user's row. `display`: The display. `profile`: The profile and its values, written as given. `cancellationToken`: Cancels the wait for the lane; not a dispatched setter. Whether the driver accepted it. While this owner holds a fixed refresh rate on the display nothing is written and the result says so: supply the profile through LibGPUDriverInteract.GpuDriver.SetRefreshRateBaselineAsync(LibGPUDriverInteract.GpuDisplayTarget,LibGPUDriverInteract.GpuArcSyncBaseline,System.Threading.CancellationToken) and the release writes it. Unlike a write through an Arc Sync row this does not hand the profile to the user, and turning variable refresh back on returns to it. The write is never retried. |
| `ValueTask<GpuVrrSolutions> ReadVrrSolutionsAsync(GpuDisplayTarget display, CancellationToken cancellationToken = null/default)`                                                                   | Reads Intel's variable refresh frame rate solutions for the adapter driving a display. `display`: The display. `cancellationToken`: Cancels the wait for the lane. The state, or null on another vendor or display, or when the escape failed (logged). Intel only: the driver's private D3DKMT VRR escape, read operation.                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `ValueTask<GpuDisplayWriteResult> SetVrrSolutionAsync(GpuDisplayTarget display, GpuVrrSolution solution, Boolean enabled, CancellationToken cancellationToken = null/default)`                     | Turns one of Intel's variable refresh frame rate solutions on or off for the adapter driving a display. `display`: The display. `solution`: The solution. `enabled`: The request. `cancellationToken`: Cancels the wait for the lane; not a dispatched setter. Whether the driver accepted it. Intel only: the private VRR escape's operations 3 and 4 (low frame rate on, off) and 5 and 6 (high frame rate on, off). The setting is adapter-wide driver state that outlives the process; the caller owns putting it back. The write is never retried.                                                                                                                                                                                                                                            |
| `ValueTask<GpuScaledResolutionCatalog> ReadScaledResolutionsAsync(GpuDisplayTarget display, CancellationToken cancellationToken = null/default)`                                                   | Reads source sizes on the existing serialized driver lane; never activates a display mode.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| `ValueTask<GpuScaledResolutionResult> SetScaledResolutionAsync(GpuScaledResolutionCatalog observed, GpuScaledResolution mode, Boolean remove, CancellationToken cancellationToken = null/default)` | Adds or removes one source size, preserving all other driver data and the active display mode.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `ValueTask<GpuScaledResolutionResult> RestartScaledResolutionDriverAsync(GpuScaledResolutionCatalog observed, CancellationToken cancellationToken = null/default)`                                 | Explicitly restarts only the selected display's still-matching driving adapter. The display can go blank and graphics applications can lose their devices.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |

## GpuHealth

Namespace: `LibGPUDriverInteract`.

Current driver availability.

| Value         | Numeric value | Meaning                                                |
| ------------- | ------------- | ------------------------------------------------------ |
| `Ready`       | `0`           | At least one supported driver control is available.    |
| `Unavailable` | `1`           | No driver session or supported controls are available. |
| `Failed`      | `2`           | Startup or lifecycle failed.                           |

## GpuLogEntry

Namespace: `LibGPUDriverInteract`.

One diagnostic transition delivered to the consumer. `Level`: Severity. `Scope`: Driver area.
`Message`: Plain diagnostic text. `Timestamp`: UTC diagnostic time.

| Member                                                                                   | Contract                                                                                                                                                          |
| ---------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GpuLogEntry(GpuLogLevel Level, String Scope, String Message, DateTimeOffset Timestamp)` | One diagnostic transition delivered to the consumer. `Level`: Severity. `Scope`: Driver area. `Message`: Plain diagnostic text. `Timestamp`: UTC diagnostic time. |
| `GpuLogLevel Level { get; init; }`                                                       | Severity.                                                                                                                                                         |
| `String Scope { get; init; }`                                                            | Driver area.                                                                                                                                                      |
| `String Message { get; init; }`                                                          | Plain diagnostic text.                                                                                                                                            |
| `DateTimeOffset Timestamp { get; init; }`                                                | UTC diagnostic time.                                                                                                                                              |

## GpuLogLevel

Namespace: `LibGPUDriverInteract`.

Diagnostic severity.

| Value   | Numeric value | Meaning                   |
| ------- | ------------- | ------------------------- |
| `Info`  | `0`           | Informational transition. |
| `Warn`  | `1`           | Recoverable failure.      |
| `Error` | `2`           | Operation failure.        |

## GpuPersistence

Namespace: `LibGPUDriverInteract`.

How long a written setting survives.

| Value              | Numeric value | Meaning                                        |
| ------------------ | ------------- | ---------------------------------------------- |
| `DevicePersistent` | `0`           | Stored by the driver or device across restart. |
| `Volatile`         | `1`           | Lost when the session or device restarts.      |

## GpuProfileFailure

Namespace: `LibGPUDriverInteract`.

One refused or uncertain profile entry. `ProfileId`: Consumer profile identity. `Executable`:
Executable file name. `CapabilityId`: Setting identity. `Detail`: Plain diagnostic detail.

| Member                                                                                       | Contract                                                                                                                                                                                 |
| -------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GpuProfileFailure(String ProfileId, String Executable, String CapabilityId, String Detail)` | One refused or uncertain profile entry. `ProfileId`: Consumer profile identity. `Executable`: Executable file name. `CapabilityId`: Setting identity. `Detail`: Plain diagnostic detail. |
| `String ProfileId { get; init; }`                                                            | Consumer profile identity.                                                                                                                                                               |
| `String Executable { get; init; }`                                                           | Executable file name.                                                                                                                                                                    |
| `String CapabilityId { get; init; }`                                                         | Setting identity.                                                                                                                                                                        |
| `String Detail { get; init; }`                                                               | Plain diagnostic detail.                                                                                                                                                                 |

## GpuProfileScope

Namespace: `LibGPUDriverInteract`.

The driver mechanism available to a consumer's application profiles.

| Value                  | Numeric value | Meaning                                                                         |
| ---------------------- | ------------- | ------------------------------------------------------------------------------- |
| `GlobalOnly`           | `0`           | Only global state is available.                                                 |
| `Switched`             | `1`           | The consumer applies global values when its selected application changes.       |
| `NativePerApplication` | `2`           | The driver supports executable-specific values through profile synchronization. |

## GpuProfileSync

Namespace: `LibGPUDriverInteract`.

A complete desired set of native executable profiles. `Revision`: Increasing consumer revision.
`Profiles`: Complete desired native application profile set.

| Member                                                                          | Contract                                                                                                                                                     |
| ------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `GpuProfileSync(Int64 Revision, IReadOnlyList<GpuApplicationProfile> Profiles)` | A complete desired set of native executable profiles. `Revision`: Increasing consumer revision. `Profiles`: Complete desired native application profile set. |
| `Int64 Revision { get; init; }`                                                 | Increasing consumer revision.                                                                                                                                |
| `IReadOnlyList<GpuApplicationProfile> Profiles { get; init; }`                  | Complete desired native application profile set.                                                                                                             |

## GpuProfileSyncResult

Namespace: `LibGPUDriverInteract`.

Outcome of a native profile synchronization. `Written`: Entries written or confirmed. `Removed`:
Previously owned entries removed. `Failures`: Entries refused or left uncertain.

| Member                                                                                          | Contract                                                                                                                                                                          |
| ----------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GpuProfileSyncResult(Int32 Written, Int32 Removed, IReadOnlyList<GpuProfileFailure> Failures)` | Outcome of a native profile synchronization. `Written`: Entries written or confirmed. `Removed`: Previously owned entries removed. `Failures`: Entries refused or left uncertain. |
| `Int32 Written { get; init; }`                                                                  | Entries written or confirmed.                                                                                                                                                     |
| `Int32 Removed { get; init; }`                                                                  | Previously owned entries removed.                                                                                                                                                 |
| `IReadOnlyList<GpuProfileFailure> Failures { get; init; }`                                      | Entries refused or left uncertain.                                                                                                                                                |

## GpuReason

Namespace: `LibGPUDriverInteract`.

A structured failure or availability reason. `Code`: Machine-readable reason. `Detail`: Plain
diagnostic detail. `Retryable`: Whether a fresh user action could succeed later; never authorizes
automatic retry.

| Member                                                                    | Contract                                                                                                                                                                                                         |
| ------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GpuReason(GpuReasonCode Code, String Detail, Boolean Retryable = False)` | A structured failure or availability reason. `Code`: Machine-readable reason. `Detail`: Plain diagnostic detail. `Retryable`: Whether a fresh user action could succeed later; never authorizes automatic retry. |
| `GpuReasonCode Code { get; init; }`                                       | Machine-readable reason.                                                                                                                                                                                         |
| `String Detail { get; init; }`                                            | Plain diagnostic detail.                                                                                                                                                                                         |
| `Boolean Retryable { get; init; }`                                        | Whether a fresh user action could succeed later; never authorizes automatic retry.                                                                                                                               |

## GpuReasonCode

Namespace: `LibGPUDriverInteract`.

The reason a setting or command cannot complete cleanly.

| Value                 | Numeric value | Meaning                                                    |
| --------------------- | ------------- | ---------------------------------------------------------- |
| `DriverUnavailable`   | `0`           | The driver session is unavailable.                         |
| `PrerequisiteMissing` | `1`           | A required driver, DLL or service is missing.              |
| `Quiescing`           | `2`           | Cancellation or lifecycle closed write admission.          |
| `TransportFaulted`    | `3`           | A native transport operation failed.                       |
| `Unsupported`         | `4`           | The driver does not support the requested setting.         |
| `ValueOutOfRange`     | `5`           | The requested value does not match the current descriptor. |

## GpuRefreshRateOutcome

Namespace: `LibGPUDriverInteract`.

What a refresh-rate write or release did.

| Value       | Numeric value | Meaning                                                                          |
| ----------- | ------------- | -------------------------------------------------------------------------------- |
| `Applied`   | `0`           | The driver accepted the setter.                                                  |
| `Unchanged` | `1`           | Nothing needed writing.                                                          |
| `Refused`   | `2`           | The write was refused or the driver answered with a failure; nothing is retried. |

## GpuRefreshRateRange

Namespace: `LibGPUDriverInteract`.

The refresh rates a driver can hold one display at, without a Windows mode change. `Display`: The
display the range belongs to. `MinimumHz`: The lowest rate the monitor reports. `MaximumHz`: The
highest rate the monitor reports. `FixedHz`: The rate this driver owner holds now, or null when none
is fixed.

| Member                                                                                               | Contract                                                                                                                                                                                                                                                                                                          |
| ---------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GpuRefreshRateRange(GpuDisplayTarget Display, Double MinimumHz, Double MaximumHz, Double? FixedHz)` | The refresh rates a driver can hold one display at, without a Windows mode change. `Display`: The display the range belongs to. `MinimumHz`: The lowest rate the monitor reports. `MaximumHz`: The highest rate the monitor reports. `FixedHz`: The rate this driver owner holds now, or null when none is fixed. |
| `GpuDisplayTarget Display { get; init; }`                                                            | The display the range belongs to.                                                                                                                                                                                                                                                                                 |
| `Double MinimumHz { get; init; }`                                                                    | The lowest rate the monitor reports.                                                                                                                                                                                                                                                                              |
| `Double MaximumHz { get; init; }`                                                                    | The highest rate the monitor reports.                                                                                                                                                                                                                                                                             |
| `Double? FixedHz { get; init; }`                                                                     | The rate this driver owner holds now, or null when none is fixed.                                                                                                                                                                                                                                                 |

## GpuRefreshRateResult

Namespace: `LibGPUDriverInteract`.

The result of a refresh-rate write or release. `Outcome`: What happened. `RefreshHz`: The rate now
fixed, after clamping to the monitor's range, or null when none is. `Detail`: A sentence for the
log.

| Member                                                                                  | Contract                                                                                                                                                                                                 |
| --------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GpuRefreshRateResult(GpuRefreshRateOutcome Outcome, Double? RefreshHz, String Detail)` | The result of a refresh-rate write or release. `Outcome`: What happened. `RefreshHz`: The rate now fixed, after clamping to the monitor's range, or null when none is. `Detail`: A sentence for the log. |
| `GpuRefreshRateOutcome Outcome { get; init; }`                                          | What happened.                                                                                                                                                                                           |
| `Double? RefreshHz { get; init; }`                                                      | The rate now fixed, after clamping to the monitor's range, or null when none is.                                                                                                                         |
| `String Detail { get; init; }`                                                          | A sentence for the log.                                                                                                                                                                                  |

## GpuRole

Namespace: `LibGPUDriverInteract`.

The meaning and presentation shape of a driver control.

| Value                 | Numeric value | Meaning                                    |
| --------------------- | ------------- | ------------------------------------------ |
| `Toggle`              | `0`           | A Boolean setting.                         |
| `Choice`              | `1`           | A selection from named values.             |
| `Range`               | `2`           | An integer range.                          |
| `ReadOnly`            | `3`           | An observed value which cannot be changed. |
| `Action`              | `4`           | A one-shot operation.                      |
| `VariableRefreshRate` | `5`           | Variable refresh rate enablement.          |

## GpuScaledResolution

Namespace: `LibGPUDriverInteract`.

A GPU source size, with no refresh rate or signal timing.

| Member                                           | Contract                                                          |
| ------------------------------------------------ | ----------------------------------------------------------------- |
| `GpuScaledResolution(Int32 Width, Int32 Height)` | A GPU source size, with no refresh rate or signal timing.         |
| `Int32 Width { get; init; }`                     |                                                                   |
| `Int32 Height { get; init; }`                    |                                                                   |
| `Boolean Valid { get; }`                         | Whether both dimensions fit the shared source-mode editor limits. |

## GpuScaledResolutionActivation

Namespace: `LibGPUDriverInteract`.

Activation required after an accepted source-mode edit.

| Value           | Numeric value | Meaning                                                                  |
| --------------- | ------------- | ------------------------------------------------------------------------ |
| `Immediate`     | `0`           | No driver restart is needed.                                             |
| `DriverRestart` | `1`           | The stored adapter list needs an explicit driver restart.                |
| `Uncertain`     | `2`           | The write may have completed; inspect before any new explicit operation. |

## GpuScaledResolutionCatalog

Namespace: `LibGPUDriverInteract`.

The current driving adapter and its custom source-size catalog.

| Member                                                                                                                                                                                                                   | Contract                                                        |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------- |
| `GpuScaledResolutionCatalog(GpuDisplayTarget Display, GpuVendor Vendor, String AdapterIdentity, String AdapterName, Boolean OutputSpecific, Boolean Supported, IReadOnlyList<GpuScaledResolution> Modes, String Detail)` | The current driving adapter and its custom source-size catalog. |
| `GpuDisplayTarget Display { get; init; }`                                                                                                                                                                                |                                                                 |
| `GpuVendor Vendor { get; init; }`                                                                                                                                                                                        |                                                                 |
| `String AdapterIdentity { get; init; }`                                                                                                                                                                                  |                                                                 |
| `String AdapterName { get; init; }`                                                                                                                                                                                      |                                                                 |
| `Boolean OutputSpecific { get; init; }`                                                                                                                                                                                  |                                                                 |
| `Boolean Supported { get; init; }`                                                                                                                                                                                       |                                                                 |
| `IReadOnlyList<GpuScaledResolution> Modes { get; init; }`                                                                                                                                                                |                                                                 |
| `String Detail { get; init; }`                                                                                                                                                                                           |                                                                 |

## GpuScaledResolutionResult

Namespace: `LibGPUDriverInteract`.

An edit result; a potentially completed write is never automatically retried.

| Member                                                                                                                  | Contract                                                                      |
| ----------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| `GpuScaledResolutionResult(Boolean Accepted, Boolean Changed, GpuScaledResolutionActivation Activation, String Detail)` | An edit result; a potentially completed write is never automatically retried. |
| `Boolean Accepted { get; init; }`                                                                                       |                                                                               |
| `Boolean Changed { get; init; }`                                                                                        |                                                                               |
| `GpuScaledResolutionActivation Activation { get; init; }`                                                               |                                                                               |
| `String Detail { get; init; }`                                                                                          |                                                                               |

## GpuSection

Namespace: `LibGPUDriverInteract`.

A plain group of related driver controls.

| Member                                                 | Contract                                                                                                                                                                                    |
| ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GpuSection()`                                         |                                                                                                                                                                                             |
| `String SectionId { get; init; }`                      | Stable group identity.                                                                                                                                                                      |
| `String Title { get; init; }`                          | Plain group title.                                                                                                                                                                          |
| `String Description { get; init; }`                    | Optional plain description.                                                                                                                                                                 |
| `GpuSectionKind Kind { get; init; }`                   | Adapter, display or other group hint.                                                                                                                                                       |
| `Int32 SortOrder { get; init; }`                       | Relative group order.                                                                                                                                                                       |
| `IReadOnlyList<GpuCategory> Categories { get; init; }` | Declared categories within the group.                                                                                                                                                       |
| `GpuDisplayTarget DisplayTarget { get; init; }`        | Read-only Windows monitor identity and current route for a physical output section. Null for adapter-wide controls. Friendly titles and capability instance IDs are not monitor identities. |

## GpuSectionKind

Namespace: `LibGPUDriverInteract`.

Simple grouping hint for a driver section.

| Value     | Numeric value | Meaning           |
| --------- | ------------- | ----------------- |
| `Adapter` | `0`           | Adapter settings. |
| `Display` | `1`           | Display settings. |

## GpuState

Namespace: `LibGPUDriverInteract`.

An observation, separate from a consumer's requested setting.

| Member                                      | Contract                                           |
| ------------------------------------------- | -------------------------------------------------- |
| `GpuState()`                                |                                                    |
| `String CapabilityId { get; init; }`        | Setting identity.                                  |
| `String InstanceId { get; init; }`          | Adapter or display discriminator.                  |
| `GpuValue ObservedValue { get; init; }`     | Last read or accepted value, or null when unknown. |
| `Boolean Available { get; init; }`          | Whether this setting can currently be used.        |
| `DateTimeOffset? ObservedAt { get; init; }` | UTC observation time, or null before observation.  |
| `GpuReason Reason { get; init; }`           | Why the state is unavailable or degraded.          |
| `GpuStateQuality Quality { get; init; }`    | Evidence supporting the observed value.            |

## GpuStateQuality

Namespace: `LibGPUDriverInteract`.

The evidence supporting an observed value.

| Value      | Numeric value | Meaning                                                                 |
| ---------- | ------------- | ----------------------------------------------------------------------- |
| `Unknown`  | `0`           | No known value.                                                         |
| `Observed` | `1`           | Read or accepted by a setter without matching independent confirmation. |
| `Verified` | `2`           | Independent readback matched the written value.                         |

## GpuStatus

Namespace: `LibGPUDriverInteract`.

Detached driver availability. `Health`: Current health. `Detail`: Plain diagnostic detail.

| Member                                       | Contract                                                                                   |
| -------------------------------------------- | ------------------------------------------------------------------------------------------ |
| `GpuStatus(GpuHealth Health, String Detail)` | Detached driver availability. `Health`: Current health. `Detail`: Plain diagnostic detail. |
| `GpuHealth Health { get; init; }`            | Current health.                                                                            |
| `String Detail { get; init; }`               | Plain diagnostic detail.                                                                   |

## GpuUnit

Namespace: `LibGPUDriverInteract`.

The numeric value's unit.

| Value     | Numeric value | Meaning           |
| --------- | ------------- | ----------------- |
| `None`    | `0`           | No physical unit. |
| `Percent` | `1`           | Percentage.       |

## GpuValue

Namespace: `LibGPUDriverInteract`.

A typed driver setting value.

| Member                                 | Contract                                                                                                             |
| -------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `GpuValue()`                           |                                                                                                                      |
| `GpuValueKind Kind { get; init; }`     | The field carrying this value.                                                                                       |
| `Boolean? BooleanValue { get; init; }` | The Boolean field.                                                                                                   |
| `Int32? IntegerValue { get; init; }`   | The integer field.                                                                                                   |
| `String ChoiceValue { get; init; }`    | The named option field.                                                                                              |
| `GpuValue None()`                      | Creates a value with no payload. The empty value.                                                                    |
| `GpuValue Boolean(Boolean value)`      | Creates a Boolean value; both values are shared, so a read allocates nothing. `value`: The payload. The typed value. |
| `GpuValue Integer(Int32 value)`        | Creates an integer value. `value`: The payload. The typed value.                                                     |
| `GpuValue Choice(String value)`        | Creates a named choice. `value`: The machine option. The typed value.                                                |

## GpuValueKind

Namespace: `LibGPUDriverInteract`.

The field carrying a typed value.

| Value     | Numeric value | Meaning           |
| --------- | ------------- | ----------------- |
| `None`    | `0`           | No value.         |
| `Boolean` | `1`           | A Boolean value.  |
| `Integer` | `2`           | An integer value. |
| `Choice`  | `3`           | A named choice.   |

## GpuVendor

Namespace: `LibGPUDriverInteract`.

The installed driver family to open.

| Value    | Numeric value | Meaning                         |
| -------- | ------------- | ------------------------------- |
| `Intel`  | `0`           | Intel Graphics Control Library. |
| `Amd`    | `1`           | AMD ADLX.                       |
| `Nvidia` | `2`           | NVIDIA NVAPI and DRS.           |

## GpuVrrSolution

Namespace: `LibGPUDriverInteract`.

One of Intel's variable refresh frame rate solutions.

| Value           | Numeric value | Meaning                                                                          |
| --------------- | ------------- | -------------------------------------------------------------------------------- |
| `LowFrameRate`  | `0`           | The low frame rate solution (LFC), which repeats frames below the panel's range. |
| `HighFrameRate` | `1`           | The high frame rate solution.                                                    |

## GpuVrrSolutions

Namespace: `LibGPUDriverInteract`.

Intel's variable refresh state for the adapter driving a display, from its private VRR escape.
`Supported`: Whether the adapter reports variable refresh support. `Enabled`: Whether variable
refresh is enabled. `LowFrameRate`: Whether the low frame rate solution (LFC) is on.
`HighFrameRate`: Whether the high frame rate solution is on.

| Member                                                                                             | Contract                                                                                                                                                                                                                                                                                                                                        |
| -------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GpuVrrSolutions(Boolean Supported, Boolean Enabled, Boolean LowFrameRate, Boolean HighFrameRate)` | Intel's variable refresh state for the adapter driving a display, from its private VRR escape. `Supported`: Whether the adapter reports variable refresh support. `Enabled`: Whether variable refresh is enabled. `LowFrameRate`: Whether the low frame rate solution (LFC) is on. `HighFrameRate`: Whether the high frame rate solution is on. |
| `Boolean Supported { get; init; }`                                                                 | Whether the adapter reports variable refresh support.                                                                                                                                                                                                                                                                                           |
| `Boolean Enabled { get; init; }`                                                                   | Whether variable refresh is enabled.                                                                                                                                                                                                                                                                                                            |
| `Boolean LowFrameRate { get; init; }`                                                              | Whether the low frame rate solution (LFC) is on.                                                                                                                                                                                                                                                                                                |
| `Boolean HighFrameRate { get; init; }`                                                             | Whether the high frame rate solution is on.                                                                                                                                                                                                                                                                                                     |
