// SPDX-License-Identifier: MIT

using System.Globalization;

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

/// <summary>A control whose read and write are plain delegates over the vendor session.</summary>
internal sealed class DelegateControl(
    GpuDescriptor descriptor,
    Func<GpuValue> read,
    Action<GpuValue, WriteAdmission> write)
    : DriverControl(descriptor)
{
    internal override GpuValue Read()
    {
        return read();
    }

    internal override void Write(GpuValue value, WriteAdmission admission)
    {
        write(value, admission);
    }
}

/// <summary>Discovery, observations and sticky written state shared by all three driver sessions.</summary>
/// <remarks>GpuDriver owns serialization and lifetime. This class has no separate gate, loop or retirement task.</remarks>
internal sealed partial class DriverRuntime(string id, Func<string, IDriverSink, IDriverSession> open)
{
    /// <summary>
    ///     Readback delays after a dispatched setter reports failure. A driver can answer a setter with a
    ///     failure code and still hold the value a moment later, so its own readback decides the outcome
    ///     within roughly a second and a half; the setter is never sent again.
    /// </summary>
    internal static readonly TimeSpan[] SettleDelays =
    [
        TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(800)
    ];

    private readonly Dictionary<string, GpuState> _published = [];
    private readonly Dictionary<string, WrittenValue> _written = [];
    private Func<bool> _active = () => false;
    private GpuDescriptorSet _descriptors = new();
    private DriverModel _model = new([], []);
    private IDriverSession? _session;
    private IDriverSink? _sink;
    private string _stateDirectory = "";

    /// <summary>Waits between settle readbacks; tests replace it to run without real delays.</summary>
    internal Action<TimeSpan, CancellationToken> Pause { get; set; } = static (delay, token) =>
    {
        token.WaitHandle.WaitOne(delay);
    };

    internal GpuHealth Start(IDriverSink sink, string stateDirectory, Func<bool> active, CancellationToken token)
    {
        _sink = sink;
        _stateDirectory = stateDirectory;
        _active = active;
        _published.Clear();
        _written.Clear();
        _descriptors = new GpuDescriptorSet();
        return Open(token);
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

            ReportAvailability();
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
            return CommandResults.Rejected(GpuReasonCode.DriverUnavailable, "The GPU driver is not open.");
        }

        var control = _model.Controls.FirstOrDefault(item => item.Descriptor.CapabilityId == command.CapabilityId
                                                             && item.Descriptor.InstanceId == command.InstanceId);
        if (control is null)
        {
            return CommandResults.Rejected(GpuReasonCode.Unsupported,
                $"{command.CapabilityId} is not published for {command.InstanceId}.");
        }

        if (!control.Accepts(command.RequestedValue))
        {
            return CommandResults.Rejected(GpuReasonCode.ValueOutOfRange,
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
                return failure.Attempted
                    ? CommandResults.Indeterminate(GpuReasonCode.TransportFaulted, failure.Message)
                    : CommandResults.Rejected(GpuReasonCode.DriverUnavailable, failure.Message);
            }

            return failure.Attempted
                ? Settle(control, requested, failure.Message, token)
                : CommandResults.Rejected(!admission.Admitted ? GpuReasonCode.Quiescing : GpuReasonCode.Unsupported,
                    failure.Message);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return CommandResults.Rejected(GpuReasonCode.Quiescing, "The command was cancelled before dispatch.",
                retryable: true);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return Settle(control, requested, failure.Message, token);
        }

        GpuValue? readback = null;
        string? detail = null;
        if (control.Descriptor.SupportsRead)
        {
            readback = ReadBack(session, control, out detail);
        }

        var verified = readback == requested;
        if (_session is not null)
        {
            _written[control.Key] = new WrittenValue(requested, readback);
            PublishValue(control, requested, detail,
                verified ? GpuStateQuality.Verified : GpuStateQuality.Observed, CancellationToken.None);
        }

        return verified ? CommandResults.Verified(readback!) : CommandResults.Unverified(detail);
    }

    /// <summary>Reads a control back once after a write; a lost session closes, any other failure is logged.</summary>
    private GpuValue? ReadBack(IDriverSession session, DriverControl control, out string? error)
    {
        try
        {
            session.BeginPass();
            error = null;
            return control.Read();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            error = failure.Message;
            if (failure is DriverFailure { Lost: true })
            {
                Lose(error);
            }
            else
            {
                _sink!.LogChange(GpuLogLevel.Warn, id, "readback/" + control.Key, error);
            }

            return null;
        }
    }

    /// <summary>Resolves a setter that reported failure after dispatch by asking the driver what it holds now.</summary>
    /// <remarks>
    ///     The setter is never repeated. A readback that matches the request is the driver accepting the
    ///     value despite its result code; one that still differs after the settle window is published as
    ///     the current state so the requested value is not shown as applied.
    /// </remarks>
    private GpuCommandResult Settle(DriverControl control, GpuValue requested, string message,
        CancellationToken token)
    {
        if (_session is not { } session || !control.Descriptor.SupportsRead)
        {
            return CommandResults.Indeterminate(GpuReasonCode.TransportFaulted, message);
        }

        GpuValue? readback;
        for (var attempt = 0;; attempt++)
        {
            readback = ReadBack(session, control, out var error);
            if (error is not null)
            {
                return CommandResults.Indeterminate(GpuReasonCode.TransportFaulted, message);
            }

            if (readback == requested)
            {
                _written[control.Key] = new WrittenValue(requested, readback);
                PublishValue(control, requested, null, GpuStateQuality.Verified, CancellationToken.None);
                _sink!.LogChange(GpuLogLevel.Info, id, "settled/" + control.Key,
                    $"{message} The driver reports the requested value on readback.");
                return CommandResults.Verified(readback!);
            }

            if (attempt >= SettleDelays.Length || token.IsCancellationRequested)
            {
                break;
            }

            Pause(SettleDelays[attempt], token);
        }

        _written.Remove(control.Key);
        PublishValue(control, readback, null, GpuStateQuality.Observed, CancellationToken.None);
        return CommandResults.Indeterminate(GpuReasonCode.TransportFaulted,
            $"{message} The driver still reports {Describe(readback!)}.");
    }

    private static string Describe(GpuValue value)
    {
        return value.Kind switch
        {
            GpuValueKind.Boolean => value.BooleanValue == true ? "on" : "off",
            GpuValueKind.Integer => value.IntegerValue?.ToString(CultureInfo.InvariantCulture) ?? "no value",
            GpuValueKind.Choice => value.ChoiceValue ?? "no value",
            _ => "no value"
        };
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

    private GpuHealth Open(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            _session = open(_stateDirectory, _sink!);
            token.ThrowIfCancellationRequested();
            if (!_active())
            {
                Close();
                return GpuHealth.Unavailable;
            }

            _session.Initialize(new WriteAdmission(token, _active), token);
            token.ThrowIfCancellationRequested();
            _session.BeginPass();
            PublishModel(_session.Discover(), token);
            Observe(token);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            Lose(failure.Message);
        }

        return ReportAvailability();
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

        token.ThrowIfCancellationRequested();
        _sink!.PublishDescriptors(descriptors);
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
        token.ThrowIfCancellationRequested();
        _sink!.PublishCapabilityState(state);
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
