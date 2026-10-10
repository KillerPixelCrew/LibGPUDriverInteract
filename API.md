# Public API

All public types use namespace `LibGPUDriverInteract`. Native driver sessions, engines and
publication sinks are internal.

```csharp
GpuDriver GpuDriver.Create(GpuVendor vendor, string stateDirectory,
    Action<GpuLogEntry>? diagnostics = null);
IReadOnlyList<GpuVendor> GpuDriver.DetectVendors();
ValueTask<GpuHealth> StartAsync(CancellationToken cancellationToken = default);
ValueTask<bool> StopAsync(CancellationToken cancellationToken = default);
ValueTask<GpuCommandResult> ExecuteAsync(GpuCommand command,
    CancellationToken cancellationToken = default);
ValueTask<GpuProfileSyncResult> SyncApplicationProfilesAsync(GpuProfileSync sync,
    CancellationToken cancellationToken = default);
ValueTask RefreshTopologyAsync(CancellationToken cancellationToken = default);
ValueTask DisposeAsync();

event Action<GpuDescriptorSet>? DescriptorsChanged;
event Action<GpuState>? StateChanged;
event Action<GpuStatus>? StatusChanged;
```

Events run synchronously on the publishing thread; marshal UI work in the caller. Subscriber
exceptions are logged and do not change the driver operation. Getters and publications contain
detached, read-only collection snapshots. One owner serializes native work and closes admission
before stop or disposal. Cancelling a startup wait does not cancel the owner's initialization. Sleep
policy belongs to the consumer, which can pause publication and commands while retaining the native
session. Polling observes existing controls; an explicit topology signal rediscovers adapters and
outputs. Lost sessions reopen only after an explicit topology signal or user command.

Records with many fields (`GpuDescriptor`, `GpuSection`, `GpuCategory`, `GpuDescriptorSet`,
`GpuValue`, `GpuState`, `GpuCommand`, `GpuCommandResult`) use init properties. Commands contain
`CapabilityId`, `InstanceId` and `RequestedValue`; cancellation is supplied to the operation. There
are no lifecycle generations, command IDs or library deadline clocks. Cancellation cannot terminate
a native call or prove that a dispatched write did not complete.

Positional records are `GpuChoice(Value, Label)`, `GpuReason(Code, Detail, Retryable = false)`,
`GpuStatus(Health, Detail)`, `GpuLogEntry(Level, Scope, Message, Timestamp)`,
`GpuProfileSync(Revision, Profiles)`,
`GpuApplicationProfile(ProfileId, DisplayName, Executables, Values)`,
`GpuApplicationValue(CapabilityId, InstanceId, Value)`,
`GpuProfileSyncResult(Written, Removed, Failures)`, and
`GpuProfileFailure(ProfileId, Executable, CapabilityId, Detail)`.

`NativePerApplication` is a native profile support hint. The consumer owns profile selection and
submits the complete desired executable/profile set. `Switched` means the consumer applies a value
when selecting an application. `GlobalOnly` means the driver offers no application-specific
mechanism.

An accepted setter remains `AppliedUnverified` when readback is absent, fails or differs, and its
requested value is published as observed. Only matching independent readback returns
`AppliedVerified`. `Indeterminate` never authorizes automatic retries. Written observations remain
visible until native confirmation or a later independent change. Failed observations do not hide
writable controls. Startup and topology discovery use native capability queries without dispatching
setters to test support. Writing an identical display value can retrain the link, so only explicit
commands apply display settings. Stop returns false if cancellation bounds its wait before
retirement completes; the owned native call and DLL remain alive until safe cleanup.

Physical output sections carry `GpuSection.DisplayTarget`. Its exact monitor `DevicePath` and
`EdidIdentity` describe the physical monitor, while adapter LUID/target id and `SourceName` describe
the current Windows route. The driver-native output id is diagnostic metadata. Routes may change
after reboot or hotplug; resolve remembered display choices from monitor identity against the latest
descriptor set. Adapter-wide display policies have no physical target. Discovery includes connected
inactive monitors when the native interface resolves them; duplicate EDID serials are not guessed
into a route by their friendly label.
