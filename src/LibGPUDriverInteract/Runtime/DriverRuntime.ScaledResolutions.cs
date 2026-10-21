// SPDX-License-Identifier: MIT

using LibGPUDriverInteract.Intel;
using WindowsDeviceControl;

namespace LibGPUDriverInteract.Runtime;

internal sealed partial class DriverRuntime
{
    private GpuHealth _availability = GpuHealth.Unavailable;

    private GpuHealth ReportAvailability()
    {
        var controls = _model.Controls.Count > 0;
        var sourceModes = !controls && HasScaledResolutionSupport();
        var health = controls || sourceModes ? GpuHealth.Ready : GpuHealth.Unavailable;
        _availability = health;
        _sink!.ReportHealth(health, controls ? "Driver controls available." : sourceModes
            ? "GPU source-mode catalog available; native graphics controls are unavailable."
            : "The driver reported no supported GPU controls or source-mode catalog.");
        return health;
    }

    private bool HasScaledResolutionSupport()
    {
        if (!_active() || id is not ("intel" or "amd" or "nvidia") || !OperatingSystem.IsWindows())
        {
            return false;
        }

        // Registry source lists do not depend on ADLX/NVAPI offering another generic capability.
        // Discover actual support before reporting Ready, rather than masking a missing native backend.
        try
        {
            foreach (var target in DisplayLayouts.Observe().Targets.Where(target => target.Available)
                         .Select(target => target.Target))
            {
                if (ReadScaledResolutions(new GpuDisplayTarget
                    {
                        DevicePath = target.DevicePath, EdidIdentity = target.EdidIdentity,
                        AdapterLowPart = target.AdapterLowPart, AdapterHighPart = target.AdapterHighPart,
                        TargetId = target.TargetId
                    }) is { Supported: true })
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
        return id == "intel"
            ? (_session as IntelSession)?.ReadScaledResolutions(display)
              ?? ScaledResolutionRegistry.Read(display, GpuVendor.Intel)
            : ScaledResolutionRegistry.Read(display, id == "amd" ? GpuVendor.Amd : GpuVendor.Nvidia);
    }

    internal GpuScaledResolutionResult SetScaledResolution(GpuScaledResolutionCatalog observed,
        GpuScaledResolution mode, bool remove, WriteAdmission admission)
    {
        if (!MatchesVendor(observed.Vendor))
        {
            return WrongVendor();
        }

        if (id == "intel")
        {
            return (_session as IntelSession)?.SetScaledResolution(observed, mode, remove, admission)
                   ?? new GpuScaledResolutionResult(false, false, GpuScaledResolutionActivation.Immediate,
                       "The Intel session is unavailable.");
        }

        return ScaledResolutionRegistry.Set(observed, mode, remove, admission);
    }

    internal GpuScaledResolutionResult RestartScaledResolutionDriver(GpuScaledResolutionCatalog observed,
        WriteAdmission admission)
    {
        return MatchesVendor(observed.Vendor) ? ScaledResolutionRegistry.Restart(observed, admission) : WrongVendor();
    }

    private bool MatchesVendor(GpuVendor vendor)
    {
        return id == vendor switch { GpuVendor.Intel => "intel", GpuVendor.Amd => "amd", _ => "nvidia" };
    }

    private static GpuScaledResolutionResult WrongVendor()
    {
        return new GpuScaledResolutionResult(false, false, GpuScaledResolutionActivation.Immediate,
            "The catalog does not belong to this serialized vendor owner.");
    }
}
