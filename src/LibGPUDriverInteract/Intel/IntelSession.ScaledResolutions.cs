// SPDX-License-Identifier: MIT

using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Intel;

internal sealed partial class IntelSession
{
    private const int MaxCustomSourceModes = 20;

    internal unsafe GpuScaledResolutionCatalog? ReadScaledResolutions(GpuDisplayTarget display)
    {
        EnsureSession();
        var route = ScaledResolutionRegistry.Resolve(display, GpuVendor.Intel);
        if (route is null)
        {
            return null;
        }

        _session.RefreshOutputs();
        var output = FindOutput(display);
        if (output is null)
        {
            return null;
        }

        var args = new CtlCustomModeArgs();
        var result = _session.Call(_api.GetSetCustomMode, output.Handle, ref args);
        if (result != 0 || args.Count > MaxCustomSourceModes)
        {
            return Catalog(false, [], "Custom source modes unavailable: " + IgclResult.Describe(result));
        }

        var modes = new CtlCustomSourceMode[args.Count];
        if (modes.Length > 0)
        {
            fixed (CtlCustomSourceMode* pointer = modes)
            {
                args.Modes = pointer;
                result = _session.Call(_api.GetSetCustomMode, output.Handle, ref args);
            }

            if (result != 0 || args.Count > modes.Length)
            {
                return Catalog(false, [], "Custom source modes could not be read: " + IgclResult.Describe(result));
            }
        }

        return Catalog(true, modes.Take((int)args.Count)
            .Select(mode => new GpuScaledResolution((int)mode.SourceX, (int)mode.SourceY)).ToArray(),
            "Intel output-specific source modes; accepted changes are available immediately. "
            + $"Maximum {MaxCustomSourceModes} entries.");

        GpuScaledResolutionCatalog Catalog(bool supported, IReadOnlyList<GpuScaledResolution> sizes, string detail)
        {
            return new GpuScaledResolutionCatalog(display, GpuVendor.Intel, route.Identity, output.Adapter.Name,
                true, supported, sizes, detail);
        }
    }

    internal unsafe GpuScaledResolutionResult SetScaledResolution(GpuScaledResolutionCatalog observed,
        GpuScaledResolution mode, bool remove, WriteAdmission admission)
    {
        var fresh = ReadScaledResolutions(observed.Display);
        if (fresh is not { Supported: true } || fresh.AdapterIdentity != observed.AdapterIdentity)
        {
            return Result(false, false, "The display route changed or custom source modes are unavailable.");
        }

        if (fresh.Modes.Contains(mode) == !remove)
        {
            return Result(true, false, remove ? "The source mode is already absent." : "The source mode already exists.");
        }

        if (!remove && fresh.Modes.Count >= MaxCustomSourceModes)
        {
            return Result(false, false,
                $"The Intel driver accepts at most {MaxCustomSourceModes} custom source modes.");
        }

        var output = FindOutput(fresh.Display)!;
        var source = new CtlCustomSourceMode { SourceX = (uint)mode.Width, SourceY = (uint)mode.Height };
        var args = new CtlCustomModeArgs { Operation = remove ? 2 : 1, Count = 1, Modes = &source };
        admission.Check();
        var result = _session.Call(_api.GetSetCustomMode, output.Handle, ref args);
        return result == 0
            ? Result(true, true, "Source mode saved. Available immediately; the active mode was not changed.")
            : new GpuScaledResolutionResult(false, false, GpuScaledResolutionActivation.Uncertain,
                "Intel returned " + IgclResult.Describe(result) + ". Inspect the list before another explicit edit.");

        static GpuScaledResolutionResult Result(bool accepted, bool changed, string detail)
        {
            return new GpuScaledResolutionResult(accepted, changed, GpuScaledResolutionActivation.Immediate, detail);
        }
    }

    private IgclOutput? FindOutput(GpuDisplayTarget display)
    {
        return _session.Outputs.SingleOrDefault(item => (uint)item.Adapter.Luid == display.AdapterLowPart
            && (int)(item.Adapter.Luid >> 32) == display.AdapterHighPart && item.TargetId == display.TargetId);
    }
}
