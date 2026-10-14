using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using LibGPUDriverInteract.Runtime;
using WindowsDeviceControl;

namespace LibGPUDriverInteract.Nvidia;

/// <summary>Documented NVAPI DRS/color layouts; private output extensions live in NvApiPrivate.cs.</summary>
internal sealed unsafe partial class NvApi : INvProfiles, INvAdaptiveSync, IDisposable
{
    internal const int SettingSize = 12320;
    internal const int SettingIdOffset = 4100;
    internal const int CurrentValueOffset = 8220;
    internal const int ValuesSize = 414112;
    internal const int ValueStride = 4100;
    internal const int ApplicationSize = 20492;
    internal const int ProfileSize = 4116;
    internal const int ColorSize = 24;
    private readonly Dictionary<uint, DriverFailure> _colorFailures = [];
    private readonly Dictionary<uint, byte[]> _colors = [];
    private readonly Dictionary<uint, nint> _functions = [];
    private readonly delegate* unmanaged[Cdecl]<uint, nint> _query;
    private readonly byte[] _settingBuffer = new byte[SettingSize];
    private readonly byte[] _valuesBuffer = new byte[ValuesSize];
    private bool _initialized;
    private nint _library;

    internal NvApi()
    {
        _library = SystemLibrary.Load("nvapi64.dll");
        try
        {
            _query = (delegate* unmanaged[Cdecl]<uint, nint>)NativeLibrary.GetExport(_library, "nvapi_QueryInterface");
            Check(((delegate* unmanaged[Cdecl]<int>)Function(0x0150e828))(), "Initialize");
            _initialized = true;
            nint session = 0;
            Check(((delegate* unmanaged[Cdecl]<nint*, int>)Function(0x0694d52e))(&session), "DRS CreateSession");
            Drs = session;
            Load();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal nint Drs { get; private set; }

    public void Dispose()
    {
        if (_library == 0)
        {
            return;
        }

        try
        {
            if (Drs != 0)
            {
                ((delegate* unmanaged[Cdecl]<nint, int>)Function(0xdad9cff8))(Drs);
                Drs = 0;
            }

            if (_initialized)
            {
                ((delegate* unmanaged[Cdecl]<int>)Function(0xd22bdd7e))();
                _initialized = false;
            }
        }
        finally
        {
            NativeLibrary.Free(_library);
            _library = 0;
        }
    }

    public void Load()
    {
        Check(((delegate* unmanaged[Cdecl]<nint, int>)Function(0x375dbd6b))(Drs), "DRS LoadSettings");
    }

    public void Save(WriteAdmission admission)
    {
        var save = (delegate* unmanaged[Cdecl]<nint, int>)Function(0xfcbc7e14);
        admission.Check();
        Check(save(Drs), "DRS SaveSettings", true);
    }

    public nint GlobalProfile()
    {
        nint profile = 0;
        Check(((delegate* unmanaged[Cdecl]<nint, nint*, int>)Function(0x617bff9f))(Drs, &profile),
            "DRS GetCurrentGlobalProfile");
        return profile;
    }

    public (uint Value, bool Explicit) Get(nint profile, uint setting)
    {
        var buffer = NewSetting(setting);
        fixed (byte* pointer = buffer)
        {
            Check(
                ((delegate* unmanaged[Cdecl]<nint, nint, uint, byte*, int>)Function(0x73bf8338))(Drs, profile, setting,
                    pointer), "DRS GetSetting");
        }

        if (Number(buffer, 4104) != 0)
        {
            throw new DriverFailure("The setting is not a documented DWORD setting.");
        }

        return (Number(buffer, CurrentValueOffset), Number(buffer, 4108) == 0 && Number(buffer, 4112) == 0);
    }

    public void Set(nint profile, uint setting, uint value, WriteAdmission admission)
    {
        var buffer = NewSetting(setting);
        Number(buffer, CurrentValueOffset, value);
        fixed (byte* pointer = buffer)
        {
            var set = (delegate* unmanaged[Cdecl]<nint, nint, byte*, int>)Function(0x577dd202);
            admission.Check();
            Check(set(Drs, profile, pointer),
                "DRS SetSetting", true);
        }
    }

    public void Inherit(nint profile, uint setting, WriteAdmission admission)
    {
        var inherit = (delegate* unmanaged[Cdecl]<nint, nint, uint, int>)Function(0xe4a26362);
        admission.Check();
        // Delete this one user override. Predefined settings and every unrelated setting remain.
        var status = inherit(Drs, profile, setting);
        if (status != -160)
        {
            Check(status, "DRS DeleteProfileSetting", true);
        }
    }

    public nint Application(string executable, bool create, string profileName, WriteAdmission admission)
    {
        if (executable.Length is 0 or > 260 || Path.GetFileName(executable) != executable
                                            || executable.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                                            executable.Any(PlainText.IsUnsafe)
                                            || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new DriverFailure("A native NVIDIA profile requires a plain executable filename.");
        }

        // Creating a profile or an application only stages it in the session; Save, which checks admission,
        // is what commits it.
        var app = new byte[ApplicationSize];
        Number(app, 0, Version(ApplicationSize, 4));
        Unicode(app, 8, executable);
        nint profile = 0;
        int status;
        fixed (char* name = executable)
        fixed (byte* pointer = app)
        {
            status = ((delegate* unmanaged[Cdecl]<nint, char*, nint*, byte*, int>)Function(0xeee566b2))(Drs, name,
                &profile, pointer);
        }

        if (status == 0)
        {
            return profile;
        }

        // NVAPI_EXECUTABLE_NOT_FOUND is the sole permission to create an application association.
        if (status == -166 && !create)
        {
            return 0;
        }

        if (status != -166)
        {
            Check(status, "DRS FindApplicationByName");
        }

        var info = new byte[ProfileSize];
        Number(info, 0, Version(ProfileSize, 1));
        Unicode(info, 4, profileName);
        fixed (char* name = profileName)
        {
            status = ((delegate* unmanaged[Cdecl]<nint, char*, nint*, int>)Function(0x7e4a9a0b))(Drs, name, &profile);
        }

        if (status == -163)
        {
            fixed (byte* pointer = info)
            {
                var createProfile = (delegate* unmanaged[Cdecl]<nint, byte*, nint*, int>)Function(0xcc176068);
                admission.Check();
                Check(
                    createProfile(Drs, pointer, &profile),
                    "DRS CreateProfile", true);
            }
        }
        else
        {
            Check(status, "DRS FindProfileByName");
        }

        fixed (byte* pointer = app)
        {
            var createApplication = (delegate* unmanaged[Cdecl]<nint, nint, byte*, int>)Function(0x4347a9de);
            admission.Check();
            Check(createApplication(Drs, profile, pointer),
                "DRS CreateApplication", true);
        }

        return profile;
    }

    public string ProfileName(nint profile)
    {
        var info = new byte[ProfileSize];
        Number(info, 0, Version(ProfileSize, 1));
        fixed (byte* pointer = info)
        {
            Check(((delegate* unmanaged[Cdecl]<nint, nint, byte*, int>)Function(0x61cd6fd6))(Drs, profile, pointer),
                "DRS GetProfileInfo");
        }

        return Encoding.Unicode.GetString(info, 4, 4096).Split('\0')[0];
    }

    public nint FindProfile(string name)
    {
        nint profile = 0;
        int status;
        fixed (char* text = name)
        {
            status = ((delegate* unmanaged[Cdecl]<nint, char*, nint*, int>)Function(0x7e4a9a0b))(Drs, text, &profile);
        }

        if (status == -163)
        {
            return 0;
        }

        Check(status, "DRS FindProfileByName");
        return profile;
    }

    public IReadOnlyList<NvNamedProfile> Profiles()
    {
        var profiles = new List<NvNamedProfile>();
        var enumerate = (delegate* unmanaged[Cdecl]<nint, uint, nint*, int>)Function(0xbc371ee0);
        for (uint index = 0; ; index++)
        {
            nint profile = 0;
            var status = enumerate(Drs, index, &profile);
            if (status == -7) // NVAPI_END_ENUMERATION
            {
                return profiles;
            }

            Check(status, "DRS EnumProfiles");
            profiles.Add(new NvNamedProfile(profile, ProfileName(profile)));
        }
    }

    public void DeleteProfile(nint profile, WriteAdmission admission)
    {
        var deleteProfile = (delegate* unmanaged[Cdecl]<nint, nint, int>)Function(0x17093206);
        admission.Check();
        Check(deleteProfile(Drs, profile),
            "DRS DeleteProfile", true);
    }

    internal void BeginPass()
    {
        _colors.Clear();
        _colorFailures.Clear();
        Load();
    }

    internal nint Function(uint id)
    {
        return FunctionPointer(id) is var pointer && pointer != 0
            ? pointer
            : throw new DriverFailure($"NVAPI function 0x{id:X8} is unavailable.");
    }

    private nint FunctionPointer(uint id)
    {
        if (!_functions.TryGetValue(id, out var pointer))
        {
            pointer = _query(id);
            _functions.Add(id, pointer);
        }

        return pointer;
    }

    internal HashSet<uint> SettingIds()
    {
        var values = new uint[4096];
        while (true)
        {
            var count = (uint)values.Length;
            int status;
            fixed (uint* pointer = values)
            {
                status = ((delegate* unmanaged[Cdecl]<uint*, uint*, int>)Function(0xf020614a))(pointer, &count);
            }

            // NVAPI_END_ENUMERATION: the buffer cannot hold every id. Size it from the reported count when that
            // is larger, else double it, and ask again; this is a read.
            if (status == -7 || (status == 0 && count > values.Length))
            {
                values = new uint[Math.Max(count, (uint)values.Length * 2)];
                continue;
            }

            Check(status, "DRS EnumAvailableSettingIds");
            return values.Take((int)count).ToHashSet();
        }
    }

    internal uint[] Values(uint setting)
    {
        var buffer = _valuesBuffer;
        Array.Clear(buffer);
        Number(buffer, 0, Version(ValuesSize, 1));
        uint count = 100;
        fixed (byte* pointer = buffer)
        {
            Check(((delegate* unmanaged[Cdecl]<uint, uint*, byte*, int>)Function(0x2ec39f90))(setting, &count, pointer),
                "DRS EnumAvailableSettingValues");
        }

        if (Number(buffer, 8) != 0 || count > 100 || Number(buffer, 4) > 100)
        {
            throw new DriverFailure("The setting value table is unsupported.");
        }

        return Enumerable.Range(0, (int)Math.Min(count, Number(buffer, 4)))
            .Select(index => Number(buffer, 12 + ValueStride + index * ValueStride)).Distinct().ToArray();
    }

    internal NvOutput[] Displays()
    {
        var gpus = new nint[64];
        uint count = 0;
        fixed (nint* pointer = gpus)
        {
            Check(((delegate* unmanaged[Cdecl]<nint*, uint*, int>)Function(0xe5ac921f))(pointer, &count),
                "EnumPhysicalGPUs");
        }

        if (count is 0 or > 64)
        {
            throw new DriverFailure("No NVIDIA physical GPU is available.");
        }

        var displays = new Dictionary<uint, NvOutput>();
        foreach (var gpu in gpus.Take((int)count))
        {
            uint outputs = 0;
            var get = (delegate* unmanaged[Cdecl]<nint, byte*, uint*, uint, int>)Function(0x0078dba2);
            var status = get(gpu, null, &outputs, 1); // NV_GPU_CONNECTED_IDS_FLAG_UNCACHED
            if (status is -207 or -210 or -220) // no connector/display or an unpowered Optimus adapter
            {
                continue;
            }

            Check(status, "GetConnectedDisplayIds count");
            if (outputs is 0 or > 64)
            {
                continue;
            }

            var data = new byte[outputs * 16];
            for (var index = 0; index < outputs; index++)
            {
                Number(data, index * 16, Version(16, 3));
            }

            fixed (byte* pointer = data)
            {
                Check(get(gpu, pointer, &outputs, 1), "GetConnectedDisplayIds");
            }

            if (outputs > data.Length / 16)
            {
                throw new DriverFailure("The NVIDIA display topology changed during enumeration.");
            }

            for (var index = 0; index < outputs; index++)
            {
                if ((Number(data, index * 16 + 12) & 4) != 0)
                {
                    var display = Number(data, index * 16 + 8);
                    displays[display] = new NvOutput(display, gpu);
                }
            }
        }

        var routes = displays.Count == 0 ? new DriverDisplayTargets([], []) : DriverDisplayTargets.Capture();
        foreach (var display in displays.Keys.ToArray())
        {
            var matches = routes.Active.Where(path => DisplayId(path.SourceName) == display).ToArray();
            var sourceName = matches.Length == 1 ? matches[0].SourceName : null;
            var target = matches.Length == 1 ? matches[0].Target : null;
            string? edid = null;
            if (target is null)
            {
                // GetDisplayIdByDisplayName only resolves active outputs. The native EDID query also
                // reaches connected inactive outputs without enabling them or guessing by a name.
                try
                {
                    edid = DisplayEdid.ParseIdentity(ReadEdid(display));
                    target = routes.ByEdid(edid);
                }
                catch (DriverFailure failure) when (!failure.Lost)
                {
                }
            }

            displays[display] = displays[display] with
            {
                Target = target,
                Route = routes.Describe(target, display.ToString("x8"), sourceName, edid)
            };
        }

        return displays.Values.OrderBy(output => output.Id).ToArray();
    }

    /// <summary>Reads the documented NV_EDID_DATA_V1 by native display ID, including inactive connectors.</summary>
    internal byte[] ReadEdid(uint display)
    {
        var get = (delegate* unmanaged[Cdecl]<uint, NvEdidData*, uint*, int>)Function(0x436ced76);
        var data = new NvEdidData { Version = Version(sizeof(NvEdidData), 1) };
        uint flags = 0;
        Check(get(display, &data, &flags), "DISP_GetEdidData size");
        if (data.Size is < 128 or > 32768)
        {
            throw new DriverFailure("The NVIDIA display EDID size is invalid.");
        }

        var bytes = new byte[data.Size];
        fixed (byte* pointer = bytes)
        {
            data.Data = pointer;
            Check(get(display, &data, &flags), "DISP_GetEdidData");
        }

        return bytes;
    }

    private uint? DisplayId(string sourceName)
    {
        var name = Encoding.ASCII.GetBytes(sourceName + '\0');
        uint id = 0;
        fixed (byte* pointer = name)
        {
            return ((delegate* unmanaged[Cdecl]<byte*, uint*, int>)Function(0xae457190))(pointer, &id) == 0 ? id : null;
        }
    }

    internal uint Vrr(uint display)
    {
        var data = new byte[24];
        Number(data, 0, Version(data.Length, 1));
        fixed (byte* pointer = data)
        {
            Check(((delegate* unmanaged[Cdecl]<uint, byte*, int>)Function(0xdf8fda57))(display, pointer),
                "Disp_GetVRRInfo");
        }

        return Number(data, 4);
    }

    public NvAdaptiveSync AdaptiveSync(uint display)
    {
        // NV_GET_ADAPTIVE_SYNC_DATA_V1, with the 64-bit timestamp aligned to eight bytes.
        var data = new byte[40];
        Number(data, 0, Version(data.Length, 1));
        fixed (byte* pointer = data)
        {
            Check(((delegate* unmanaged[Cdecl]<uint, byte*, int>)Function(0xb73d1ee9))(display, pointer),
                "DISP_GetAdaptiveSyncData");
        }

        return new NvAdaptiveSync(Number(data, 4), Number(data, 8));
    }

    public void SetAdaptiveSync(uint display, NvAdaptiveSync current, bool enabled, WriteAdmission admission)
    {
        // NV_SET_ADAPTIVE_SYNC_DATA_V2. Keep frame splitting and the configured frame interval.
        var data = new byte[40];
        Number(data, 0, Version(data.Length, 2));
        Number(data, 8, (current.Flags & 2u) | (enabled ? 0u : 1u));
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(16), (ulong)current.MaxFrameInterval * 1000);
        var function = (delegate* unmanaged[Cdecl]<uint, byte*, int>)Function(0x3eebba1d);
        fixed (byte* pointer = data)
        {
            admission.Check();
            Check(function(display, pointer), "DISP_SetAdaptiveSyncData", true);
        }
    }

    internal uint HdrCapabilities(uint display)
    {
        var data = new byte[64]; // NV_HDR_CAPABILITIES_V3, including both metadata blocks
        Number(data, 0, Version(data.Length, 3));
        fixed (byte* pointer = data)
        {
            Check(((delegate* unmanaged[Cdecl]<uint, byte*, int>)Function(0x84f2a8df))(display, pointer),
                "Disp_GetHdrCapabilities");
        }

        return Number(data, 4);
    }

    /// <summary>Reads the colour block (command 1) or asks whether one is supported (command 3).</summary>
    /// <param name="display">NVAPI display id captured by the session's topology discovery.</param>
    /// <param name="command">NV_COLOR_CMD value: 1 reads current color, 3 validates a complete proposed combination.</param>
    /// <param name="input">Native color-v5 buffer to clone, or null for a fresh zeroed query buffer.</param>
    /// <returns>
    ///     A new color-v5 buffer containing the driver response; the supplied input is unchanged and native failures
    ///     throw.
    /// </returns>
    internal byte[] Color(uint display, byte command, byte[]? input = null)
    {
        var buffer = ColorBuffer(command, input);
        fixed (byte* pointer = buffer)
        {
            Check(((delegate* unmanaged[Cdecl]<uint, byte*, int>)Function(0x92f9d80d))(display, pointer),
                "Disp_ColorControl");
        }

        return buffer;
    }

    internal byte[] ReadColor(uint display)
    {
        if (_colorFailures.TryGetValue(display, out var failure))
        {
            throw failure;
        }

        if (!_colors.TryGetValue(display, out var data))
        {
            try
            {
                data = Color(display, 1);
            }
            catch (DriverFailure error)
            {
                _colorFailures.Add(display, error);
                throw;
            }

            _colors.Add(display, data);
        }

        return data;
    }

    /// <summary>Writes a colour block, checking admission immediately before the native call.</summary>
    /// <param name="display">NVAPI display id captured by the session's topology discovery.</param>
    /// <param name="data">Complete native color-v5 state; copied before the command/version fields are set.</param>
    /// <param name="admission">Cancellation and live-session permission checked immediately before the setter.</param>
    internal void SetColor(uint display, byte[] data, WriteAdmission admission)
    {
        _colors.Remove(display);
        _colorFailures.Remove(display);
        var buffer = ColorBuffer(2, data);
        fixed (byte* pointer = buffer)
        {
            admission.Check();
            Check(((delegate* unmanaged[Cdecl]<uint, byte*, int>)Function(0x92f9d80d))(display, pointer),
                "Disp_ColorControl", true);
        }
    }

    private static byte[] ColorBuffer(byte command, byte[]? input)
    {
        var buffer = input is null ? new byte[ColorSize] : (byte[])input.Clone();
        Number(buffer, 0, Version(ColorSize, 5));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), ColorSize);
        buffer[6] = command;
        return buffer;
    }

    internal bool SupportsColor(uint display, byte[] input)
    {
        try
        {
            Color(display, 3, input);
            return true;
        }
        catch (DriverFailure failure) when (!failure.Lost)
        {
            return false;
        }
    }

    internal bool HasFunction(uint id)
    {
        return FunctionPointer(id) != 0;
    }

    internal static uint Version(int size, int revision)
    {
        return (uint)size | ((uint)revision << 16);
    }

    internal static uint Number(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    }

    internal static void Number(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    }

    private byte[] NewSetting(uint setting)
    {
        var data = _settingBuffer;
        Array.Clear(data);
        Number(data, 0, Version(SettingSize, 1));
        Number(data, SettingIdOffset, setting);
        return data;
    }

    private static void Unicode(byte[] data, int offset, string value)
    {
        Encoding.Unicode.GetBytes(value.AsSpan(0, Math.Min(value.Length, 2047)), data.AsSpan(offset, 4096));
    }

    internal static void Check(int status, string operation, bool attempted = false)
    {
        if (status != 0)
        {
            var refused = status is -3 or -5 or -9 or -104 or -137 or -160 or -166 or -175;
            throw new DriverFailure($"NVAPI {operation} returned {status}.", attempted && !refused,
                status is -4 or -6 or -8 or -10);
        }
    }
}

internal sealed record NvOutput(uint Id, nint Gpu, DisplayTargetIdentity? Target = null, GpuDisplayTarget? Route = null);

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NvEdidData
{
    internal uint Version;
    internal byte* Data;
    internal uint Size;
}
