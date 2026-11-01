using LibGPUDriverInteract.Intel.Controls;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Intel.Display;

/// <summary>One field of <c>ctl_intel_arc_sync_profile_params_t</c> that a Custom profile row publishes.</summary>
internal sealed class ArcSyncField
{
    public static readonly ArcSyncField MinimumHz = new(
        "display.arc-sync-min-refresh",
        "Custom minimum refresh (Hz)",
        true,
        static profile => profile.MinimumHz,
        static (ref profile, value) => profile.MinimumHz = value);

    public static readonly ArcSyncField MaximumHz = new(
        "display.arc-sync-max-refresh",
        "Custom maximum refresh (Hz)",
        true,
        static profile => profile.MaximumHz,
        static (ref profile, value) => profile.MaximumHz = value);

    public static readonly ArcSyncField FrameTimeIncrease = new(
        "display.arc-sync-frame-time-increase",
        "Custom max frame time increase (µs)",
        false,
        static profile => profile.MaxFrameTimeIncreaseUs,
        static (ref profile, value) => profile.MaxFrameTimeIncreaseUs = (uint)value);

    public static readonly ArcSyncField FrameTimeDecrease = new(
        "display.arc-sync-frame-time-decrease",
        "Custom max frame time decrease (µs)",
        false,
        static profile => profile.MaxFrameTimeDecreaseUs,
        static (ref profile, value) => profile.MaxFrameTimeDecreaseUs = (uint)value);

    private readonly Func<CtlArcSyncProfileParams, double> _get;
    private readonly Setter _set;

    private ArcSyncField(string id, string label, bool refresh, Func<CtlArcSyncProfileParams, double> get, Setter set)
    {
        Id = id;
        Label = label;
        IsRefresh = refresh;
        _get = get;
        _set = set;
    }

    /// <summary>Every field, in row order.</summary>
    public static IReadOnlyList<ArcSyncField> All { get; } =
        [MinimumHz, MaximumHz, FrameTimeIncrease, FrameTimeDecrease];

    /// <summary>The row's capability id.</summary>
    public string Id { get; }

    /// <summary>The row's label.</summary>
    public string Label { get; }

    /// <summary>Whether the field is a refresh rate in Hz rather than a frame time change in microseconds.</summary>
    public bool IsRefresh { get; }

    /// <summary>The field's raw value.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The value in the field's own unit.</returns>
    public double Get(CtlArcSyncProfileParams profile)
    {
        return _get(profile);
    }

    /// <summary>A copy of a profile with this field set.</summary>
    /// <param name="profile">The profile the other fields come from.</param>
    /// <param name="value">The value in the field's own unit.</param>
    /// <returns>The profile with that field set.</returns>
    public CtlArcSyncProfileParams With(CtlArcSyncProfileParams profile, int value)
    {
        var result = profile;
        _set(ref result, value);
        return result;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Id;
    }

    private delegate void Setter(ref CtlArcSyncProfileParams profile, int value);
}

