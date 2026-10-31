// SPDX-License-Identifier: MIT

using LibGPUDriverInteract.Runtime;
using WindowsDeviceControl;

namespace LibGPUDriverInteract.Nvidia;

internal sealed record NvColorField(
    string Id,
    string Label,
    int Offset,
    bool Byte,
    IReadOnlyList<(uint Value, string Label)> Values)
{
    internal static readonly NvColorField[] All =
    [
        new("format", "Output color format", 8, true,
            [(0, "RGB"), (1, "YCbCr 4:2:2"), (2, "YCbCr 4:4:4"), (3, "YCbCr 4:2:0"), (254, "Default"), (255, "Auto")]),
        new("depth", "Output bit depth", 12, false,
            [(0, "Default"), (1, "6 bpc"), (2, "8 bpc"), (3, "10 bpc"), (4, "12 bpc"), (5, "16 bpc")]),
        new("range", "Output dynamic range", 10, true, [(0, "Full"), (1, "Limited"), (255, "Auto")]),
        new("colorimetry", "Colorimetry", 9, true,
        [
            (0, "RGB"), (1, "BT.601"), (2, "BT.709"), (3, "xvYCC 601"), (4, "xvYCC 709"),
            (5, "sYCC 601"), (6, "Adobe YCC 601"), (7, "Adobe RGB"), (8, "BT.2020 RGB"),
            (9, "BT.2020 YCC"), (10, "BT.2020 constant luminance"), (254, "Default"), (255, "Auto")
        ]),
        new("policy", "Output color selection", 16, false, [(0, "User settings"), (1, "Best quality")]),
        new("desktop-depth", "Desktop color depth", 20, false,
        [
            (0, "Keep current"), (1, "8 bpc"), (2, "10 bpc"), (3, "16-bit float"),
            (4, "16-bit float wide gamut"), (5, "16-bit float HDR")
        ])
    ];

    internal uint Read(byte[] data)
    {
        return Byte ? data[Offset] : NvApi.Number(data, Offset);
    }

    internal byte[] WithValue(byte[] current, uint value)
    {
        var data = (byte[])current.Clone();
        if (Byte)
        {
            data[Offset] = checked((byte)value);
        }
        else
        {
            NvApi.Number(data, Offset, value);
        }

        if (Offset != 16)
        {
            NvApi.Number(data, 16, 0); // An explicit field edit selects USER policy, as ColorControl does.
        }

        if (Offset == 8 && current[Offset] != value)
        {
            data[9] = 255; // The driver selects colorimetry compatible with the new RGB/YCC encoding.
        }

        return data;
    }
}

internal sealed class NvColorControl(
    NvApi api,
    NvOutput output,
    string instance,
    string section,
    NvColorField field,
    IReadOnlyList<(uint Value, string Label)> values)
    : DriverControl(DriverDescriptors.Choice("display.color." + field.Id, instance, field.Label, section,
        GpuProfileScope.GlobalOnly, values.Select(value => (NvSettingControl.Encode(value.Value), value.Label))))
{
    internal override GpuValue Read()
    {
        return GpuValue.Choice(NvSettingControl.Encode(field.Read(api.ReadColor(output.Id))));
    }

    internal override void Write(GpuValue value, WriteAdmission admission)
    {
        var data = field.WithValue(api.Color(output.Id, 1), NvSettingControl.Decode(value));
        api.SetColor(output.Id, data, admission);
    }
}

