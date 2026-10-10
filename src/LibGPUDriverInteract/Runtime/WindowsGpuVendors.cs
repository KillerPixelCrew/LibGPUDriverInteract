// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace LibGPUDriverInteract.Runtime;

/// <summary>Present PCI display vendors, independent of driver installation metadata and its registry permissions.</summary>
internal static partial class WindowsGpuVendors
{
    internal const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    internal const uint PresentDisplayFlags = 0x00000200 | 0x00000100;
    internal const uint BufferSmall = 0x1a;

    internal delegate uint SizeLookup(out uint length);
    internal delegate uint ListRead(char[] buffer, uint length);

    internal static IReadOnlyList<GpuVendor> Collect()
    {
        return Collect(
            (out uint length) => CM_Get_Device_ID_List_SizeW(out length, DisplayClass, PresentDisplayFlags),
            (buffer, length) => CM_Get_Device_ID_ListW(DisplayClass, buffer, length, PresentDisplayFlags));
    }

    internal static IReadOnlyList<GpuVendor> Collect(SizeLookup size, ListRead read)
    {
        uint result = BufferSmall;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            result = size(out var length);
            if (result != 0)
            {
                break;
            }

            if (length == 0)
            {
                return [];
            }

            var buffer = new char[length];
            result = read(buffer, length);
            if (result == 0)
            {
                return Parse(new string(buffer));
            }

            // Hotplug may grow the list between the size and read calls. Only enumeration is repeated.
            if (result != BufferSmall)
            {
                break;
            }
        }

        throw new InvalidOperationException($"Windows display adapter enumeration failed (CONFIGRET 0x{result:x8}).");
    }

    internal static IReadOnlyList<GpuVendor> Parse(string multiString)
    {
        HashSet<GpuVendor> present = [];
        foreach (var instance in multiString.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!instance.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var end = instance.IndexOf('\\', 4);
            var hardwareId = end < 0 ? instance.AsSpan(4) : instance.AsSpan(4, end - 4);
            foreach (var field in hardwareId.ToString().Split('&'))
            {
                var vendor = field.ToUpperInvariant() switch
                {
                    "VEN_8086" => GpuVendor.Intel,
                    "VEN_1002" => GpuVendor.Amd,
                    "VEN_10DE" => GpuVendor.Nvidia,
                    _ => (GpuVendor?)null
                };
                if (vendor is { } value)
                {
                    present.Add(value);
                }
            }
        }

        return new[] { GpuVendor.Intel, GpuVendor.Amd, GpuVendor.Nvidia }.Where(present.Contains).ToArray();
    }

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint length, uint flags);
}