/// <summary>
///     The bounds a Custom Arc Sync profile may use on one monitor, from
///     <c>ctlGetIntelArcSyncInfoForMonitor</c>.
/// </summary>
/// <param name="MinimumHz">The lowest whole refresh rate inside the monitor's range.</param>
/// <param name="MaximumHz">The highest whole refresh rate inside the monitor's range.</param>
/// <param name="FrameTimeMaximum">The largest frame time change offered, in microseconds.</param>
internal readonly record struct ArcSyncBounds(int MinimumHz, int MaximumHz, int FrameTimeMaximum)
{
    /// <summary>Derives the bounds from what the monitor reports.</summary>
    /// <param name="monitor">The monitor parameters.</param>
    /// <returns>The bounds, or null when the range holds fewer than two whole refresh rates.</returns>
    /// <remarks>
    ///     Intel's sample says the Custom refresh rates must lie in the panel's supported range. The frame
    ///     time changes are bounded by the whole frame time span of that range, since no step between two
    ///     frames can exceed it, and never below what the monitor itself reports.
    /// </remarks>
    public static ArcSyncBounds? From(CtlArcSyncMonitorParams monitor)
    {
        if (!float.IsFinite(monitor.MinimumHz) || !float.IsFinite(monitor.MaximumHz) || monitor.MinimumHz <= 0)
        {
            return null;
        }

        var minimum = (int)Math.Ceiling(monitor.MinimumHz - 0.001);
        var maximum = (int)Math.Floor(monitor.MaximumHz + 0.001);
        if (maximum <= minimum)
        {
            return null;
        }

        var span = (long)Math.Ceiling(1_000_000.0 / minimum - 1_000_000.0 / maximum - 0.001);
        var frameTime = Math.Max(span, Math.Max(monitor.MaxFrameTimeIncreaseUs, monitor.MaxFrameTimeDecreaseUs));
        return new ArcSyncBounds(minimum, maximum, (int)Math.Clamp(frameTime, 1, 1_000_000));
    }

    /// <summary>The range one field's row offers.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The inclusive range in the field's own unit.</returns>
    public IntegerRange RangeOf(ArcSyncField field)
    {
        return field.IsRefresh
            ? IntegerRange.Linear(MinimumHz, MaximumHz)
            : IntegerRange.Linear(0, FrameTimeMaximum);
    }

    /// <summary>One field of a profile, as the row publishes it: rounded and inside the row's range.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="field">The field.</param>
    /// <returns>The value.</returns>
    public int Read(CtlArcSyncProfileParams profile, ArcSyncField field)
    {
        return RangeOf(field).ToInteger(field.Get(profile));
    }

    /// <summary>Brings a profile's refresh rates inside the monitor's range, keeping them ordered.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The profile with usable refresh rates.</returns>
    public CtlArcSyncProfileParams Clamp(CtlArcSyncProfileParams profile)
    {
        var result = profile;
        result.MinimumHz = Read(profile, ArcSyncField.MinimumHz);
        result.MaximumHz = Read(profile, ArcSyncField.MaximumHz);
        if (result.MinimumHz >= result.MaximumHz)
        {
            result.MinimumHz = MinimumHz;
            result.MaximumHz = MaximumHz;
        }

        result.MaxFrameTimeIncreaseUs = (uint)Read(profile, ArcSyncField.FrameTimeIncrease);
        result.MaxFrameTimeDecreaseUs = (uint)Read(profile, ArcSyncField.FrameTimeDecrease);
        return result;
    }
}

/// <summary>
///     One display's Intel Arc Sync state: the profile, and the profile to bring back when variable
///     refresh is turned on again.
/// </summary>
/// <remarks>
///     Carried over from the Claw package, where it is hardware-verified on the Claw 8 AI+ A2VM on
///     2026-08-30: the panel reports support across 30-120 Hz, a write to OFF and a restore of the saved
///     parameter structure both succeed, and the readback confirms each. Enabling restores the last
///     profile other than OFF, so a user who chose EXCELLENT does not end up on RECOMMENDED after a
///     toggle; only when nothing was seen does it fall back to the driver's own RECOMMENDED. Unlike the
///     Claw's version nothing is rolled back: a write is graded by its readback and never undone.
///     <para>
///         The Custom profile (<c>CTL_INTEL_ARC_SYNC_PROFILE_CUSTOM</c>, <c>igcl_api.h</c> line 3933) carries
///         its own refresh range and frame time limits in the same structure. That path follows
///         <c>Samples/IntelArcSync</c> and is blind.
///     </para>
///     <para>
///         A fixed refresh rate is the same Custom profile with its minimum and maximum both set to the rate,
///         which makes the driver run the panel at exactly that rate, fractions included. The profile in use
///         before the first fixed write is kept in <see cref="FixedRefreshStore" />, on disk as well, so
///         <see cref="RestoreRate" />, the session's close and the next session's start can put it back. While
///         a rate is fixed, reads of the fixed profile are not remembered as the user's own profile, and any
///         write through the Arc Sync rows hands the profile back to the user.
///     </para>
/// </remarks>
internal sealed class ArcSyncDisplay
{
    public const int ProfileRecommended = 1;
    public const int ProfileExcellent = 2;
    public const int ProfileGood = 3;
    public const int ProfileCompatible = 4;
    public const int ProfileOff = 5;
    public const int ProfileVesa = 6;
    public const int ProfileCustom = 7;

