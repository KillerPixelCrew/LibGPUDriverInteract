// SPDX-License-Identifier: MIT

using System.Diagnostics;
using LibGPUDriverInteract.Amd;
using LibGPUDriverInteract.Intel;
using LibGPUDriverInteract.Nvidia;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract;

/// <summary>Owns one GPU driver's controls and native lifetime, serialized on one lane.</summary>
/// <remarks>Events run on the publishing thread. Native writes are never retried.</remarks>
public sealed partial class GpuDriver : IAsyncDisposable
{
    private readonly Dictionary<(string, string), string> _changes = [];
    private readonly Action<GpuLogEntry>? _diagnostics;
    private readonly SemaphoreSlim _lane = new(1, 1);
    private readonly DriverRuntime _runtime;
    private readonly Sink _sink;
    private readonly string _stateDirectory;
    private readonly Lock _stateGate = new();
    private volatile bool _active;
    private CancellationTokenSource? _cancellation;
    private Task? _disposal;
    private bool _disposed;
    private Task? _observation;
    private Task<GpuHealth>? _startup;
    private GpuStatus _status = new(GpuHealth.Unavailable, "The driver has not started.");
    private Task? _stop;
    private CancellationTokenSource? _stopCancellation;

    internal GpuDriver(DriverRuntime runtime, string stateDirectory, Action<GpuLogEntry>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _runtime = runtime;
        _stateDirectory = Path.GetFullPath(stateDirectory);
        _diagnostics = diagnostics;
        _sink = new Sink(this);
    }

