// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract.Runtime;

internal sealed record DriverModel(IReadOnlyList<GpuSection> Sections, IReadOnlyList<DriverControl> Controls);

/// <summary>A native session accessed only while GpuDriver owns its single lane.</summary>
internal interface IDriverSession : IDisposable
{
    void Initialize(WriteAdmission admission, CancellationToken token)
    {
    }

    void BeginPass();
    DriverModel Discover();
    GpuProfileSyncResult Sync(GpuProfileSync sync, WriteAdmission admission, CancellationToken token);
}

internal sealed class DriverFailure(string message, bool attempted = false, bool lost = false) : Exception(message)
{
    internal bool Attempted { get; } = attempted;
    internal bool Lost { get; } = lost;
}

internal abstract class DriverControl(GpuDescriptor descriptor)
{
    internal GpuDescriptor Descriptor { get; } = descriptor;
    internal string Key { get; } = DriverIds.Key(descriptor.CapabilityId, descriptor.InstanceId);
    internal abstract GpuValue Read();
    internal abstract void Write(GpuValue value, WriteAdmission admission);

    internal virtual bool Accepts(GpuValue? value)
    {
        return DriverDescriptors.Accepts(Descriptor, value);
    }
}

/// <summary>Discovery, observations and sticky written state shared by all three driver sessions.</summary>
/// <remarks>GpuDriver owns serialization and lifetime. This class has no separate gate, loop or retirement task.</remarks>
internal sealed class DriverRuntime(string id, Func<string, IDriverSink, IDriverSession> open)
{
    private readonly Dictionary<string, GpuState> _published = [];
    private readonly Dictionary<string, WrittenValue> _written = [];
    private Func<bool> _active = () => false;
    private GpuDescriptorSet _descriptors = new();
    private DriverModel _model = new([], []);
    private IDriverSession? _session;
    private IDriverSink? _sink;
    private string _stateDirectory = "";

    internal GpuHealth Start(IDriverSink sink, string stateDirectory, Func<bool> active, CancellationToken token)
    {
        _sink = sink;
        _stateDirectory = stateDirectory;
        _active = active;
        _published.Clear();
        _written.Clear();
        _descriptors = new GpuDescriptorSet();
        Open(token);
        return _session is null || _model.Controls.Count == 0 ? GpuHealth.Unavailable : GpuHealth.Ready;
    }

    internal void RefreshTopology(CancellationToken token)
    {
        if (_session is null)
        {
            Open(token);
            return;
        }

        try
        {
            _session.BeginPass();
            // Discovery is read-only. Keep replacement handles even when the caller cancels observation.
            _model = _session.Discover();
            PublishModel(_model, CancellationToken.None);
            Observe(token);
            if (_session is null)
            {
                ReopenAfterReadLoss(token);
                return;
            }

            _sink!.ReportHealth(_model.Controls.Count == 0 ? GpuHealth.Unavailable : GpuHealth.Ready,
                _model.Controls.Count == 0
                    ? "The driver reported no supported GPU controls."
                    : "Driver controls available.");
        }
        catch (DriverFailure failure) when (failure.Lost)
        {
            Lose(failure.Message);
            if (!failure.Attempted)
            {
                ReopenAfterReadLoss(token);
            }
        }
    }

    private void ReopenAfterReadLoss(CancellationToken token)
    {
        if (_active() && !token.IsCancellationRequested)
        {
            Open(token);
        }
    }

    internal void Observe(CancellationToken token)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            _session.BeginPass();
            foreach (var control in _model.Controls)
            {
                token.ThrowIfCancellationRequested();
                GpuValue? value = null;
                string? error = null;
                try
                {
                    value = control.Read();
                }
                catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException
                                                    and not DriverFailure { Lost: true })
                {
                    error = failure.Message;
                    _sink!.LogChange(GpuLogLevel.Warn, id, "read/" + control.Key, error);
                }