    private readonly IDriverSink _log;
    private readonly string _name;
    private readonly IgclSource<CtlArcSyncProfileParams> _profile;
    private readonly FixedRefreshStore _store;
    private CtlArcSyncProfileParams? _custom;
    private CtlArcSyncProfileParams? _restore;

    private ArcSyncDisplay(IgclSource<CtlArcSyncProfileParams> profile, CtlArcSyncMonitorParams monitor,
        FixedRefreshStore store, string instance, string name, IDriverSink log)
    {
        _profile = profile;
        _store = store;
        Instance = instance;
        _name = name;
        _log = log;
        Monitor = monitor;
        Bounds = ArcSyncBounds.From(monitor);
    }

    /// <summary>What the monitor reports.</summary>
    public CtlArcSyncMonitorParams Monitor { get; }

    /// <summary>What a Custom profile may use, or null when the monitor's range does not allow one.</summary>
    public ArcSyncBounds? Bounds { get; }

    /// <summary>The display's stable instance id, which keys its fixed-rate record.</summary>
    public string Instance { get; }

    /// <summary>Whether the monitor reports a refresh range a fixed rate can be chosen from.</summary>
    public bool RateRangeAvailable => float.IsFinite(Monitor.MinimumHz) && float.IsFinite(Monitor.MaximumHz)
                                      && Monitor.MinimumHz > 0 && Monitor.MaximumHz >= Monitor.MinimumHz;

    /// <summary>The rate this library fixed and has not put back, or null.</summary>
    public float? FixedHz => _store.Get(Instance)?.FixedHz;

    /// <summary>Builds the state when the monitor supports Arc Sync.</summary>
    /// <param name="session">The session.</param>
    /// <param name="output">The output.</param>
    /// <param name="log">Receives the decision.</param>
    /// <param name="name">The display name for the trace.</param>
    /// <param name="store">The fixed-rate records; a private in-memory one when null.</param>
    /// <param name="instance">The display's stable instance id; the name when null.</param>
    /// <returns>The state, or null when variable refresh is not offered.</returns>
    public static unsafe ArcSyncDisplay? TryCreate(IgclSession session, IgclOutput output, IDriverSink log, string name,
        FixedRefreshStore? store = null, string? instance = null)
    {
        var api = session.Api;
        if (api.GetArcSyncInfo is null || api.GetArcSyncProfile is null || api.SetArcSyncProfile is null)
        {
            return null;
        }

        CtlArcSyncMonitorParams monitor = default;
        var result = session.Call(api.GetArcSyncInfo, output.Handle, ref monitor);
        if (result != IgclResult.Success || monitor.IsSupported == 0)
        {
            log.Info("arcsync", $"{name}: no variable refresh ({IgclResult.Describe(result)}).");
            return null;
        }

        ArcSyncDisplay display = new(
            new IgclSource<CtlArcSyncProfileParams>(session, output.Handle, api.GetArcSyncProfile,
                api.SetArcSyncProfile, default),
            monitor, store ?? FixedRefreshStore.Load(null, log), instance ?? name, name, log);
        _ = display.ReadProfile(out _);
        log.Info(
            "arcsync",
            $"{name}: variable refresh {monitor.MinimumHz:0}-{monitor.MaximumHz:0} Hz, frame time limits "
            + $"+{monitor.MaxFrameTimeIncreaseUs}/-{monitor.MaxFrameTimeDecreaseUs} us"
            + (display.Bounds is null ? "; the range is too narrow for a Custom profile." : "."));
        return display;
    }

