using LibGPUDriverInteract.Intel.Controls;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Runtime;
using WindowsDeviceControl;

namespace LibGPUDriverInteract.Intel.Display;

/// <summary>Who a display output is, in terms that survive a reboot and a reconnect.</summary>
/// <param name="InstanceId">The stable capability instance id.</param>
/// <param name="Name">The plain name for its section.</param>
/// <param name="Internal">Whether Windows reports the output as the built-in panel.</param>
/// <param name="Target">Physical monitor identity and the current Windows output route.</param>
internal sealed record DisplayIdentity(string InstanceId, string Name, bool Internal, GpuDisplayTarget Target);

/// <summary>
///     Resolves an IGCL output to a stable identity through Windows' own display configuration.
/// </summary>
/// <remarks>
///     IGCL hands over the adapter LUID and the Windows target id. Both are session keys: the LUID
///     changes every boot and the target id belongs to the connector. Windows maps the pair to the
///     monitor's EDID manufacturer and product codes and to its monitor device path
///     (<c>\\?\DISPLAY#BOE0B78#4&amp;...&amp;UID8388688#{...}</c>), whose instance part the PnP manager
///     keeps for the same monitor on the same connector across reboots and replugging.
///     <para>
///         So the instance id is <c>edid-</c> plus the three-letter manufacturer and four-digit product
///         code, then an FNV-1a fingerprint of the device path: <c>edid-boe0b78-1a2b3c4d</c>. The EDID
///         part keeps the id readable and tells two monitors apart even if Windows ever renumbered a
///         connector; the fingerprint keeps two identical monitors apart. Intel's own Arc Sync registry
///         key (<c>IntelArcSyncProfile_165_09e500780b0000000018</c>) is EDID-derived in the same spirit.
///         A monitor moved to another connector is a new instance, as it is for Windows itself.
///     </para>
///     <para>
///         The built-in panel's id starts with <c>internal-</c>. IGCL's encoder flag
///         (<c>CTL_ENCODER_CONFIG_FLAG_INTERNAL_DISPLAY</c>) decides, and Windows' output technology
///         (internal, embedded DisplayPort, LVDS or embedded UDI) answers only when IGCL does not.
///     </para>
/// </remarks>
internal static class DisplayIdentityResolver
{
    /// <summary>The prefix of the built-in panel's instance id.</summary>
    public const string InternalPrefix = "internal-";

    /// <summary><c>DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL</c>.</summary>
    private const uint OutputInternal = 0x80000000;

    /// <summary><c>DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED</c>.</summary>
    private const uint OutputDisplayPortEmbedded = 11;

    /// <summary><c>DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS</c>.</summary>
    private const uint OutputLvds = 6;

    /// <summary><c>DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED</c>.</summary>
    private const uint OutputUdiEmbedded = 13;

    /// <summary>Captures Windows monitor identities once for discovery.</summary>
    /// <param name="log">Receives the fallback decisions.</param>
    /// <returns>The captured topology, or an empty one when Windows cannot query it.</returns>
    public static DriverDisplayTargets Capture(IDriverSink log)
    {
        try
        {
            return DriverDisplayTargets.Capture();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            log.Warn("display", "Windows display identities are unavailable: " + error.Message);
            return new DriverDisplayTargets([], []);
        }
    }

    /// <summary>Resolves one output against the session's captured Windows topology.</summary>
    /// <param name="output">The IGCL output.</param>
    /// <param name="log">Receives fallback decisions.</param>
    /// <param name="topology">The capture for this discovery.</param>
    /// <returns>A stable identity, or a session-local fallback when Windows has no matching output.</returns>
    public static DisplayIdentity Resolve(IgclOutput output, IDriverSink log, DriverDisplayTargets topology)
    {
        var target = topology.ByRoute((uint)output.Adapter.Luid, (int)(output.Adapter.Luid >> 32), output.TargetId);
        var nativeId = $"{output.Adapter.Luid:x16}/{output.TargetId}";
        var metadata = topology.Describe(target, nativeId) with
        {
            AdapterLowPart = (uint)output.Adapter.Luid,
            AdapterHighPart = (int)(output.Adapter.Luid >> 32),
            TargetId = output.TargetId
        };
        if (target is null)
        {
            log.Warn(
                "display",
                $"Windows has no name for target {output.TargetId} on adapter {output.Adapter.Index} "
                + "; its settings are not remembered across reboots.");
            var fallbackInternal = output.Internal == true;
            return new DisplayIdentity(
                (fallbackInternal ? InternalPrefix : "") + $"adapter{output.Adapter.Index}-target{output.TargetId}",
                fallbackInternal ? "Built-in display" : $"Display {output.Index + 1}",
                fallbackInternal, metadata);
        }

        // IGCL's encoder flag decides; Windows' output technology only answers when IGCL does not.
        var isInternal = output.Internal
                         ?? topology.Active.FirstOrDefault(path => path.Target == target)?.OutputTechnology
                             is OutputInternal or OutputDisplayPortEmbedded or OutputLvds
                             or OutputUdiEmbedded;
        var manufacturer = target.EdidManufacturerId is { } code
            ? ValueMapping.DecodeManufacturer(code)
            : null;
        var instance = InstanceId(isInternal, manufacturer, target.EdidProductCodeId ?? 0,
            target.DevicePath.Length > 0 ? target.DevicePath : $"target-{output.TargetId}");
        var label = isInternal
            ? "Built-in display"
            : DriverDescriptors.Label(target.FriendlyName, $"Display {output.Index + 1}");
        return new DisplayIdentity(instance, label, isInternal, metadata);
    }

    /// <summary>Builds the instance id of a display.</summary>
    /// <param name="isInternal">Whether it is the built-in panel.</param>
    /// <param name="manufacturer">The decoded EDID manufacturer, or null.</param>
    /// <param name="product">The EDID product code.</param>
    /// <param name="path">The monitor device path the fingerprint is taken from.</param>
    /// <returns>
    ///     <c>edid-&lt;mfg&gt;&lt;product&gt;-&lt;fingerprint&gt;</c>, or <c>display-&lt;fingerprint&gt;</c>
    ///     without EDID codes, prefixed with <c>internal-</c> for the built-in panel. A consumer can give
    ///     Valve's single variable refresh row to an instance starting with <c>internal</c>.
    /// </returns>
    internal static string InstanceId(bool isInternal, string? manufacturer, ushort product, string path)
    {
        var fingerprint = ValueMapping.Fingerprint(path);
        var id = manufacturer is null ? $"display-{fingerprint}" : $"edid-{manufacturer}{product:x4}-{fingerprint}";
        return isInternal ? InternalPrefix + id : id;
    }
}
