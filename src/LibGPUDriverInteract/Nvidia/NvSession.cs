// SPDX-License-Identifier: MIT

using System.Globalization;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Nvidia;

internal sealed class NvSession : IDriverSession
{
    private readonly NvApi _api;
    private readonly NvProfiles _profiles;
    private readonly Action<string, string> _report;

    /// <summary>The driver's value tables, which do not change within one session.</summary>
    private readonly Dictionary<uint, uint[]> _values = [];

    private HashSet<uint>? _settingIds;
    private Dictionary<string, NvSettingControl> _settings = [];

    internal NvSession(string stateDirectory, Action<string, string> report)
    {
        _report = report;
        _api = new NvApi();
        try
        {
            _profiles = new NvProfiles(_api, stateDirectory, report);
        }
        catch
        {
            _api.Dispose();
            throw;
        }
    }

    /// <summary>Cleans legacy profiles only while the explicit owner still admits native writes.</summary>
    public void Initialize(WriteAdmission admission, CancellationToken token)
    {
        try
        {
            _profiles.Initialize(admission, token);
        }
        catch (Exception failure) when (admission.Admitted &&
                                        failure is not OutOfMemoryException and not OperationCanceledException
                                            and not DriverFailure { Lost: true })
        {
            _report("legacy-profiles", "Legacy profile cleanup did not complete: " + failure.Message);
            _api.Load(); // Discard uncommitted DRS mutations before publishing otherwise usable controls.
        }
    }

    /// <summary>Loads the DRS database once; the pass's reads and the command's write use that state.</summary>
    public void BeginPass()
    {
        _api.BeginPass();
    }

    /// <inheritdoc />
    public DriverModel Discover()
    {
        var outputs = _api.Displays(); // NVIDIA present does not imply that it drives the laptop panel.
        var ids = _settingIds ??= _api.SettingIds();
        var sections = new List<GpuSection>();
        var controls = new List<DriverControl>();
        var settings = new Dictionary<string, NvSettingControl>();
        foreach (var group in NvSettings.All.GroupBy(setting => setting.Group))
        {
            var section = "nvidia." + group.Key;
            var before = controls.Count;
            foreach (var setting in group.Where(setting => ids.Contains(setting.Id)))
            {
                Try("driver." + setting.Id.ToString("x8"), () =>
                {
                    if (!_values.TryGetValue(setting.Id, out var values))
                    {
                        values = _api.Values(setting.Id);
                        _values[setting.Id] = values;
                    }

                    if (values.Length == 0 && setting.Documented)
                    {
                        values = setting.Values.Select(value => value.Value).Distinct().ToArray();
                    }

                    if (values.Length == 0)
                    {
                        return;
                    }

                    try
                    {
                        var current = _api.Get(_api.GlobalProfile(), setting.Id);
                        values = values.Append(current.Value).Distinct().ToArray();
                    }
                    catch (DriverFailure failure) when (!failure.Lost)
                    {
                        _report("driver." + setting.Id.ToString("x8"), failure.Message);
                    }

                    var control = new NvSettingControl(_api, setting, section, values);
                    // Checks the DWORD type. Display-output controls are discovered separately.
                    controls.Add(control);
                    settings.Add(control.Descriptor.CapabilityId, control);
                });
            }

            if (controls.Count > before)
            {
                var title = group.Key switch
                {
                    "performance" => "Performance",
                    "quality" => "Quality",
                    "display" => "Display",
                    "dlss" => "DLSS",
                    "vr" => "VR",
                    _ => group.Key
                };
                sections.Add(DriverDescriptors.Section(section, "NVIDIA " + title, group.Key == "display"));
            }
        }

        foreach (var output in outputs)
        {
            var instance = output.Target is { DevicePath.Length: > 0 } target
                ? "output-" + DriverIds.Hash(target.DevicePath.ToUpperInvariant())
                : "output-" + output.Id.ToString("x8");
            var section = "nvidia." + instance;
            var before = controls.Count;
            AddOutput(controls, output, instance, section);
            if (controls.Count > before)
            {
                sections.Add(DriverDescriptors.Section(section,
                    "NVIDIA " + (output.Target?.FriendlyName ?? "display " + output.Id.ToString("X8")), true) with
                {
                    DisplayTarget = output.Route
                });
            }
        }

        _settings = settings;
        return new DriverModel(sections, controls);
    }

    /// <inheritdoc />
    public GpuProfileSyncResult Sync(GpuProfileSync sync, WriteAdmission admission,
        CancellationToken token)
    {
        return _profiles.Sync(sync, _settings, admission, token);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _api.Dispose();
    }