    /// <summary>Reads the profile in use, once per pass, remembering what enabling and Custom start from.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The driver result.</returns>
    public int ReadProfile(out CtlArcSyncProfileParams profile)
    {
        var result = _profile.Read(out profile);
        if (result == IgclResult.Success)
        {
            Remember(profile);
        }

        return result;
    }

    /// <summary>Writes variable refresh on or off.</summary>
    /// <param name="enabled">The request.</param>
    /// <param name="admission">Rechecked after preparatory reads and before the setter.</param>
    /// <returns>The driver result.</returns>
    public int WriteEnabled(bool enabled, WriteAdmission admission)
    {
        // The read only captures the profile to bring back; the write happens either way.
        _ = ReadProfile(out _);
        var request = _restore ?? default;
        request.Profile = enabled ? _restore?.Profile ?? ProfileRecommended : ProfileOff;
        return SetForUser(request, admission);
    }

    /// <summary>Writes a named profile, or Custom with the last custom values seen.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="admission">Rechecked after preparatory reads and before the setter.</param>
    /// <returns>The driver result.</returns>
    public int WriteProfile(int profile, WriteAdmission admission)
    {
        var request = profile == ProfileCustom ? CustomBase() : MonitorProfile();
        request.Profile = profile;
        return SetForUser(request, admission);
    }

    /// <summary>Writes one value of the Custom profile, carrying the others.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value, inside the row's range.</param>
    /// <param name="admission">Rechecked after preparatory reads and before the setter.</param>
    /// <returns>How the driver answered.</returns>
    /// <remarks>
    ///     The other fields come from the current profile when it is Custom, else from the last Custom
    ///     values seen. The write sets the Custom profile either way; the read never decides whether to
    ///     write.
    /// </remarks>
    public ControlWrite WriteCustom(ArcSyncField field, int value, WriteAdmission admission)
    {
        if (Bounds is null)
        {
            return ControlWrite.Refuse("The monitor's range does not allow a Custom profile.");
        }

        var current = ReadProfile(out var profile) == IgclResult.Success && profile.Profile == ProfileCustom
            ? profile
            : CustomBase();
        var request = field.With(current, value);
        if (request.MinimumHz >= request.MaximumHz)
        {
            return ControlWrite.Refuse(
                $"The minimum refresh ({request.MinimumHz:0} Hz) must stay below the maximum ({request.MaximumHz:0} Hz).");
        }

        request.Profile = ProfileCustom;
        return ControlWrite.From(SetForUser(request, admission), $"the Custom Arc Sync {field}");
    }

    /// <summary>Runs the panel at one refresh rate: the Custom profile with its minimum and maximum both at it.</summary>
    /// <param name="hz">The rate; brought inside the monitor's reported range.</param>
    /// <param name="admission">Rechecked after preparatory reads and before the setter.</param>
    /// <param name="applied">The rate the request carried after clamping.</param>
    /// <returns>The driver result.</returns>
    /// <remarks>
    ///     The first fixed write captures the profile in use and records it before the setter runs, so a
    ///     process that never reaches <see cref="RestoreRate" /> still leaves the record the next session
    ///     restores from. Without a readable profile nothing is written, because nothing could be put back.
    ///     The frame time limits are carried from that captured profile.
    /// </remarks>
    public int WriteFixedRate(float hz, WriteAdmission admission, out float applied)
    {
        applied = Math.Clamp(hz, Monitor.MinimumHz, Monitor.MaximumHz);
        var record = _store.Get(Instance);
        var captured = record is null;
        if (record is null)
        {
            var read = _profile.Read(out var before);
            if (read != IgclResult.Success)
            {
                _log.Warn("arcsync", $"{_name}: refresh rate {hz:0.###} Hz not fixed; the profile in use could "
                                     + $"not be read to put back later ({IgclResult.Describe(read)}).");
                return read;
            }

            Remember(before);
            record = FixedRefreshRecord.Capture(before, applied);
        }

        record = record with { FixedHz = applied };
        _store.Set(Instance, record);
        var request = record.Before;
        request.Profile = ProfileCustom;
        request.MinimumHz = applied;
        request.MaximumHz = applied;
        var result = _profile.Write(request, admission);
        if (result != IgclResult.Success && captured)
        {
            _store.Remove(Instance);
        }

        _log.Info("arcsync",
            $"{_name}: refresh rate {applied:0.###} Hz through the Custom profile (min {request.MinimumHz:0.###}, "
            + $"max {request.MaximumHz:0.###} Hz; restores profile {record.Profile}): {IgclResult.Describe(result)}.");
        return result;
    }

