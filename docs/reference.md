# Complete API reference

Public types, declared members and enum values from the library's compiled metadata and XML
documentation. Native internals and compiler-generated record helpers are omitted. See
[integration guides](README.md) for usage and ownership.

- [GpuApplicationProfile](#gpuapplicationprofile)
- [GpuApplicationValue](#gpuapplicationvalue)
- [GpuApplyTiming](#gpuapplytiming)
- [GpuCategory](#gpucategory)
- [GpuChoice](#gpuchoice)
- [GpuCommand](#gpucommand)
- [GpuCommandOutcome](#gpucommandoutcome)
- [GpuCommandResult](#gpucommandresult)
- [GpuDescriptor](#gpudescriptor)
- [GpuDescriptorSet](#gpudescriptorset)
- [GpuDisplayTarget](#gpudisplaytarget)
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
- [GpuRole](#gpurole)
- [GpuSection](#gpusection)
- [GpuSectionKind](#gpusectionkind)
- [GpuState](#gpustate)
- [GpuStateQuality](#gpustatequality)
- [GpuStatus](#gpustatus)
- [GpuUnit](#gpuunit)
- [GpuValue](#gpuvalue)
- [GpuValueKind](#gpuvaluekind)
- [GpuVendor](#gpuvendor)

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

## GpuDriver

Namespace: `LibGPUDriverInteract`.

Owns one GPU driver's controls and native lifetime, serialized on one lane. Events run on the
publishing thread. Native writes are never retried.

| Member                                                                                                                                  | Contract                                                                                                                                                          |
| --------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `event Action<GpuDescriptorSet> DescriptorsChanged`                                                                                     | Raised for a complete detached descriptor replacement.                                                                                                            |
| `event Action<GpuState> StateChanged`                                                                                                   | Raised when an observation changes.                                                                                                                               |
| `event Action<GpuStatus> StatusChanged`                                                                                                 | Raised when driver availability changes.                                                                                                                          |
| `ValueTask DisposeAsync()`                                                                                                              | Awaits complete native cleanup. The driver library remains loaded until owned calls finish.                                                                       |
| `GpuDriver Create(GpuVendor vendor, String stateDirectory, Action<GpuLogEntry> diagnostics = null/default)`                             | Creates a stopped owner. Start opens the native driver.                                                                                                           |
| `IReadOnlyList<GpuVendor> DetectVendors()`                                                                                              | Detects present Intel, AMD and NVIDIA display adapter vendors without opening driver sessions.                                                                    |
| `ValueTask<GpuHealth> StartAsync(CancellationToken cancellationToken = null/default)`                                                   | Opens the native session and discovers controls once. Cancelling a wait does not stop startup.                                                                    |
| `ValueTask<Boolean> StopAsync(CancellationToken cancellationToken = null/default)`                                                      | Closes admission immediately and awaits the owned native call before unloading. False if the caller stops waiting; cleanup continues and a later start awaits it. |
| `ValueTask<GpuCommandResult> ExecuteAsync(GpuCommand command, CancellationToken cancellationToken = null/default)`                      | Dispatches one typed request without automatic retry or rollback.                                                                                                 |
| `ValueTask<GpuProfileSyncResult> SyncApplicationProfilesAsync(GpuProfileSync sync, CancellationToken cancellationToken = null/default)` | Synchronizes the complete desired set of native application profiles.                                                                                             |
| `ValueTask RefreshTopologyAsync(CancellationToken cancellationToken = null/default)`                                                    | Rediscovers adapters and displays after an explicit topology notification. A lost session reopens here or on the next explicit command, never on a polling pass.  |

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

| Member                                 | Contract                                                              |
| -------------------------------------- | --------------------------------------------------------------------- |
| `GpuValue()`                           |                                                                       |
| `GpuValueKind Kind { get; init; }`     | The field carrying this value.                                        |
| `Boolean? BooleanValue { get; init; }` | The Boolean field.                                                    |
| `Int32? IntegerValue { get; init; }`   | The integer field.                                                    |
| `String ChoiceValue { get; init; }`    | The named option field.                                               |
| `GpuValue None()`                      | Creates a value with no payload. The empty value.                     |
| `GpuValue Boolean(Boolean value)`      | Creates a Boolean value. `value`: The payload. The typed value.       |
| `GpuValue Integer(Int32 value)`        | Creates an integer value. `value`: The payload. The typed value.      |
| `GpuValue Choice(String value)`        | Creates a named choice. `value`: The machine option. The typed value. |

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