    private void AddOutput(List<DriverControl> controls, NvOutput output, string instance, string section)
    {
        Try(instance + "/vrr", () =>
        {
            var flags = _api.Vrr(output.Id);
            controls.Add(new DelegateControl(
                DriverDescriptors.ReadOnly("display.gsync-status", instance, "G-SYNC state", section,
                    [("unsupported", "Unavailable"), ("off", "Off"), ("enabled", "Enabled"), ("active", "Active")]),
                () => GpuValue.Choice(_api.Vrr(output.Id) switch
                {
                    var current when (current & 2) == 0 => "unsupported",
                    var current when (current & 16) != 0 => "active",
                    var current when (current & 1) != 0 => "enabled",
                    _ => "off"
                }),
                static (_, _) => throw new DriverFailure("This NVIDIA state is read only.")));
            if ((flags & 2u) != 0)
            {
                controls.Add(new NvGsyncControl(_api, output.Id, instance, section));
            }
        });
        Try(instance + "/color", () =>
        {
            var current = _api.ReadColor(output.Id);
            foreach (var field in NvColorField.All)
            {
                var choices = field.Values.Where(value => _api.SupportsColor(output.Id,
                    field.WithValue(current, value.Value))).ToArray();
                if (choices.Length > 0)
                {
                    controls.Add(new NvColorControl(_api, output, instance, section, field, choices));
                }
            }
        });
        Try(instance + "/dither", () =>
        {
            if (!_api.SupportsDither)
            {
                return;
            }

            var current = _api.Dither(output.Id);
            if (current.State > 2)
            {
                return;
            }

            controls.Add(new NvDitherControl(_api, output, instance, section, 0,
                [(0, "Auto"), (1, "Enabled"), (2, "Disabled")]));
            var bits = new (uint, string)[] { (0, "6-bit"), (1, "8-bit"), (2, "10-bit") }
                .Where(value => (current.BitsCaps & (1u << (int)value.Item1)) != 0).ToArray();
            var modes = new (uint, string)[]
            {
                (0, "Spatial dynamic"), (1, "Spatial static"), (2, "Spatial dynamic 2x2"),
                (3, "Spatial static 2x2"), (4, "Temporal")
            }.Where(value => (current.ModeCaps & (1u << (int)value.Item1)) != 0).ToArray();
            if (bits.Length > 0 && current.State == 1)
            {
                controls.Add(new NvDitherControl(_api, output, instance, section, 1, bits));
            }

            if (modes.Length > 0 && current.State == 1)
            {
                controls.Add(new NvDitherControl(_api, output, instance, section, 2, modes));
            }
        });
        Try(instance + "/hdr-output", () =>
        {
            if (!_api.SupportsOutputMode)
            {
                return;
            }

            var flags = _api.HdrCapabilities(output.Id);
            if ((flags & 1) == 0)
            {
                return;
            }

            _api.OutputMode(output.Id);
            var values = new List<(uint, string)> { (0, "SDR"), (1, "HDR10") };
            if ((flags & 0x80) != 0) // isHdr10PlusGamingSupported, not merely HDR10+ video support
            {
                values.Add((2, "HDR10+ Gaming"));
            }

            controls.Add(new NvOutputModeControl(_api, output, instance, section, values));
        });
    }

    private void Try(string key, Action action)
    {
        DriverLog.Try(_report, key, action);
    }
}

/// <summary>One DWORD setting of the global DRS profile, used by driver-wide choice controls.</summary>
/// <remarks>Reads and writes use the state the pass loaded (<see cref="NvSession.BeginPass" />).</remarks>
internal sealed class NvSettingControl : DriverControl
{
    private readonly INvProfiles _api;

    internal NvSettingControl(INvProfiles api, NvSettingDefinition setting, string section, IEnumerable<uint> values)
        : base(DriverDescriptors.Choice("driver." + setting.Id.ToString("x8"), "driver", setting.Label, section,
            setting.Native ? GpuProfileScope.NativePerApplication : GpuProfileScope.GlobalOnly,
            Labelled(setting, values)))
    {
        _api = api;
        Setting = setting;
    }

    internal NvSettingDefinition Setting { get; }

    internal override GpuValue Read()
    {
        return GpuValue.Choice(Encode(_api.Get(_api.GlobalProfile(), Setting.Id).Value));
    }

    internal override void Write(GpuValue value, WriteAdmission admission)
    {
        var requested = Decode(value);
        _api.Set(_api.GlobalProfile(), Setting.Id, requested, admission);
        // The runtime's BeginPass reloads DRS before any readback, so no reload is needed here.
        _api.Save(admission);
    }

    /// <summary>The label of a value: its table entry, its size for a sized setting, or "Custom".</summary>
    /// <param name="setting">The setting.</param>
    /// <param name="value">A value the driver offers or holds.</param>
    /// <returns>A label that never shows the raw driver value.</returns>
    /// <remarks>
    ///     The driver can hold a value no table names, such as a shader cache size another tool set. It is
    ///     still offered, so the current value stays selectable, under a name a user can read.
    /// </remarks>
    internal static string LabelOf(NvSettingDefinition setting, uint value)
    {
        if (setting.Values.FirstOrDefault(option => option.Value == value).Label is { } label)
        {
            return label;
        }

        double? megabytes = setting.Unit switch
        {
            NvValueUnit.Megabytes => value,
            NvValueUnit.Bytes => value / 1048576.0,
            _ => null
        };
        return megabytes switch
        {
            null => "Custom",
            >= 1024 => $"{(megabytes.Value / 1024).ToString("0.#", CultureInfo.InvariantCulture)} GB",
            _ => $"{megabytes.Value.ToString("0.#", CultureInfo.InvariantCulture)} MB"
        };
    }

    /// <summary>Every offered value with its label; repeated labels are numbered so each stays distinct.</summary>
    /// <param name="setting">The setting.</param>
    /// <param name="values">The offered values.</param>
    /// <returns>The encoded values and their labels, in offer order.</returns>
    internal static IEnumerable<(string Value, string Label)> Labelled(NvSettingDefinition setting,
        IEnumerable<uint> values)
    {
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var label = LabelOf(setting, value);
            var count = seen[label] = seen.GetValueOrDefault(label) + 1;
            yield return (Encode(value), count == 1 ? label : $"{label} {count}");
        }
    }

    internal static string Encode(uint value)
    {
        return "v" + value.ToString("x8");
    }

    internal static uint Decode(GpuValue value)
    {
        return uint.Parse(value.ChoiceValue!.AsSpan(1), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
    }
}