    /// <summary>Puts back the profile in use before the first fixed write, when a rate is still fixed.</summary>
    /// <param name="admission">Rechecked before the setter.</param>
    /// <param name="wrote">Whether a setter was dispatched.</param>
    /// <returns>The driver result; success when nothing was fixed.</returns>
    /// <remarks>
    ///     A profile somebody else changed since is theirs: when the driver no longer runs the fixed rate the
    ///     record is dropped and nothing is written.
    /// </remarks>
    public int RestoreRate(WriteAdmission admission, out bool wrote)
    {
        wrote = false;
        return _store.Get(Instance) is { } record
            ? Restore(record, admission, out wrote, () => _store.Remove(Instance))
            : IgclResult.Success;
    }

    /// <summary>Supplies the profile a released fixed rate returns to, or null for the captured one.</summary>
    /// <param name="baseline">The profile.</param>
    public void SetBaseline(CtlArcSyncProfileParams? baseline)
    {
        _store.SetBaseline(Instance, baseline);
        _log.Info("arcsync", baseline is { } profile
            ? $"{_name}: a released fixed rate returns to profile {profile.Profile} "
              + $"(min {profile.MinimumHz:0.###}, max {profile.MaximumHz:0.###} Hz)."
            : $"{_name}: a released fixed rate returns to the profile in use before it.");
    }

    /// <summary>The native profile for a public baseline.</summary>
    /// <param name="baseline">The baseline.</param>
    /// <returns>The profile structure the setter takes.</returns>
    public static CtlArcSyncProfileParams ToNative(GpuArcSyncBaseline baseline)
    {
        CtlArcSyncProfileParams profile = default;
        profile.Profile = (int)baseline.Profile;
        profile.MinimumHz = (float)baseline.MinimumHz;
        profile.MaximumHz = (float)baseline.MaximumHz;
        profile.MaxFrameTimeIncreaseUs = baseline.MaxFrameTimeIncreaseUs;
        profile.MaxFrameTimeDecreaseUs = baseline.MaxFrameTimeDecreaseUs;
        return profile;
    }

    /// <summary>Restores a fixed rate a previous process left behind, once per session.</summary>
    /// <param name="admission">Rechecked before the setter.</param>
    public void RecoverStale(WriteAdmission admission)
    {
        if (_store.TakeRecovered(Instance) is not { } record)
        {
            return;
        }

        _log.Info("arcsync", $"{_name}: a previous session left the refresh rate fixed at {record.FixedHz:0.###} Hz.");
        _ = Restore(record, admission, out _, () => _store.ForgetRecovered(Instance));
    }

    private int Restore(FixedRefreshRecord record, WriteAdmission admission, out bool wrote, Action forget)
    {
        wrote = false;
        if (_profile.Read(out var current) == IgclResult.Success && !record.Matches(current))
        {
            _log.Info("arcsync",
                $"{_name}: the Arc Sync profile changed since the refresh rate was fixed; it is left as it is.");
            forget();
            return IgclResult.Success;
        }

        wrote = true;
        var baseline = _store.Baseline(Instance) ?? record.Before;
        var result = _profile.Write(baseline, admission);
        if (result == IgclResult.Success)
        {
            forget();
            Remember(baseline);
        }

        _log.Info("arcsync",
            $"{_name}: fixed refresh rate {record.FixedHz:0.###} Hz released, profile {baseline.Profile} "
            + $"(min {baseline.MinimumHz:0.###}, max {baseline.MaximumHz:0.###} Hz) restored: "
            + $"{IgclResult.Describe(result)}.");
        return result;
    }

