using System.Globalization;
using LibGPUDriverInteract.Runtime;
using Microsoft.Win32;

namespace LibGPUDriverInteract;

/// <summary>The installed display driver for one present PCI adapter.</summary>
public sealed record GpuInstalledDriver(string InstanceId, string HardwareId, GpuVendor Vendor, string Name,
    string Version, DateTime? Date)
{
    /// <summary>Reads present display-class devices without opening a vendor driver session.</summary>
    public static IReadOnlyList<GpuInstalledDriver> ReadAll()
    {
        const string displayClass = WindowsGpuVendors.DisplayClass;
        List<GpuInstalledDriver> result = [];
        foreach (var instance in WindowsGpuVendors.CollectInstances())
        {
            var vendors = WindowsGpuVendors.Parse(instance);
            var vendor = vendors.Count == 1 ? vendors[0] : (GpuVendor?)null;
            if (vendor is null || !instance.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var device = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + instance);
            if (device?.GetValue("Driver") is not string driverKey || !driverKey.StartsWith(displayClass + "\\",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var driver = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + driverKey);
            result.Add(new GpuInstalledDriver(instance, instance.Split('\\')[1], vendor.Value,
                driver?.GetValue("DriverDesc") as string ?? instance,
                driver?.GetValue("DriverVersion") as string ?? "Unknown",
                ParseDate(driver?.GetValue("DriverDate") as string)));
        }

        return result;
    }

    internal static DateTime? ParseDate(string? value)
    {
        return DateTime.TryParseExact(value, ["M-d-yyyy", "MM-dd-yyyy", "M-dd-yyyy", "MM-d-yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }
}
