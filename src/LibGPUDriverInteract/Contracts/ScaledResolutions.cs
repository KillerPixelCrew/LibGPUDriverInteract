// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract;

/// <summary>A GPU source size, with no refresh rate or signal timing.</summary>
public sealed record GpuScaledResolution(int Width, int Height)
{
    /// <summary>Whether both dimensions fit the shared source-mode editor limits.</summary>
    public bool Valid => Width is >= 100 and <= 15999 && Height is >= 100 and <= 15999;
}

/// <summary>The current driving adapter and its custom source-size catalog.</summary>
public sealed record GpuScaledResolutionCatalog(
    GpuDisplayTarget Display,
    GpuVendor Vendor,
    string AdapterIdentity,
    string AdapterName,
    bool OutputSpecific,
    bool Supported,
    IReadOnlyList<GpuScaledResolution> Modes,
    string Detail);

/// <summary>Activation required after an accepted source-mode edit.</summary>
public enum GpuScaledResolutionActivation
{
    /// <summary>No driver restart is needed.</summary>
    Immediate,
    /// <summary>The stored adapter list needs an explicit driver restart.</summary>
    DriverRestart,
    /// <summary>The write may have completed; inspect before any new explicit operation.</summary>
    Uncertain
}

/// <summary>An edit result; a potentially completed write is never automatically retried.</summary>
public sealed record GpuScaledResolutionResult(
    bool Accepted,
    bool Changed,
    GpuScaledResolutionActivation Activation,
    string Detail);