    /// <summary>What a switch to Custom starts from: the last Custom values, else what the driver uses now.</summary>
    private CtlArcSyncProfileParams CustomBase()
    {
        CtlArcSyncProfileParams start;
        if (_custom is { } custom)
        {
            start = custom;
        }
        else if (ReadProfile(out var current) == IgclResult.Success && current.Profile != ProfileOff)
        {
            // The driver reports the range and limits it applies for the named profile in use.
            start = current;
        }
        else
        {
            start = MonitorProfile();
        }

        return Bounds is { } bounds ? bounds.Clamp(start) : start;
    }

    /// <summary>A profile carrying the monitor's own range and frame time limits.</summary>
    private CtlArcSyncProfileParams MonitorProfile()
    {
        CtlArcSyncProfileParams profile = default;
        profile.MinimumHz = Monitor.MinimumHz;
        profile.MaximumHz = Monitor.MaximumHz;
        profile.MaxFrameTimeIncreaseUs = Monitor.MaxFrameTimeIncreaseUs;
        profile.MaxFrameTimeDecreaseUs = Monitor.MaxFrameTimeDecreaseUs;
        return profile;
    }

    /// <summary>A write from an Arc Sync row: the user's own choice, which ends any fixed rate.</summary>
    private int SetForUser(CtlArcSyncProfileParams request, WriteAdmission admission)
    {
        var result = _profile.Write(request, admission);
        if (result == IgclResult.Success)
        {
            _store.Remove(Instance);
            Remember(request);
        }

        return result;
    }

    private void Remember(CtlArcSyncProfileParams profile)
    {
        // The fixed rate is this library's, not a profile the user chose.
        if (_store.Get(Instance) is { } record && record.Matches(profile))
        {
            return;
        }

        if (profile.Profile == ProfileCustom)
        {
            _custom = profile;
        }

        // A profile of OFF says nothing about what the user would want when enabling.
        if (profile.Profile is ProfileOff or <= 0 or > ProfileCustom)
        {
            return;
        }

        _restore = profile;
    }
}

/// <summary>Variable refresh on or off for one display.</summary>
internal sealed class VariableRefreshControl : IntelControl
{
    private readonly ArcSyncDisplay _display;

    public VariableRefreshControl(ArcSyncDisplay display, string instance, bool semantic, Placement placement)
        : base(DriverDescriptors.Toggle(
            "display.variable-refresh",
            instance,
            "Variable refresh rate",
            placement,
            semantic ? GpuRole.VariableRefreshRate : GpuRole.Toggle))
    {
        _display = display;
    }

    /// <inheritdoc />
    public override ControlRead Read()
    {
        var result = _display.ReadProfile(out var profile);
        return result == IgclResult.Success
            ? ControlRead.Of(GpuValue.Boolean(profile.Profile != ArcSyncDisplay.ProfileOff))
            : ControlRead.Driver(result);
    }

    /// <inheritdoc />
    protected override ControlWrite WriteValidated(GpuValue value, WriteAdmission admission)
    {
        return ControlWrite.From(_display.WriteEnabled(value.BooleanValue == true, admission), "variable refresh");
    }
}

/// <summary>The Arc Sync profile of one display, offered while variable refresh is on.</summary>
internal sealed class ArcSyncProfileControl : IntelControl
{
    /// <summary>Why the profile and Custom rows cannot be used while variable refresh is off.</summary>
    internal static readonly GpuReason VariableRefreshOff =
        new(GpuReasonCode.PrerequisiteMissing, "Variable refresh is off.");

    private static readonly EnumMember[] Named =
    [
        new(ArcSyncDisplay.ProfileRecommended, "recommended", "Recommended"),
        new(ArcSyncDisplay.ProfileExcellent, "excellent", "Excellent"),
        new(ArcSyncDisplay.ProfileGood, "good", "Good"),
        new(ArcSyncDisplay.ProfileCompatible, "compatible", "Compatible"),
        new(ArcSyncDisplay.ProfileVesa, "vesa", "VESA")
    ];

