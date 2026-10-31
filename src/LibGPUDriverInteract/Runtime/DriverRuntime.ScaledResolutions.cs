// SPDX-License-Identifier: MIT

using LibGPUDriverInteract.Intel;

namespace LibGPUDriverInteract.Runtime;

internal sealed partial class DriverRuntime
{
    private GpuVendor? Vendor => id switch
    {
        "intel" => GpuVendor.Intel,
        "amd" => GpuVendor.Amd,
        "nvidia" => GpuVendor.Nvidia,
        _ => null
    };

    private GpuHealth ReportAvailability()
    {
        var controls = _model.Controls.Count > 0;
        var sourceModes = !controls && HasScaledResolutionSupport();
        var health = controls || sourceModes ? GpuHealth.Ready : GpuHealth.Unavailable;
        _sink!.ReportHealth(health, controls ? "Driver controls available." : sourceModes
            ? "GPU source-mode catalog available; native graphics controls are unavailable."
            : "The driver reported no supported GPU controls or source-mode catalog.");
        return health;
    }

    private bool HasScaledResolutionSupport()
    {
        if (!_active() || Vendor is null)
        {
            return false;
        }

        // Registry source lists do not depend on ADLX/NVAPI offering another generic capability.
        // Discover actual support before reporting Ready, rather than masking a missing native backend.
        try
        {
            var topology = DriverDisplayTargets.Capture();
            foreach (var target in topology.Targets)
            {
                if (ReadScaledResolutions(topology.Describe(target, nativeOutputId: null)) is { Supported: true })
                {
                    return true;
                }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _sink?.Log(GpuLogLevel.Warn, id, "Source-mode discovery: " + error.Message);
        }

        return false;
    }

    internal GpuScaledResolutionCatalog? ReadScaledResolutions(GpuDisplayTarget display)
    {
        return Vendor switch
        {
            GpuVendor.Intel => (_session as IntelSession)?.ReadScaledResolutions(display)
                               ?? ScaledResolutionRegistry.Read(display, GpuVendor.Intel),
            { } vendor => ScaledResolutionRegistry.Read(display, vendor),
            null => null
        };
    }

    internal GpuScaledResolutionResult SetScaledResolution(GpuScaledResolutionCatalog observed,
        GpuScaledResolution mode, bool remove, CancellationToken token)
    {
        if (observed.Vendor != Vendor)
        {
            return WrongVendor();
        }

        var admission = new WriteAdmission(token, _active);
        if (Vendor == GpuVendor.Intel)
        {
            return (_session as IntelSession)?.SetScaledResolution(observed, mode, remove, admission)
                   ?? new GpuScaledResolutionResult(false, false, GpuScaledResolutionActivation.Immediate,
                       "The Intel session is unavailable.");
        }

        return ScaledResolutionRegistry.Set(observed, mode, remove, admission);
    }

    internal GpuScaledResolutionResult RestartScaledResolutionDriver(GpuScaledResolutionCatalog observed,
        CancellationToken token)
    {
        return observed.Vendor == Vendor
            ? ScaledResolutionRegistry.Restart(observed, new WriteAdmission(token, _active))
            : WrongVendor();
    }

    private static GpuScaledResolutionResult WrongVendor()
    {
        return new GpuScaledResolutionResult(false, false, GpuScaledResolutionActivation.Immediate,
            "The catalog does not belong to this serialized vendor owner.");
    }
}
