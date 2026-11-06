using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LibGPUDriverInteract;

/// <summary>The installed display driver for one present PCI adapter.</summary>
public sealed record GpuInstalledDriver(string InstanceId, string HardwareId, GpuVendor Vendor, string Name,
    string Version, DateTime? Date)
{
    /// <summary>Reads present display-class devices without opening a vendor driver session.</summary>
    public static IReadOnlyList<GpuInstalledDriver> ReadAll()
    {
        const string displayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";
        var status = CM_Get_Device_ID_List_SizeW(out var length, displayClass, 0x300);
        if (status != 0)
        {
            throw new InvalidOperationException($"Display device enumeration failed: 0x{status:x8}.");
        }

        var buffer = new char[length];
        status = CM_Get_Device_ID_ListW(displayClass, buffer, length, 0x300);
        if (status != 0)
        {
            throw new InvalidOperationException($"Display device enumeration failed: 0x{status:x8}.");
        }

        List<GpuInstalledDriver> result = [];
        foreach (var instance in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var vendor = instance.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel
                : instance.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd
                : instance.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia
                : (GpuVendor?)null;
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
                DateTime.TryParse(driver?.GetValue("DriverDate") as string, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date) ? date : null));
        }

        return result;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint length, uint flags);
}