internal sealed class NvDitherControl(
    NvApi api,
    NvOutput output,
    string instance,
    string section,
    int field,
    IReadOnlyList<(uint Value, string Label)> values)
    : DriverControl(DriverDescriptors.Choice("display.dithering." + new[] { "state", "depth", "mode" }[field], instance,
        new[] { "Dithering", "Dithering bit depth", "Dithering mode" }[field], section,
        GpuProfileScope.GlobalOnly,
        values.Select(value => (NvSettingControl.Encode(value.Value), value.Label))))
{
    internal override GpuValue Read()
    {
        var data = api.Dither(output.Id);
        return GpuValue.Choice(
            NvSettingControl.Encode(field switch { 0 => data.State, 1 => data.Bits, _ => data.Mode }));
    }

    internal override void Write(GpuValue value, WriteAdmission admission)
    {
        var data = api.Dither(output.Id);
        var requested = NvSettingControl.Decode(value);
        api.SetDither(output, field switch
        {
            0 => data with { State = requested },
            1 => data with { Bits = requested },
            _ => data with { Mode = requested }
        }, admission);
    }
}

internal sealed class NvOutputModeControl(
    NvApi api,
    NvOutput expected,
    string instance,
    string section,
    IReadOnlyList<(uint Value, string Label)> values)
    : DriverControl(DriverDescriptors.Choice("display.hdr-output-mode", instance, "HDR output mode", section,
        GpuProfileScope.GlobalOnly, values.Select(value => (NvSettingControl.Encode(value.Value), value.Label))))
{
    internal override GpuValue Read()
    {
        return GpuValue.Choice(NvSettingControl.Encode(api.OutputMode(expected.Id)));
    }

    internal override void Write(GpuValue value, WriteAdmission admission)
    {
        var output = expected;
        var requested = NvSettingControl.Decode(value);
        var previous = api.OutputMode(output.Id);
        DisplayTargetIdentity? resetTarget = null;
        if (previous == 2 && requested == 1)
        {
            // ColorControl's HDR10+ -> HDR10 workaround. Capture the exact Windows HDR target first.
            resetTarget = output.Target ?? throw new DriverFailure("The HDR display's Windows identity is ambiguous.");
            if (!DisplayColor.TryReadHdr(resetTarget, out var enabled, out _))
            {
                throw new DriverFailure("The HDR state could not be captured for the HDR10+ transition.");
            }

            if (!enabled)
            {
                resetTarget = null;
            }
        }

        api.SetOutputMode(output.Id, requested, admission);
        if (resetTarget is not null)
        {
            var disabled = DisplayColor.SetHdr(resetTarget, false);
            // Restore the captured On state even if the Off write did not happen.
            var restored = DisplayColor.SetHdr(resetTarget, true);
            if (!disabled.Succeeded || !restored.Succeeded)
            {
                throw new DriverFailure("The HDR10 transition was not confirmed: " + Reason(disabled) + " "
                                        + Reason(restored), true);
            }
        }
    }

    /// <summary>Why one HDR write did not happen, in the words this failure has always used.</summary>
    private static string Reason(DisplaySetResult result)
    {
        return result.Outcome switch
        {
            DisplaySetOutcome.NotActive => "the display is not active, so its colour state was left alone",
            DisplaySetOutcome.Unreadable => "its colour state could not be read",
            DisplaySetOutcome.Unsupported => "this display does not support HDR",
            DisplaySetOutcome.Refused => $"Windows refused the HDR change (status {result.NativeStatus})",
            _ => ""
        };
    }
}

internal readonly record struct NvAdaptiveSync(uint MaxFrameInterval, uint Flags);

internal interface INvAdaptiveSync
{
    NvAdaptiveSync AdaptiveSync(uint display);
    void SetAdaptiveSync(uint display, NvAdaptiveSync current, bool enabled, WriteAdmission admission);
}

internal sealed class NvGsyncControl(INvAdaptiveSync api, uint display, string instance, string section)
    : DriverControl(DriverDescriptors.Toggle("display.gsync", instance, "G-SYNC enabled",
        new Placement(section, null, 0, GpuProfileScope.GlobalOnly)))
{
    internal override GpuValue Read()
    {
        return GpuValue.Boolean((api.AdaptiveSync(display).Flags & 1u) == 0);
    }

    internal override void Write(GpuValue value, WriteAdmission admission)
    {
        api.SetAdaptiveSync(display, api.AdaptiveSync(display), value.BooleanValue == true, admission);
    }
}
