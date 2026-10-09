// SPDX-License-Identifier: MIT

using System.Diagnostics;
using LibGPUDriverInteract.Amd;
using LibGPUDriverInteract.Intel;
using LibGPUDriverInteract.Nvidia;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract;

/// <summary>Owns one installed GPU driver's controls, observations and native lifetime.</summary>
/// <remarks>Events run on the publishing thread. Marshal UI work in the consumer. Native writes are never retried.</remarks>
public sealed class GpuDriver : IAsyncDisposable
{
    private readonly Dictionary<(string, string), string> _changes = [];
    private readonly Action<GpuLogEntry>? _diagnostics;
    private readonly IDriverEngine _engine;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Func<Func<Task<GpuHealth>>, Task<GpuHealth>> _scheduleStartup;
    private readonly Sink _sink;
    private readonly string _stateDirectory;
    private readonly Dictionary<(string, string?), GpuState> _states = [];
    private bool _active;
    private bool _admitted;
    private long _cycle;
    private CancellationTokenSource? _cycleCancellation;
    private GpuDescriptorSet _descriptors = new() { Generation = 0, CycleGeneration = 0 };
    private Task? _disposal;
    private bool _disposed;
    private Task? _retirement;
    private long _retirementRevision;
    private Task<GpuHealth>? _startup;
    private long _startupRevision;
    private GpuStatus _status = new(GpuHealth.Unavailable, "The driver has not started.", 0);

    internal GpuDriver(IDriverEngine engine, string stateDirectory, Action<GpuLogEntry>? diagnostics = null,
        Func<Func<Task<GpuHealth>>, Task<GpuHealth>>? scheduleStartup = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _engine = engine;
        _stateDirectory = Path.GetFullPath(stateDirectory);
        _diagnostics = diagnostics;
        _scheduleStartup = scheduleStartup ?? (operation => Task.Run(operation, CancellationToken.None));
        _sink = new Sink(this);
    }

    /// <summary>Detached, read-only snapshot of the current descriptor set.</summary>
    public GpuDescriptorSet DescriptorSet
    {
        get
        {
            lock (_gate)
            {
                return Snapshot(_descriptors);
            }
        }
    }