    private static readonly EnumMember[] WithCustom = [.. Named, new(ArcSyncDisplay.ProfileCustom, "custom", "Custom")];

    private readonly ArcSyncDisplay _display;

    public ArcSyncProfileControl(ArcSyncDisplay display, string instance, Placement placement)
        : base(DriverDescriptors.Choice("display.arc-sync-profile", instance, "Arc Sync profile",
                display.Bounds is null ? Named : WithCustom, placement),
            display.Bounds is null ? Named : WithCustom)
    {
        _display = display;
    }

    /// <inheritdoc />
    public override ControlRead Read()
    {
        var result = _display.ReadProfile(out var profile);
        if (result != IgclResult.Success)
        {
            return ControlRead.Driver(result);
        }

        return profile.Profile == ArcSyncDisplay.ProfileOff
            ? ControlRead.Unavailable(VariableRefreshOff)
            : ControlRead.Of(profile.Profile < 0 ? null : MemberOf((uint)profile.Profile));
    }

    /// <inheritdoc />
    protected override ControlWrite WriteValidated(GpuValue value, WriteAdmission admission)
    {
        return ControlWrite.From(_display.WriteProfile((int)ValueOf(value), admission),
            $"Arc Sync profile {value.ChoiceValue}");
    }
}

/// <summary>One value of a display's Custom Arc Sync profile, available while that profile is in use.</summary>
internal sealed class ArcSyncParameterControl : IntelControl
{
    private static readonly GpuReason NotCustom =
        new(GpuReasonCode.PrerequisiteMissing, "Only the Custom Arc Sync profile takes its own values.");

    private readonly ArcSyncDisplay _display;
    private readonly ArcSyncField _field;

    private ArcSyncParameterControl(ArcSyncDisplay display, ArcSyncField field, GpuDescriptor descriptor)
        : base(descriptor)
    {
        _display = display;
        _field = field;
    }

    /// <summary>Builds the four Custom profile rows, when the monitor's range allows a Custom profile.</summary>
    /// <param name="display">The display's Arc Sync state.</param>
    /// <param name="instance">The display's instance id.</param>
    /// <param name="placement">Where the first row sits; the others follow it.</param>
    /// <returns>The rows, possibly none.</returns>
    public static IReadOnlyList<ArcSyncParameterControl> Build(ArcSyncDisplay display, string instance,
        Placement placement)
    {
        if (display.Bounds is not { } bounds)
        {
            return [];
        }

        List<ArcSyncParameterControl> rows = [];
        foreach (var field in ArcSyncField.All)
        {
            rows.Add(new ArcSyncParameterControl(display, field,
                DriverDescriptors.Range(field.Id, instance, field.Label, bounds.RangeOf(field), GpuUnit.None,
                    placement.Plus(rows.Count))));
        }

        return rows;
    }

    /// <inheritdoc />
    public override ControlRead Read()
    {
        var result = _display.ReadProfile(out var profile);
        if (result != IgclResult.Success)
        {
            return ControlRead.Driver(result);
        }

        // The value is what the driver applies now, shown even while the row cannot be changed.
        var value = GpuValue.Integer(Range!.Value.ToInteger(_field.Get(profile)));
        return profile.Profile switch
        {
            ArcSyncDisplay.ProfileCustom => ControlRead.Of(value),
            ArcSyncDisplay.ProfileOff => ControlRead.Unavailable(ArcSyncProfileControl.VariableRefreshOff, value),
            _ => ControlRead.Unavailable(NotCustom, value)
        };
    }

    /// <inheritdoc />
    protected override ControlWrite WriteValidated(GpuValue value, WriteAdmission admission)
    {
        return _display.WriteCustom(_field, value.IntegerValue!.Value, admission);
    }
}