                var quality = value is null ? GpuStateQuality.Unknown : GpuStateQuality.Observed;
                if (_written.TryGetValue(control.Key, out var written))
                {
                    if (value is null || value == written.Readback || value == written.Value ||
                        written.Readback is null)
                    {
                        if (value is not null && (value == written.Value || written.Readback is null))
                        {
                            _written[control.Key] = written with { Readback = value };
                        }

                        quality = control.Descriptor.SupportsRead && value == written.Value
                            ? GpuStateQuality.Verified
                            : GpuStateQuality.Observed;
                        value = written.Value;
                    }
                    else
                    {
                        _written.Remove(control.Key);
                    }
                }

                PublishValue(control, value, error, quality, token);
            }
        }
        catch (DriverFailure failure) when (failure.Lost)
        {
            Lose(failure.Message);
        }
    }

    internal GpuCommandResult Execute(GpuCommand command, CancellationToken token)
    {
        if (_session is null)
        {
            Open(token);
        }

        if (_session is not { } session)
        {
            return CommandResults.Rejected(command, GpuReasonCode.DriverUnavailable, "The GPU driver is not open.");
        }

        var control = _model.Controls.FirstOrDefault(item => item.Descriptor.CapabilityId == command.CapabilityId
                                                             && item.Descriptor.InstanceId == command.InstanceId);
        if (control is null)
        {
            return CommandResults.Rejected(command, GpuReasonCode.Unsupported,
                $"{command.CapabilityId} is not published for {command.InstanceId}.");
        }

        if (!control.Accepts(command.RequestedValue))
        {
            return CommandResults.Rejected(command, GpuReasonCode.ValueOutOfRange,
                "The value is not offered by this GPU control.");
        }

        var requested = command.RequestedValue ?? GpuValue.None();
        var admission = new WriteAdmission(token, _active);
        try
        {
            session.BeginPass();
            admission.Check();
            control.Write(requested, admission);
        }
        catch (DriverFailure failure)
        {
            if (failure.Lost)
            {
                Lose(failure.Message);
            }

            return failure.Attempted
                ? CommandResults.Indeterminate(command, GpuReasonCode.TransportFaulted, failure.Message)
                : CommandResults.Rejected(command, !admission.Admitted ? GpuReasonCode.Quiescing
                    : failure.Lost ? GpuReasonCode.DriverUnavailable : GpuReasonCode.Unsupported, failure.Message);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return CommandResults.Rejected(command, GpuReasonCode.Quiescing,
                "The command was cancelled before dispatch.", true);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return CommandResults.Indeterminate(command, GpuReasonCode.TransportFaulted, failure.Message);
        }

        GpuValue? readback = null;
        string? detail = null;
        if (control.Descriptor.SupportsRead)
        {
            try
            {
                session.BeginPass();
                readback = control.Read();
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                detail = failure.Message;
                if (failure is DriverFailure { Lost: true })
                {
                    Lose(detail);
                }
                else
                {
                    _sink!.LogChange(GpuLogLevel.Warn, id, "readback/" + control.Key, detail);
                }
            }
        }

        var verified = readback == requested;
        if (_session is not null)
        {
            _written[control.Key] = new WrittenValue(requested, readback);
            PublishValue(control, requested, detail,
                verified ? GpuStateQuality.Verified : GpuStateQuality.Observed, CancellationToken.None);
        }

        return verified ? CommandResults.Verified(command, readback!) : CommandResults.Unverified(command, detail);
    }

    internal GpuProfileSyncResult Sync(GpuProfileSync sync, CancellationToken token)
    {
        if (_session is null)
        {
            if (sync.Profiles.Count == 0)
            {
                return SyncUnavailable(sync, "The GPU driver session is not open.");
            }

            Open(token);
        }

        if (_session is not { } session)
        {
            return SyncUnavailable(sync, "The GPU driver session is not open.");
        }

        try
        {
            session.BeginPass();
            return session.Sync(sync, new WriteAdmission(token, _active), token);
        }
        catch (DriverFailure failure) when (failure.Lost)
        {
            Lose(failure.Message);
            return SyncUnavailable(sync, failure.Message);
        }
    }

    internal static GpuProfileSyncResult SyncUnavailable(GpuProfileSync sync, string detail)
    {
        var failures = sync.Profiles.SelectMany(profile => profile.Executables.SelectMany(executable =>
            profile.Values.Select(value => new GpuProfileFailure(profile.ProfileId, executable,
                value.CapabilityId, detail)))).ToArray();
        return new GpuProfileSyncResult(0, 0, failures.Length == 0
            ? [new GpuProfileFailure("driver", "", "", detail)]
            : failures);
    }

    private void Open(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            _session = open(_stateDirectory, _sink!);
            token.ThrowIfCancellationRequested();
            if (!_active())
            {
                Close();
                return;
            }

            _session.Initialize(new WriteAdmission(token, _active), token);
            token.ThrowIfCancellationRequested();
            _session.BeginPass();
            PublishModel(_session.Discover(), token);
            Observe(token);
            _sink!.ReportHealth(_model.Controls.Count == 0 ? GpuHealth.Unavailable : GpuHealth.Ready,
                _model.Controls.Count == 0
                    ? "The driver reported no supported GPU controls."
                    : "Driver controls available.");
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            Lose(failure.Message);
        }
    }

    private void PublishModel(DriverModel model, CancellationToken token)
    {
        _model = model;
        var descriptors = new GpuDescriptorSet
        {
            Sections = model.Sections,
            Descriptors = model.Controls.Select(control => control.Descriptor).ToArray()
        };
        if (SameModel(_descriptors, descriptors))
        {
            return;
        }

        _sink!.PublishDescriptorsAsync(descriptors, token).GetAwaiter().GetResult();
        _descriptors = descriptors;
        _published.Clear();
        var keys = model.Controls.Select(control => control.Key).ToHashSet();
        foreach (var key in _written.Keys.Where(key => !keys.Contains(key)).ToArray())
        {
            _written.Remove(key);
        }
    }

    private static bool SameModel(GpuDescriptorSet left, GpuDescriptorSet right)
    {
        return left.Sections.Count == right.Sections.Count && left.Descriptors.Count == right.Descriptors.Count
                                                           && left.Sections.Zip(right.Sections).All(pair =>
                                                               pair.First with { Categories = null! } ==
                                                               pair.Second with { Categories = null! }
                                                               && pair.First.Categories.SequenceEqual(pair.Second
                                                                   .Categories))
                                                           && left.Descriptors.Zip(right.Descriptors).All(pair =>
                                                               pair.First with { Choices = null! } == pair.Second with
                                                               {
                                                                   Choices = null!
                                                               }
                                                               && pair.First.Choices
                                                                   .SequenceEqual(pair.Second.Choices));
    }

    private void PublishValue(DriverControl control, GpuValue? value, string? error,
        GpuStateQuality quality, CancellationToken token)
    {
        var state = new GpuState
        {
            CapabilityId = control.Descriptor.CapabilityId,
            InstanceId = control.Descriptor.InstanceId,
            ObservedValue = value,
            Available = value is not null || control.Descriptor.SupportsWrite ||
                        control.Descriptor.Role == GpuRole.Action,
            Reason = error is null ? null : new GpuReason(GpuReasonCode.TransportFaulted, error),
            Quality = quality
        };
        if (_published.GetValueOrDefault(control.Key) == state)
        {
            return;
        }

        state = state with { ObservedAt = DateTimeOffset.UtcNow };
        _sink!.PublishCapabilityStateAsync(state, token).GetAwaiter().GetResult();
        _published[control.Key] = state with { ObservedAt = null };
    }

    private void Lose(string detail)
    {
        Close();
        PublishModel(new DriverModel([], []), CancellationToken.None);
        _sink!.ReportHealth(GpuHealth.Unavailable, detail);
        _sink.LogChange(GpuLogLevel.Warn, id, "health", detail);
    }

    internal void Close()
    {
        var session = _session;
        _session = null;
        try
        {
            session?.Dispose();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            _sink?.Log(GpuLogLevel.Warn, id, "Driver cleanup: " + failure.Message);
        }

        _model = new DriverModel([], []);
        _published.Clear();
        _written.Clear();
    }

    private sealed record WrittenValue(GpuValue Value, GpuValue? Readback);
}
