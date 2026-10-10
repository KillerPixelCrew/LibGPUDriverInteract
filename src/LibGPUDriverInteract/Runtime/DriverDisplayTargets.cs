// SPDX-License-Identifier: MIT

using System.ComponentModel;
using WindowsDeviceControl;

namespace LibGPUDriverInteract.Runtime;

/// <summary>Read-only CCD monitor identities, including connected outputs outside the desktop.</summary>
internal sealed class DriverDisplayTargets(
    IReadOnlyList<DisplayTargetIdentity> targets,
    IReadOnlyList<ActiveDisplayPath> active)
{
    internal IReadOnlyList<ActiveDisplayPath> Active => active;

    internal static DriverDisplayTargets Capture()
    {
        IReadOnlyList<DisplayTargetIdentity> targets;
        IReadOnlyList<ActiveDisplayPath> active;
        try
        {
            targets = DisplayLayouts.Observe().Targets.Where(target => target.Available)
                .Select(target => target.Target).ToArray();
        }
        catch (Win32Exception)
        {
            targets = [];
        }

        try
        {
            active = DisplayTopology.CaptureActive().Paths;
        }
        catch (Win32Exception)
        {
            active = [];
        }

        return new DriverDisplayTargets(targets, active);
    }

    internal DisplayTargetIdentity? ByRoute(uint low, int high, uint targetId)
    {
        return Unique(targets.Where(target => target.AdapterLowPart == low
                                             && target.AdapterHighPart == high && target.TargetId == targetId));
    }

    internal DisplayTargetIdentity? BySource(string? sourceName)
    {
        return string.IsNullOrEmpty(sourceName) ? null : Unique(active.Where(path =>
            string.Equals(path.SourceName, sourceName, StringComparison.OrdinalIgnoreCase)).Select(path => path.Target));
    }

    internal DisplayTargetIdentity? ByEdid(string? edidIdentity)
    {
        return string.IsNullOrEmpty(edidIdentity) ? null : Unique(targets.Where(target =>
            string.Equals(target.EdidIdentity, edidIdentity, StringComparison.Ordinal)));
    }

    internal GpuDisplayTarget Describe(DisplayTargetIdentity? target, string nativeOutputId, string? sourceName = null,
        string? edidIdentity = null)
    {
        return new GpuDisplayTarget
        {
            DevicePath = target?.DevicePath,
            EdidIdentity = target?.EdidIdentity ?? edidIdentity,
            SourceName = sourceName ?? active.FirstOrDefault(path => SameRoute(path.Target, target))?.SourceName,
            AdapterLowPart = target?.AdapterLowPart,
            AdapterHighPart = target?.AdapterHighPart,
            TargetId = target?.TargetId,
            NativeOutputId = nativeOutputId
        };
    }

    private static bool SameRoute(DisplayTargetIdentity first, DisplayTargetIdentity? second)
    {
        return second is not null && first.AdapterLowPart == second.AdapterLowPart
                                  && first.AdapterHighPart == second.AdapterHighPart && first.TargetId == second.TargetId;
    }

    private static DisplayTargetIdentity? Unique(IEnumerable<DisplayTargetIdentity> candidates)
    {
        var matches = candidates.Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