    /// <summary>Awaits complete native cleanup. The driver library remains loaded until owned calls finish.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_stateGate)
        {
            _disposed = true;
            return new ValueTask(_disposal ??= QueueStop());
        }
    }

    /// <summary>Creates a stopped owner. Start opens the native driver.</summary>
    public static GpuDriver Create(GpuVendor vendor, string stateDirectory, Action<GpuLogEntry>? diagnostics = null)
    {
        var runtime = vendor switch
        {
            GpuVendor.Intel => new DriverRuntime("intel", (directory, sink) => new IntelSession(directory, sink)),
            GpuVendor.Amd => new DriverRuntime("amd", (_, sink) => new AdlxSession((key, detail) =>
                sink.LogChange(GpuLogLevel.Warn, "amd", key, detail), message =>
                sink.Log(GpuLogLevel.Info, "amd", message))),
            GpuVendor.Nvidia => new DriverRuntime("nvidia", (directory, sink) => new NvSession(directory,
                (key, detail) => sink.LogChange(GpuLogLevel.Warn, "nvidia", key, detail))),
            _ => throw new ArgumentOutOfRangeException(nameof(vendor))
        };
        return new GpuDriver(runtime, stateDirectory, diagnostics);
    }

    /// <summary>Detects present Intel, AMD and NVIDIA display adapter vendors without opening driver sessions.</summary>
    public static IReadOnlyList<GpuVendor> DetectVendors()
    {
        return WindowsGpuVendors.Collect();
    }

    /// <summary>Raised for a complete detached descriptor replacement.</summary>
    public event Action<GpuDescriptorSet>? DescriptorsChanged;

    /// <summary>Raised when an observation changes.</summary>
    public event Action<GpuState>? StateChanged;

    /// <summary>Raised when driver availability changes.</summary>
    public event Action<GpuStatus>? StatusChanged;

    /// <summary>Opens the native session and discovers controls once. Cancelling a wait does not stop startup.</summary>
    public async ValueTask<GpuHealth> StartAsync(CancellationToken cancellationToken = default)
    {
        Task<GpuHealth> startup;
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active && _startup is { IsCompleted: true })
            {
                return _status.Health;
            }

            if (_startup is null || _startup.IsCompleted || _cancellation?.IsCancellationRequested == true)
            {
                var previousStop = _stop;
                var cancellation = _cancellation = new CancellationTokenSource();
                _startup = Task.Run(() => StartOwnedAsync(previousStop, cancellation), CancellationToken.None);
            }

            startup = _startup;
        }

        return await startup.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes admission immediately and awaits the owned native call before unloading.</summary>
    /// <returns>False if the caller stops waiting; cleanup continues and a later start awaits it.</returns>
    public async ValueTask<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        var stop = QueueStop();
        try
        {
            await stop.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Dispatches one typed request without automatic retry or rollback.</summary>
    public async ValueTask<GpuCommandResult> ExecuteAsync(GpuCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await EnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return CommandResults.Rejected(command, GpuReasonCode.Quiescing,
                "Driver admission is closed or the command was cancelled before dispatch.", true);
        }

        try
        {
            return await Task.Run(() => _runtime.Execute(command, cancellationToken), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CommandResults.Rejected(command, GpuReasonCode.Quiescing,
                "The command was cancelled before dispatch.", true);
        }
        finally
        {
            _lane.Release();
        }
    }

    /// <summary>Synchronizes the complete desired set of native application profiles.</summary>
    public async ValueTask<GpuProfileSyncResult> SyncApplicationProfilesAsync(GpuProfileSync sync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sync);
        if (!await EnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return DriverRuntime.SyncUnavailable(sync, "Driver admission is closed or profile sync was cancelled.");
        }

        try
        {
            var detached = sync with
            {
                Profiles = sync.Profiles.Select(profile => profile with
                {
                    Executables = profile.Executables.ToArray(), Values = profile.Values.ToArray()
                }).ToArray()
            };
            return await Task.Run(() => _runtime.Sync(detached, cancellationToken), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DriverRuntime.SyncUnavailable(sync, "Profile synchronization was cancelled.");
        }
        finally
        {
            _lane.Release();
        }
    }

    /// <summary>Rediscovers adapters and displays after an explicit topology notification.</summary>
    /// <remarks>A lost session reopens here or on the next explicit command, never on a polling pass.</remarks>
    public async ValueTask RefreshTopologyAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await Task.Run(() => _runtime.RefreshTopology(cancellationToken), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _lane.Release();
        }
    }

    private async Task<GpuHealth> StartOwnedAsync(Task? previousStop, CancellationTokenSource cancellation)
    {
        if (previousStop is not null)
        {
            await previousStop.ConfigureAwait(false);
        }

        await _lane.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            lock (_stateGate)
            {
                if (cancellation.IsCancellationRequested || _disposed)
                {
                    return GpuHealth.Unavailable;
                }

                _active = true;
            }

            _sink.ReportHealth(GpuHealth.Unavailable, "The driver is starting.");
            var health = await Task.Run(() => _runtime.Start(_sink, _stateDirectory, () => _active,
                cancellation.Token), CancellationToken.None).ConfigureAwait(false);
            lock (_stateGate)
            {
                if (_active)
                {
                    _observation = ObserveLoopAsync(cancellation.Token);
                }
            }

            return health;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _active = false;
            await Task.Run(_runtime.Close, CancellationToken.None).ConfigureAwait(false);
            return GpuHealth.Unavailable;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _active = false;
            await Task.Run(_runtime.Close, CancellationToken.None).ConfigureAwait(false);
            _sink.ReportHealth(GpuHealth.Failed, "Driver startup failed: " + error.Message);
            return GpuHealth.Failed;
        }
        finally
        {
            _lane.Release();
        }
    }

    private Task QueueStop()
    {
        lock (_stateGate)
        {
            _active = false;
            if (_stop is not null && _stopCancellation == _cancellation)
            {
                return _stop;
            }

            _cancellation?.Cancel();
            if (_stop is null || _stopCancellation != _cancellation)
            {
                var startup = _startup;
                var observation = _observation;
                var cancellation = _stopCancellation = _cancellation;
                _stop = Task.Run(async () =>
                {
                    if (startup is not null)
                    {
                        await startup.ConfigureAwait(false);
                    }

                    if (observation is not null)
                    {
                        await observation.ConfigureAwait(false);
                    }

                    await _lane.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        await Task.Run(_runtime.Close, CancellationToken.None).ConfigureAwait(false);
                    }
                    finally
                    {
                        _sink.PublishDescriptorsAsync(new GpuDescriptorSet(), CancellationToken.None)
                            .GetAwaiter().GetResult();
                        _sink.ReportHealth(GpuHealth.Unavailable, "The driver is stopped.");
                        cancellation?.Dispose();
                        _lane.Release();
                    }
                }, CancellationToken.None);
            }

            return _stop;
        }
    }

    private async ValueTask<bool> EnterCommandAsync(CancellationToken token)
    {
        if (!_active || _disposed || token.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            await _lane.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }

        if (_active && !_disposed && !token.IsCancellationRequested)
        {
            return true;
        }

        _lane.Release();
        return false;
    }

    private async Task ObserveLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                await _lane.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (_active)
                    {
                        await Task.Run(() => _runtime.Observe(token), CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _lane.Release();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Log(GpuLogLevel.Error, "observe", error.Message);
        }
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
        public ValueTask PublishDescriptorsAsync(GpuDescriptorSet set, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (owner._active || set.Descriptors.Count == 0)
            {
                owner.Notify(owner.DescriptorsChanged, set with
                {
                    Sections = Array.AsReadOnly(set.Sections.Select(section => section with
                    {
                        Categories = Array.AsReadOnly(section.Categories.ToArray())
                    }).ToArray()),
                    Descriptors = Array.AsReadOnly(set.Descriptors.Select(descriptor => descriptor with
                    {
                        Choices = Array.AsReadOnly(descriptor.Choices.ToArray())
                    }).ToArray())
                });
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask PublishCapabilityStateAsync(GpuState state, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (owner._active)
            {
                owner.Notify(owner.StateChanged, state);
            }

            return ValueTask.CompletedTask;
        }

        public void ReportHealth(GpuHealth health, string detail)
        {
            var status = new GpuStatus(health, detail);
            lock (owner._stateGate)
            {
                if ((!owner._active && health == GpuHealth.Ready) || owner._status == status)
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
            lock (owner._stateGate)
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