    /// <summary>Detached, read-only snapshot of observations for the current descriptors.</summary>
    public IReadOnlyList<GpuState> States
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(_states.Values.ToArray());
            }
        }
    }

    /// <summary>Detached current driver availability.</summary>
    public GpuStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>Monotonic lifecycle cycle; advances before start and resume publications.</summary>
    public long CycleGeneration
    {
        get
        {
            lock (_gate)
            {
                return _cycle;
            }
        }
    }

    /// <summary>Closes admission and queues safe retirement without waiting for an uninterruptible native call.</summary>
    /// <returns>A completed task once cleanup is owned; use stop to confirm retirement.</returns>
    public ValueTask DisposeAsync()
    {
        CloseAdmission();
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _retirementRevision++;
            if (_retirement is null || _retirement.IsCompleted)
            {
                _retirement = Task.Run(RetireAsync, CancellationToken.None);
            }

            var retiring = _retirement;
            // Join the entire retirement task, including its final lane release. One continuation owns disposal.
            _disposal = Task.Run(async () =>
            {
                await retiring.ConfigureAwait(false);
                await _engine.DisposeAsync().ConfigureAwait(false);
            }, CancellationToken.None);
            ObserveFailure(_disposal, "Driver disposal failed.");
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Creates a driver owner; no native driver is opened until start.</summary>
    /// <param name="vendor">Installed driver family.</param>
    /// <param name="stateDirectory">Consumer-owned writable directory for native profile ownership and recovery records.</param>
    /// <param name="diagnostics">Optional diagnostic callback.</param>
    /// <returns>A stopped driver owner.</returns>
    public static GpuDriver Create(GpuVendor vendor, string stateDirectory, Action<GpuLogEntry>? diagnostics = null)
    {
        IDriverEngine engine = vendor switch
        {
            GpuVendor.Intel => new IntelDriverEngine(),
            GpuVendor.Amd => new DriverRuntime("amd", (_, report) => new AdlxSession(report)),
            GpuVendor.Nvidia => new DriverRuntime("nvidia", (directory, report) => new NvSession(directory, report)),
            _ => throw new ArgumentOutOfRangeException(nameof(vendor))
        };
        return new GpuDriver(engine, stateDirectory, diagnostics);
    }

    /// <summary>Raised before a new cycle can publish descriptors.</summary>
    public event Action<long>? CycleStarted;

    /// <summary>Raised after write admission closes and before suspend, stop or disposal reaches the engine.</summary>
    public event Action<long>? AdmissionClosed;

    /// <summary>Raised for a complete detached descriptor replacement.</summary>
    public event Action<GpuDescriptorSet>? DescriptorsChanged;

    /// <summary>Raised for one detached observation.</summary>
    public event Action<GpuState>? StateChanged;

    /// <summary>Raised when driver availability changes.</summary>
    public event Action<GpuStatus>? StatusChanged;

    /// <summary>Opens the driver and starts periodic observations.</summary>
    /// <param name="cancellationToken">Cancels preparatory work and the caller's wait.</param>
    /// <returns>Current driver availability after initial discovery.</returns>
    public async ValueTask<GpuHealth> StartAsync(CancellationToken cancellationToken = default)
    {
        return await WaitForStartupAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes write admission before suspending the native driver session.</summary>
    /// <param name="cancellationToken">Bounds the caller's wait; cannot terminate a native call.</param>
    /// <returns>The lifecycle operation.</returns>
    public async ValueTask SuspendAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await StopAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        _sink.ReportHealth(GpuHealth.Unavailable, "The driver is suspended.");
    }

    /// <summary>Starts a new cycle and reopens a suspended driver.</summary>
    /// <param name="cancellationToken">Cancels preparatory work and the caller's wait.</param>
    /// <returns>The lifecycle operation.</returns>
    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        await WaitForStartupAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes admission and retires the session while retaining DLLs used by owned native calls.</summary>
    /// <param name="cancellationToken">Bounds the caller's wait; retirement continues after cancellation.</param>
    /// <returns>True when retirement completed; false when the caller stopped waiting first.</returns>
    public async ValueTask<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        var retiring = BeginRetirement();
        try
        {
            await retiring.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Dispatches one current-generation request without retry or rollback.</summary>
    /// <param name="command">Request containing both current generations and an active-time deadline.</param>
    /// <param name="cancellationToken">Cancels preparatory work before a setter is dispatched.</param>
    /// <returns>The truthful write outcome.</returns>
    public async ValueTask<GpuCommandResult> ExecuteAsync(GpuCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_gate)
        {
            if (!_admitted || _disposed)
            {
                return CommandResults.Rejected(command, GpuReasonCode.Quiescing, "Driver write admission is closed.");
            }

            if (command.ExpectedCycleGeneration != _cycle ||
                command.ExpectedDescriptorGeneration != _descriptors.Generation)
            {
                return CommandResults.Rejected(command, GpuReasonCode.GenerationChanged,
                    "The driver generations changed.", true);
            }
        }

        using var linked = LinkOperation(cancellationToken);
        return await _engine.ExecuteCommandAsync(command, linked.Token).ConfigureAwait(false);
    }

    /// <summary>Synchronizes the complete desired native application profile set.</summary>
    /// <param name="sync">Consumer-owned profile selection and executable policy.</param>
    /// <param name="cancellationToken">Cancels preparatory work before native mutation.</param>
    /// <returns>Entries written, removed and refused.</returns>
    public async ValueTask<GpuProfileSyncResult> SyncApplicationProfilesAsync(GpuProfileSync sync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sync);
        lock (_gate)
        {
            if (!_admitted || _disposed || sync.CycleGeneration != _cycle)
            {
                return new GpuProfileSyncResult(0, 0,
                    [new GpuProfileFailure("driver", "", "", "The requested driver cycle is not admitted.")]);
            }
        }

        var detached = sync with
        {
            Profiles = Array.AsReadOnly(sync.Profiles.Select(profile => profile with
            {
                Executables = Array.AsReadOnly(profile.Executables.ToArray()),
                Values = Array.AsReadOnly(profile.Values.ToArray())
            }).ToArray())
        };
        using var linked = LinkOperation(cancellationToken);
        return await _engine.SyncApplicationProfilesAsync(detached, linked.Token).ConfigureAwait(false);
    }

    private async ValueTask<GpuHealth> WaitForStartupAsync(CancellationToken cancellationToken)
    {
        Task<GpuHealth> startup;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_startup is null || _startup.IsCompleted || _retirement is { IsCompleted: false }
                || _startupRevision != _retirementRevision)
            {
                // Retain the complete lifecycle operation; the caller only waits for its cancellable projection.
                _startupRevision = _retirementRevision;
                var retirementRevision = _startupRevision;
                _startup = _scheduleStartup(() => StartOwnedAsync(cancellationToken, retirementRevision));
                ObserveFailure(_startup, "Driver startup failed.");
            }

            startup = _startup;
        }

        try
        {
            return await startup.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = BeginRetirement();
            throw;
        }
    }

    private async Task<GpuHealth> StartOwnedAsync(CancellationToken cancellationToken, long retirementRevision)
    {
        await EnterLifecycleAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_gate)
            {
                if (retirementRevision != _retirementRevision)
                {
                    throw new OperationCanceledException("Driver stop superseded queued startup.");
                }
            }

            if (_active && IsAdmitted())
            {
                return Status.Health;
            }

            BeginCycle(retirementRevision);
            using var linked = LinkOperation(cancellationToken);
            try
            {
                // Suspend fully retires the session. Start opens a fresh cycle for every vendor.
                var health = await _engine.StartAsync(_sink, Context(), linked.Token).ConfigureAwait(false);
                _active = true;
                if (IsAdmitted())
                {
                    _sink.ReportHealth(health, Status.Detail);
                }

                return health;
            }
            catch
            {
                CloseAdmission();
                _sink.ReportHealth(GpuHealth.Failed, "Driver startup did not complete.");
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private Task BeginRetirement()
    {
        CloseAdmission();
        Task retiring;
        lock (_gate)
        {
            _retirementRevision++;
            if (_disposal is not null)
            {
                return _disposal;
            }

            if (_retirement is null || _retirement.IsCompleted)
            {
                _retirement = Task.Run(RetireAsync, CancellationToken.None);
            }

            retiring = _retirement;
        }

        CloseAdmission();
        return retiring;
    }

    private void ObserveFailure(Task operation, string message)
    {
        _ = operation.ContinueWith(task => Log(GpuLogLevel.Error, "lifecycle", message + " " + task.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task EnterLifecycleAsync(CancellationToken token)
    {
        while (true)
        {
            Task? retirement;
            lock (_gate)
            {
                retirement = _retirement;
            }

            if (retirement is not null)
            {
                await retirement.WaitAsync(token).ConfigureAwait(false);
            }

            await _lifecycle.WaitAsync(token).ConfigureAwait(false);
            lock (_gate)
            {
                if (_retirement is null || _retirement.IsCompleted)
                {
                    return;
                }
            }

            _lifecycle.Release();
        }
    }

    private async Task RetireAsync()
    {
        await _lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await _engine.StopAsync(Context(), CancellationToken.None).ConfigureAwait(false);
            _active = false;
            var descriptors = DescriptorSet;
            if (descriptors.Descriptors.Count != 0 || descriptors.Sections.Count != 0)
            {
                await _sink.PublishDescriptorsAsync(new GpuDescriptorSet
                {
                    Generation = descriptors.Generation + 1, CycleGeneration = descriptors.CycleGeneration
                }, CancellationToken.None).ConfigureAwait(false);
            }

            lock (_gate)
            {
                _states.Clear();
            }

            _sink.ReportHealth(GpuHealth.Unavailable, "The driver is stopped.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void BeginCycle(long retirementRevision)
    {
        CancellationTokenSource? previous;
        long cycle;
        lock (_gate)
        {
            if (_retirement is { IsCompleted: false } || retirementRevision != _retirementRevision)
            {
                throw new OperationCanceledException("Driver stop superseded startup.");
            }

            previous = _cycleCancellation;
            _cycleCancellation = new CancellationTokenSource();
            cycle = ++_cycle;
            _admitted = true;
            _states.Clear();
            _descriptors = new GpuDescriptorSet { Generation = 0, CycleGeneration = cycle };
            _status = new GpuStatus(GpuHealth.Unavailable, "The driver is starting.", cycle);
        }

        previous?.Dispose();
        Notify(CycleStarted, cycle);
    }

    private void CloseAdmission()
    {
        CancellationTokenSource? cancellation;
        long cycle;
        lock (_gate)
        {
            if (!_admitted)
            {
                return;
            }

            _admitted = false;
            cancellation = _cycleCancellation;
            cycle = _cycle;
        }

        Notify(AdmissionClosed, cycle);
        cancellation?.Cancel();
    }

    private bool IsAdmitted()
    {
        lock (_gate)
        {
            return _admitted;
        }
    }

    private DriverContext Context()
    {
        return new DriverContext(CycleGeneration, DriverDeadline.Never, _stateDirectory);
    }

    private CancellationTokenSource LinkOperation(CancellationToken token)
    {
        lock (_gate)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(token,
                _cycleCancellation?.Token ?? new CancellationToken(true));
        }
    }

    private static GpuDescriptorSet Snapshot(GpuDescriptorSet set)
    {
        return set with
        {
            Sections = Array.AsReadOnly(set.Sections.Select(section => section with
            {
                Categories = Array.AsReadOnly(section.Categories.ToArray())
            }).ToArray()),
            Descriptors = Array.AsReadOnly(set.Descriptors.Select(descriptor => descriptor with
            {
                Choices = Array.AsReadOnly(descriptor.Choices.ToArray())
            }).ToArray())
        };
    }

    private void Notify<T>(Action<T>? handlers, T value)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(value);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Log(GpuLogLevel.Warn, "events", error.Message);
            }
        }
    }

    private void Log(GpuLogLevel level, string scope, string message)
    {
        try
        {
            _diagnostics?.Invoke(new GpuLogEntry(level, scope, message, DateTimeOffset.UtcNow));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Trace.TraceError("GPU diagnostic callback failed: {0}", error);
        }
    }

    private sealed class Sink(GpuDriver owner) : IDriverSink
    {
        public long CycleGeneration => owner.CycleGeneration;

        public ValueTask PublishDescriptorsAsync(GpuDescriptorSet descriptors, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detached = Snapshot(descriptors);
            lock (owner._gate)
            {
                if (descriptors.CycleGeneration != owner._cycle || descriptors.Generation <=
                                                                owner._descriptors.Generation
                                                                || (!owner._admitted &&
                                                                    descriptors.Descriptors.Count != 0))
                {
                    return ValueTask.CompletedTask;
                }

                owner._descriptors = detached;
                owner._states.Clear();
            }

            owner.Notify(owner.DescriptorsChanged, detached);
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishCapabilityStateAsync(GpuState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (owner._gate)
            {
                if (!owner._admitted || state.CycleGeneration != owner._cycle
                                     || state.DescriptorGeneration != owner._descriptors.Generation
                                     || !owner._descriptors.Descriptors.Any(descriptor =>
                                         descriptor.CapabilityId == state.CapabilityId
                                         && descriptor.InstanceId == state.InstanceId))
                {
                    return ValueTask.CompletedTask;
                }

                owner._states[(state.CapabilityId, state.InstanceId)] = state;
            }

            owner.Notify(owner.StateChanged, state);
            return ValueTask.CompletedTask;
        }

        public void ReportHealth(GpuHealth health, string detail)
        {
            GpuStatus status;
            lock (owner._gate)
            {
                if (!owner._admitted && health == GpuHealth.Ready)
                {
                    return;
                }

                status = new GpuStatus(health, detail, owner._cycle);
                if (status == owner._status)
                {
                    return;
                }

                owner._status = status;
            }

            owner.Notify(owner.StatusChanged, status);
        }

        public void Log(GpuLogLevel level, string scope, string message)
        {
            owner.Log(level, scope, message);
        }

        public void LogChange(GpuLogLevel level, string scope, string key, string message)
        {
            lock (owner._gate)
            {
                if (owner._changes.TryGetValue((scope, key), out var previous) && previous == message)
                {
                    return;
                }

                owner._changes[(scope, key)] = message;
            }

            owner.Log(level, scope, message);
        }
    }
}
