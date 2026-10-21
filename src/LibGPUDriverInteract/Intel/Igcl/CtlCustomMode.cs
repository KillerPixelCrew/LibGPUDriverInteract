// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace LibGPUDriverInteract.Intel.Igcl;

// IGCL ctl_get_set_custom_mode_args_t, version 0. Natural native alignment is intentional.
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CtlCustomModeArgs
{
    internal uint Size;
    internal byte Version;
    internal int Operation;
    internal uint Count;
    internal CtlCustomSourceMode* Modes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CtlCustomSourceMode
{
    internal uint SourceX;
    internal uint SourceY;
}
