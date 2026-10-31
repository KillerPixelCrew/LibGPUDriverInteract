// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LibGPUDriverInteract.Runtime;

/// <summary>Adapter-list source modes. Registry edits preserve unknown entries, list partitions and value kinds.</summary>
internal static class ScaledResolutionRegistry
{
    private const string AmdModes = "DALNonStandardModesBCD";
    private const string AmdRestricted = "DALRestrictedModesBCD";
    private const string NvidiaModes = "NV_Modes";
    private static readonly Regex NvidiaSize = new(@"(?<![\w])(?<w>\d{3,5})x(?<h>\d{3,5})(?![\d])",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal sealed record Adapter(string Identity, string Name, string Key);

    internal static Adapter? Resolve(GpuDisplayTarget display, GpuVendor vendor)
    {
        if (display.AdapterLowPart is not { } low || display.AdapterHighPart is not { } high
            || display.TargetId is not { } targetId)
        {
            return null;
        }

        var topology = DriverDisplayTargets.Capture();
        var target = topology.ByRoute(low, high, targetId);
        if (target is null || string.IsNullOrEmpty(display.DevicePath)
            || !string.Equals(target.DevicePath, display.DevicePath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var packet = new AdapterName
        {
            Type = 4, Size = Marshal.SizeOf<AdapterName>(), Low = low, High = high
        };
        if (DisplayConfigGetDeviceInfo(ref packet) != 0)
        {
            return null;
        }

        var identity = InterfaceInstance(packet.Path);
        var pciVendor = vendor switch { GpuVendor.Intel => "8086", GpuVendor.Amd => "1002", _ => "10DE" };
        if (identity is null || !identity.Contains("VEN_" + pciVendor, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var instance = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + identity);
        if (instance?.GetValue("Driver") is not string driver || !driver.StartsWith('{')
            || driver.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var keyPath = @"SYSTEM\CurrentControlSet\Control\Class\" + driver;
        using var driverKey = Registry.LocalMachine.OpenSubKey(keyPath);
        return new Adapter(identity, driverKey?.GetValue("DriverDesc") as string ?? vendor.ToString(), keyPath);
    }

    internal static GpuScaledResolutionCatalog? Read(GpuDisplayTarget display, GpuVendor vendor)
    {
        var adapter = Resolve(display, vendor);
        if (adapter is null)
        {
            return null;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(adapter.Key);
            if (key is null)
            {
                return Catalog(false, [], "The driving adapter registry key is unavailable.");
            }

            IReadOnlyList<GpuScaledResolution> modes;
            if (vendor == GpuVendor.Intel)
            {
                return Catalog(false, [], "The Intel graphics control library is unavailable for this adapter.");
            }

            if (vendor == GpuVendor.Amd)
            {
                var added = ReadAmd(key, AmdModes);
                var restricted = ReadAmd(key, AmdRestricted);
                modes = added.Except(restricted).Distinct().ToArray();
            }
            else
            {
                modes = (ReadNvidiaModes(key) ?? []).SelectMany(ParseNvidia).Distinct().ToArray();
            }

            return Catalog(true, modes, "Adapter-wide source sizes. Changes require an explicit graphics-driver restart.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return Catalog(false, [], "The adapter list could not be read: " + error.Message);
        }

        GpuScaledResolutionCatalog Catalog(bool supported, IReadOnlyList<GpuScaledResolution> modes, string detail)
        {
            return new GpuScaledResolutionCatalog(display, vendor, adapter.Identity, adapter.Name, false, supported,
                modes, detail);
        }
    }

    internal static GpuScaledResolutionResult Set(GpuScaledResolutionCatalog observed, GpuScaledResolution mode,
        bool remove, WriteAdmission admission)
    {
        var attempted = false;
        try
        {
            var adapter = Resolve(observed.Display, observed.Vendor);
            if (adapter is null || adapter.Identity != observed.AdapterIdentity)
            {
                return Result(false, false, "The display's driving adapter changed. Refresh the editor.");
            }

            using var key = Registry.LocalMachine.OpenSubKey(adapter.Key, writable: true);
            if (key is null)
            {
                return Result(false, false, "The adapter list is not writable. Administrator permission is required.");
            }

            var edits = observed.Vendor == GpuVendor.Amd
                ? EditAmd(key, mode, remove)
                : EditNvidia(key, mode, remove);
            foreach (var edit in edits)
            {
                // No await between identity, fresh-list read and writes; this is the GPU owner's serialized lane.
                admission.Check();
                attempted = true;
                key.SetValue(edit.Name, edit.Value, edit.Kind);
            }

            return Result(true, edits.Count > 0, edits.Count == 0 ? "The source list already has the requested state."
                : "Source list saved. Restart the graphics driver explicitly to make the changes available.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new GpuScaledResolutionResult(false, false,
                attempted ? GpuScaledResolutionActivation.Uncertain : GpuScaledResolutionActivation.DriverRestart,
                (attempted ? "The write outcome is uncertain; refresh before another explicit action. " : "No write was made. ")
                + error.Message);
        }
    }

    internal static GpuScaledResolutionResult Restart(GpuScaledResolutionCatalog observed, WriteAdmission admission)
    {
        var attempted = false;
        try
        {
            var adapter = Resolve(observed.Display, observed.Vendor);
            if (adapter is null || adapter.Identity != observed.AdapterIdentity)
            {
                return Result(false, false, "The driving adapter changed. Refresh before restarting it.");
            }

            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("/restart-device");
            start.ArgumentList.Add(adapter.Identity);
            admission.Check();
            attempted = true;
            using var process = Process.Start(start) ?? throw new IOException("PnPUtil did not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                return new GpuScaledResolutionResult(false, false, GpuScaledResolutionActivation.Uncertain,
                    "The restart is still running. Do not repeat it automatically; refresh after it completes.");
            }

            Task.WhenAll(output, errors).GetAwaiter().GetResult();
            return process.ExitCode == 0
                ? new GpuScaledResolutionResult(true, true, GpuScaledResolutionActivation.Immediate,
                    "Windows accepted the graphics-driver restart. Refresh the display list.")
                : Result(false, false, "Windows could not complete the restart (exit " + process.ExitCode
                    + "). Administrator permission or a Windows restart may be required. " + output.Result + errors.Result);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new GpuScaledResolutionResult(false, false,
                attempted ? GpuScaledResolutionActivation.Uncertain : GpuScaledResolutionActivation.DriverRestart,
                "Driver restart failed: " + error.Message);
        }
    }

    private sealed record Edit(string Name, object Value, RegistryValueKind Kind);

    private static List<Edit> EditAmd(RegistryKey key, GpuScaledResolution mode, bool remove)
    {
        if (mode.Width > 9999 || mode.Height > 9999)
        {
            throw new ArgumentException("AMD's BCD source list supports at most 9999 pixels per dimension.");
        }

        // Restricted defaults are unrelated driver policy, not WSGM additions. Preserve their bytes.
        if (!remove && ReadAmd(key, AmdRestricted).Contains(mode))
        {
            throw new InvalidOperationException("This size is excluded by DALRestrictedModesBCD. Its driver policy was preserved.");
        }

        List<Edit> edits = [];
        var lists = AmdNames(key, AmdModes).Select(name => (Name: name, Bytes: ReadBinary(key, name))).ToArray();
        if (remove)
        {
            foreach (var list in lists)
            {
                var changed = RemoveAmdBytes(list.Bytes, mode);
                if (!list.Bytes.AsSpan().SequenceEqual(changed))
                {
                    edits.Add(new Edit(list.Name, changed, RegistryValueKind.Binary));
                }
            }
        }
        else if (!lists.Any(list => DecodeAmdList(list.Bytes).Contains(mode)))
        {
            // Append one record without repartitioning or replacing any existing list.
            var existing = lists.FirstOrDefault(list => list.Bytes.Length <= 56 && list.Bytes.Length % 8 == 0);
            var destination = existing.Name;
            if (destination is null)
            {
                var index = 1;
                while (lists.Any(list => list.Name.Equals(AmdModes + index, StringComparison.OrdinalIgnoreCase)))
                {
                    index++;
                }

                destination = AmdModes + index;
            }

            edits.Add(new Edit(destination, AddAmdBytes(existing.Bytes ?? [], mode), RegistryValueKind.Binary));
        }

        return edits;
    }

    internal static byte[] RemoveAmdBytes(byte[] bytes, GpuScaledResolution mode)
    {
        List<byte> kept = [];
        for (var offset = 0; offset < bytes.Length; offset += 8)
        {
            var count = Math.Min(8, bytes.Length - offset);
            // Only remove plain source-size entries, never custom refresh/flags or malformed data.
            if (count == 8 && DecodeAmd(bytes.AsSpan(offset, 8)) == mode
                && bytes.AsSpan(offset + 4, 4).SequenceEqual(new byte[4]))
            {
                continue;
            }

            kept.AddRange(bytes.AsSpan(offset, count).ToArray());
        }

        return kept.ToArray();
    }

    internal static byte[] AddAmdBytes(byte[] bytes, GpuScaledResolution mode)
    {
        if (!mode.Valid || mode.Width > 9999 || mode.Height > 9999)
        {
            throw new ArgumentException("The source size does not fit AMD's four-digit BCD list.");
        }

        if (bytes.Length % 8 != 0)
        {
            throw new InvalidDataException("The AMD list has a partial record; it was preserved.");
        }

        var result = new byte[bytes.Length + 8];
        bytes.CopyTo(result, 0);
        var dimensions = mode.Width.ToString("D4", CultureInfo.InvariantCulture)
                         + mode.Height.ToString("D4", CultureInfo.InvariantCulture);
        for (var index = 0; index < 4; index++)
        {
            result[bytes.Length + index] = (byte)(((dimensions[index * 2] - '0') << 4)
                | (dimensions[index * 2 + 1] - '0'));
        }

        return result;
    }

    private static GpuScaledResolution? DecodeAmd(ReadOnlySpan<byte> record)
    {
        if (record.Length < 8 || record[..4].ToArray().Any(value => (value >> 4) > 9 || (value & 15) > 9))
        {
            return null;
        }

        return new GpuScaledResolution(Number(record[0], record[1]), Number(record[2], record[3]));

        static int Number(byte first, byte second)
        {
            return (first >> 4) * 1000 + (first & 15) * 100 + (second >> 4) * 10 + (second & 15);
        }
    }

    private static IReadOnlyList<GpuScaledResolution> ReadAmd(RegistryKey key, string prefix)
    {
        return AmdNames(key, prefix).SelectMany(name => DecodeAmdList(ReadBinary(key, name))).ToList();
    }

    private static List<GpuScaledResolution> DecodeAmdList(byte[] bytes)
    {
        List<GpuScaledResolution> modes = [];
        for (var offset = 0; offset + 8 <= bytes.Length; offset += 8)
        {
            if (DecodeAmd(bytes.AsSpan(offset, 8)) is { Valid: true } mode)
            {
                modes.Add(mode);
            }
        }

        return modes;
    }

    private static string[] AmdNames(RegistryKey key, string prefix)
    {
        return key.GetValueNames().Where(name => name.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var suffix)
            && suffix > 0).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static byte[] ReadBinary(RegistryKey key, string name)
    {
        return key.GetValue(name) as byte[] ?? throw new InvalidDataException(name + " is not a binary source-mode list.");
    }

    private static string[]? ReadNvidiaModes(RegistryKey key)
    {
        var value = key.GetValue(NvidiaModes, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        ValidateNvidiaStorage(value, value is null ? null : key.GetValueKind(NvidiaModes));
        return (string[]?)value;
    }

    internal static IReadOnlyList<GpuScaledResolution> ParseNvidia(string text)
    {
        return NvidiaSize.Matches(text).Select(match => new GpuScaledResolution(
            int.Parse(match.Groups["w"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture))).Where(mode => mode.Valid).ToArray();
    }

    private static List<Edit> EditNvidia(RegistryKey key, GpuScaledResolution mode, bool remove)
    {
        var stored = ReadNvidiaModes(key);
        var texts = stored ?? [];
        if (texts.SelectMany(ParseNvidia).Contains(mode) == !remove)
        {
            return [];
        }

        if (remove)
        {
            texts = texts.Select(text => EditNvidiaText(text, mode)).Where(text => text.Length > 0).ToArray();
        }
        else
        {
            // A new independent all-output source-size group leaves every existing group's qualifiers intact.
            // Seed the driver's documented base list when NV_Modes is absent, preserving those existing defaults.
            if (stored is null)
            {
                texts = ["{*}SHV 1600x1200 1280x1024 1024x768 800x600 640x480=1"];
            }

            texts = [.. texts, $"{{*}}SHV {mode.Width}x{mode.Height}=1"];
        }

        return [new Edit(NvidiaModes, texts, RegistryValueKind.MultiString)];
    }

    internal static string EditNvidiaText(string text, GpuScaledResolution mode)
    {
        // Only the dimension-only source-list grammar is editable. Unknown driver extensions remain byte-for-byte.
        return Regex.Replace(text, @"(?<prefix>\{[^}]*\}[SHV]+)(?<sizes>(?:\s+\d{3,5}x\d{3,5})+)(?<flags>=\d+)(?=\s|$)", group =>
        {
            var sizes = group.Groups["sizes"].Value;
            var changed = Regex.Replace(sizes, @"\s+\d{3,5}x\d{3,5}", token =>
            {
                return ParseNvidia(token.Value).Contains(mode) ? "" : token.Value;
            }, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return changed.Length == 0 ? "" : group.Groups["prefix"].Value + changed + group.Groups["flags"].Value;
        }, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    internal static void ValidateNvidiaStorage(object? value, RegistryValueKind? kind)
    {
        if (value is not null && (kind != RegistryValueKind.MultiString || value is not string[]))
        {
            throw new InvalidDataException("NV_Modes is not REG_MULTI_SZ; the unsupported storage format was preserved.");
        }
    }

    private static GpuScaledResolutionResult Result(bool accepted, bool changed, string detail)
    {
        return new GpuScaledResolutionResult(accepted, changed, GpuScaledResolutionActivation.DriverRestart, detail);
    }

    private static string? InterfaceInstance(string path)
    {
        var set = SetupDiCreateDeviceInfoList(0, 0);
        if (set == -1)
        {
            return null;
        }

        try
        {
            var entry = new DeviceInterface { Size = Marshal.SizeOf<DeviceInterface>() };
            if (!SetupDiOpenDeviceInterface(set, path, 0, ref entry))
            {
                return null;
            }

            var device = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() };
            SetupDiGetDeviceInterfaceDetail(set, ref entry, 0, 0, out var required, ref device);
            if (required is < 8 or > 65536)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal((int)required);
            try
            {
                Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(set, ref entry, buffer, required, out _, ref device))
                {
                    return null;
                }

                var identity = new StringBuilder(512);
                return SetupDiGetDeviceInstanceId(set, ref device, identity, identity.Capacity, out _)
                    ? identity.ToString() : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterName
    {
        internal int Type;
        internal int Size;
        internal uint Low;
        internal int High;
        internal uint Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterface
    {
        internal int Size;
        internal Guid Class;
        internal uint Flags;
        internal nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo
    {
        internal int Size;
        internal Guid Class;
        internal uint Instance;
        internal nint Reserved;
    }

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdapterName packet);

    [DllImport("setupapi.dll")]
    private static extern nint SetupDiCreateDeviceInfoList(nint classGuid, nint parent);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiOpenDeviceInterfaceW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiOpenDeviceInterface(nint set, string path, uint flags, ref DeviceInterface entry);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref DeviceInterface entry, nint detail,
        uint size, out uint required, ref DeviceInfo device);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(nint set, ref DeviceInfo device, StringBuilder identity,
        int size, out uint required);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint set);
}
