// SPDX-License-Identifier: MIT
// ColorControl d0d3bb4: private NVIDIA dithering and HDR output-mode ABI.
// These entry points are optional. They never trigger a registry write or driver restart.

using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Nvidia;

internal sealed unsafe partial class NvApi
{
    private const uint GetOutputModeId = 0x81fed88d;
    private const uint SetOutputModeId = 0x98e7661a;
    private const uint GetDitherId = 0x932ac8fb;
    private const uint SetDitherId = 0xdf0dfcdd;

    internal bool SupportsDither => HasFunction(GetDitherId) && HasFunction(SetDitherId);

    internal bool SupportsOutputMode => HasFunction(GetOutputModeId) && HasFunction(SetOutputModeId);

    internal uint OutputMode(uint display)
    {
        uint mode = 0;
        Check(((delegate* unmanaged[Cdecl]<uint, uint*, int>)Function(GetOutputModeId))(display, &mode),
            "Disp_GetOutputMode");
        return mode;
    }

    internal void SetOutputMode(uint display, uint mode, WriteAdmission admission)
    {
        var function = (delegate* unmanaged[Cdecl]<uint, uint*, int>)Function(SetOutputModeId);
        admission.Check();
        Check(function(display, &mode), "Disp_SetOutputMode", true);
    }

    internal NvDither Dither(uint display)
    {
        var data = new byte[24];
        Number(data, 0, Version(data.Length, 1));
        fixed (byte* pointer = data)
        {
            Check(((delegate* unmanaged[Cdecl]<uint, byte*, int>)Function(GetDitherId))(display, pointer),
                "Disp_GetDitherControl");
        }

        return new NvDither(Number(data, 4), Number(data, 8), Number(data, 12), Number(data, 16), Number(data, 20));
    }

    internal void SetDither(NvOutput expected, NvDither data, WriteAdmission admission)
    {
        var function = (delegate* unmanaged[Cdecl]<nint, uint, uint, uint, uint, int>)Function(SetDitherId);
        admission.Check();
        Check(function(expected.Gpu, expected.Id, data.State, data.State == 0 ? 0 : data.Bits,
            data.State == 0 ? 0 : data.Mode), "Disp_SetDitherControl", true);
    }
}

internal sealed record NvDither(uint State, uint Bits, uint Mode, uint BitsCaps, uint ModeCaps);
