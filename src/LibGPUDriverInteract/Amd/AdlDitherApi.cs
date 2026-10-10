// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Amd;

/// <summary>The documented ADL2 dithering calls, mapped from the ADLX display instead of a fixed index.</summary>
internal sealed unsafe class AdlDitherApi : IDisposable
{
    private readonly delegate* unmanaged[Cdecl]<nint, int, int, int*, int> _get;
    private readonly delegate* unmanaged[Cdecl]<nint, int, int, int, int> _set;
    private nint _context;
    private nint _library;

    internal AdlDitherApi()
    {
        _library = SystemLibrary.Load("atiadlxx.dll");
        try
        {
            NativeLibrary.TryGetExport(_library, "ADL2_Display_DitherState_Get", out var get);
            NativeLibrary.TryGetExport(_library, "ADL2_Display_DitherState_Set", out var set);
            _get = (delegate* unmanaged[Cdecl]<nint, int, int, int*, int>)get;
            _set = (delegate* unmanaged[Cdecl]<nint, int, int, int, int>)set;
            nint context = 0;
            Check(((delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<int, nint>, int, nint*, int>)
                    NativeLibrary.GetExport(_library, "ADL2_Main_Control_Create"))(&Allocate, 1, &context),
                "Main_Control_Create");
            _context = context != 0 ? context : throw new DriverFailure("ADL2 returned no context.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal bool SupportsDithering => _get != null && _set != null;

    /// <summary>Resolves the mapped ADL output to its actual Windows GDI source, never its EDID label.</summary>
    internal string? SourceName(int adapter, int display)
    {
        if (!NativeLibrary.TryGetExport(_library, "ADL2_Display_DisplayInfo_Get", out var displayInfo)
            || !NativeLibrary.TryGetExport(_library, "ADL2_Adapter_NumberOfAdapters_Get", out var adapterCount)
            || !NativeLibrary.TryGetExport(_library, "ADL2_Adapter_AdapterInfo_Get", out var adapterInfo))
        {
            return null;
        }

        var outputCount = 0;
        nint outputs = 0;
        try
        {
            Check(((delegate* unmanaged[Cdecl]<nint, int, int*, nint*, int, int>)displayInfo)(
                _context, adapter, &outputCount, &outputs, 0), "Display_DisplayInfo_Get");
            if (outputCount < 0 || outputCount > 4096 || outputCount > 0 && outputs == 0)
            {
                throw new DriverFailure("The AMD display route table is invalid.");
            }

            var outputRoutes = new List<AdlOutputRoute>();
            for (var index = 0; index < outputCount; index++)
            {
                // ADLDisplayInfo starts with ADLDisplayID: logical display, physical display,
                // logical adapter, physical adapter. Its complete Windows ABI is 552 bytes.
                outputRoutes.Add(new AdlOutputRoute(Marshal.ReadInt32(outputs, index * 552),
                    Marshal.ReadInt32(outputs, index * 552 + 8)));
            }

            var count = 0;
            Check(((delegate* unmanaged[Cdecl]<nint, int*, int>)adapterCount)(_context, &count),
                "Adapter_NumberOfAdapters_Get");
            if (count is < 0 or > 4096)
            {
                throw new DriverFailure("The AMD adapter route table is invalid.");
            }

            var bytes = new byte[checked(count * 1572)];
            fixed (byte* pointer = bytes)
            {
                for (var index = 0; index < count; index++)
                {
                    *(int*)(pointer + index * 1572) = 1572;
                }

                Check(((delegate* unmanaged[Cdecl]<nint, byte*, int, int>)adapterInfo)(
                    _context, pointer, bytes.Length), "Adapter_AdapterInfo_Get");
            }

            var adapterRoutes = new List<AdlAdapterRoute>();
            for (var index = 0; index < count; index++)
            {
                var source = Encoding.ASCII.GetString(bytes, index * 1572 + 536, 256).Split('\0')[0];
                adapterRoutes.Add(new AdlAdapterRoute(BitConverter.ToInt32(bytes, index * 1572 + 4), source));
            }

            return ResolveSource(display, outputRoutes, adapterRoutes);
        }
        finally
        {
            if (outputs != 0)
            {
                Marshal.FreeCoTaskMem(outputs);
            }
        }
    }

    internal static string? ResolveSource(int display, IReadOnlyList<AdlOutputRoute> outputs,
        IReadOnlyList<AdlAdapterRoute> adapters)
    {
        var matches = outputs.Where(output => output.DisplayIndex == display).Take(2).ToArray();
        if (matches.Length != 1)
        {
            return null;
        }

        var sources = adapters.Where(adapter => adapter.AdapterIndex == matches[0].AdapterIndex)
            .Select(adapter => adapter.SourceName).Where(source => source.StartsWith(@"\\.\DISPLAY",
                StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return sources.Length == 1 ? sources[0] : null;
    }

    public void Dispose()
    {
        if (_library == 0)
        {
            return;
        }

        try
        {
            if (_context != 0)
            {
                ((delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(_library, "ADL2_Main_Control_Destroy"))(
                    _context);
                _context = 0;
            }
        }
        finally
        {
            NativeLibrary.Free(_library);
            _library = 0;
        }
    }

    internal int Read(int adapter, int display)
    {
        if (!SupportsDithering)
        {
            throw new DriverFailure("AMD dithering calls are unavailable.");
        }

        var state = 0;
        Check(_get(_context, adapter, display, &state), "Display_DitherState_Get");
        return state;
    }

    internal void Write(int adapter, int display, int state, WriteAdmission admission)
    {
        if (!SupportsDithering)
        {
            throw new DriverFailure("AMD dithering calls are unavailable.");
        }

        admission.Check();
        Check(_set(_context, adapter, display, state), "Display_DitherState_Set", true);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint Allocate(int size)
    {
        try
        {
            return size > 0 ? Marshal.AllocCoTaskMem(size) : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void Check(int status, string operation, bool attempted = false)
    {
        if (status != 0)
        {
            throw new DriverFailure($"ADL2 {operation} returned {status}.", attempted && status is not (-3 or -8));
        }
    }
}

internal sealed record AdlOutputRoute(int DisplayIndex, int AdapterIndex);
internal sealed record AdlAdapterRoute(int AdapterIndex, string SourceName);
