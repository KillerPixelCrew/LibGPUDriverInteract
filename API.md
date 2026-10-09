# Public API

All public types use namespace `LibGPUDriverInteract`. Native driver sessions, engines and
publication sinks are internal.

```csharp
GpuDriver GpuDriver.Create(GpuVendor vendor, string stateDirectory,
    Action<GpuLogEntry>? diagnostics = null);
ValueTask<GpuHealth> StartAsync(CancellationToken cancellationToken = default);
ValueTask SuspendAsync(CancellationToken cancellationToken = default);
ValueTask ResumeAsync(CancellationToken cancellationToken = default);
ValueTask<bool> StopAsync(CancellationToken cancellationToken = default);
ValueTask<GpuCommandResult> ExecuteAsync(GpuCommand command,
    CancellationToken cancellationToken = default);
ValueTask<GpuProfileSyncResult> SyncApplicationProfilesAsync(GpuProfileSync sync,
    CancellationToken cancellationToken = default);
ValueTask DisposeAsync();

GpuDescriptorSet DescriptorSet { get; }
IReadOnlyList<GpuState> States { get; }
GpuStatus Status { get; }
long CycleGeneration { get; }

event Action<long>? CycleStarted;
event Action<long>? AdmissionClosed;
event Action<GpuDescriptorSet>? DescriptorsChanged;
event Action<GpuState>? StateChanged;
event Action<GpuStatus>? StatusChanged;
```

Events run synchronously on the publishing thread; marshal UI work in the caller. Subscriber
exceptions are logged and do not change the driver operation. Getters and publications contain
detached, read-only collection snapshots. A cycle starts before its first descriptor publication and
increases on start after stop and on resume. Admission closes before suspend, stop or disposal
reaches the engine. Commands must match both the current cycle and descriptor generation.

Records with many fields (`GpuDescriptor`, `GpuSection`, `GpuCategory`, `GpuDescriptorSet`,
`GpuValue`, `GpuState`, `GpuCommand`, `GpuCommandResult`) use init properties. `GpuCommand.Deadline`
has type `DriverDeadline`; its factories and cancellation source use a process-local monotonic clock
which clamps long sleep/scheduling gaps. A deadline cannot terminate a native call.

Positional records are `GpuChoice(Value, Label)`, `GpuReason(Code, Detail, Retryable = false)`,
`GpuStatus(Health, Detail, CycleGeneration)`, `GpuLogEntry(Level, Scope, Message, Timestamp)`,
`GpuProfileSync(Revision, CycleGeneration, Profiles)`,
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
`AppliedVerified`. `Indeterminate` and `TimedOut` never authorize automatic retries. Stop returns
false if cancellation bounds its wait before retirement completes; the owned native call and DLL
remain alive until safe cleanup.
