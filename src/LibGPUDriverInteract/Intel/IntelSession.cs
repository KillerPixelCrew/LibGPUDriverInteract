// SPDX-License-Identifier: MIT

using LibGPUDriverInteract.Intel.Controls;
using LibGPUDriverInteract.Intel.Display;
using LibGPUDriverInteract.Intel.Graphics;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Intel.Profiles;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Intel;

/// <summary>Intel's native session, using the shared driver owner's lifetime and observations.</summary>
internal sealed partial class IntelSession : IDriverSession
{
    private readonly IReadOnlyList<AdapterClassEntry> _adapterKeys;
    private readonly IgclApi _api;
    private readonly ColorStore _colors;
    private readonly IDriverSink _log;
    private readonly IntelGraphicsMemoryTransport _memory;
    private readonly ApplicationProfileSynchronizer _profiles;
    private readonly IgclSession _session;
    private IntelModel? _model;

    internal IntelSession(string stateDirectory, IDriverSink sink)
        : this(stateDirectory, sink, IgclApi.TryLoad(sink)
                                     ?? throw new DriverFailure("The Intel graphics control library is not installed."),
            WindowsRegistryNode.LocalMachine)
    {
    }

    /// <summary>Owns supplied API/session handles; supplied registry and memory transport remain borrowed.</summary>
    internal IntelSession(string stateDirectory, IDriverSink sink, IgclApi api, IRegistryNode registry,
        IntelGraphicsMemoryTransport? memory = null)
    {
        _log = sink;
        _api = api;
        try
        {
            _session = IgclSession.TryOpen(_api, _log)
                       ?? throw new DriverFailure("No Intel graphics adapter answered.");
            _adapterKeys = AdapterClassKey.Enumerate(registry, AdapterClassKey.ClassPath, _log);
            _memory = memory ?? new IntelGraphicsMemoryTransport(_log, _adapterKeys);
            _colors = ColorStore.Load(stateDirectory, _log);
            _profiles = new ApplicationProfileSynchronizer(registry, stateDirectory, _log);
            _log.Info("lifecycle", $"IGCL 0x{_session.SupportedVersion:x} open: {_session.Adapters.Count} adapter(s), "
                                   + $"{_session.Outputs.Count} connected display(s).");
        }
        catch
        {
            _session?.Dispose();
            _api.Dispose();
            throw;
        }
    }

    public void BeginPass()
    {
        EnsureSession();
        _session.BeginPass();
    }

    public DriverModel Discover()
    {
        EnsureSession();
        _session.RefreshOutputs();
        _model = IntelModel.Build(_session, _memory, _colors, _adapterKeys, _log);
        return new DriverModel(_model.Sections,
            _model.Controls.Select(control => (DriverControl)new Control(control, _session)).ToArray());
    }

    public GpuProfileSyncResult Sync(GpuProfileSync sync, WriteAdmission admission, CancellationToken token)
    {
        EnsureSession();
        if (_model is null)
        {
            return DriverRuntime.SyncUnavailable(sync, "The Intel driver controls are not discovered.");
        }

        return _profiles.Apply(sync, _model.FindTarget, token, admission);
    }

    public void Dispose()
    {
        try
        {
            _session.Dispose();
        }
        finally
        {
            _api.Dispose();
        }
    }

    private void EnsureSession()
    {
        if (_session.Lost)
        {
            throw new DriverFailure("The Intel graphics driver session was lost.", lost: true);
        }
    }

    private sealed class Control(IntelControl control, IgclSession session) : DriverControl(control.Descriptor)
    {
        internal override bool Accepts(GpuValue? value)
        {
            return control.Validate(value, out _);
        }

        internal override GpuValue Read()
        {
            var result = control.Read();
            if (session.Lost)
            {
                throw new DriverFailure(result.Failure ?? "The Intel graphics driver session was lost.",
                    lost: true);
            }

            return result.Value ?? throw new DriverFailure(result.Failure
                                                           ?? result.Reason?.Detail
                                                           ?? "The Intel control has no readable value.");
        }

        internal override void Write(GpuValue value, WriteAdmission admission)
        {
            var result = control.Write(value, admission);
            if (session.Lost || result.Status != WriteStatus.Applied)
            {
                throw new DriverFailure(result.Detail ?? "The Intel setter failed.",
                    result.Status == WriteStatus.Uncertain, session.Lost);
            }
        }
    }
}
