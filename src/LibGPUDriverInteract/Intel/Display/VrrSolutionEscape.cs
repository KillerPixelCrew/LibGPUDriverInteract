// SPDX-License-Identifier: MIT
// The escape layout and operations follow ClawLab's MSI Claw Intel VRR Range Fix 3.0.0
// (scripts\Intel-VRR-LFC-Driver-Interface.ps1), Copyright (c) 2026 ClawLab contributors, MIT License;
// see THIRD_PARTY_NOTICES.md.

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace LibGPUDriverInteract.Intel.Display;

/// <summary>What the Intel private VRR escape answered.</summary>
/// <param name="Status">The <c>D3DKMTEscape</c> NTSTATUS; zero is success.</param>
/// <param name="State">The state the driver reported, or null when the escape failed.</param>
internal readonly record struct VrrEscapeAnswer(uint Status, GpuVrrSolutions? State);

/// <summary>
///     Intel's private D3DKMT VRR escape: reads the adapter's variable refresh state and turns the low and high
///     frame rate solutions on or off.
/// </summary>
/// <remarks>
///     A 124-byte driver-private payload: display escape major 1 at offset 8, VRR minor 10 at offset 12, the
///     operation at 16. The answer carries support, enabled, high and low frame rate solution flags at bytes 20
///     to 23. Operation 0 reads; 3 and 4 turn the low frame rate solution (LFC) on and off; 5 and 6 the high frame
///     rate solution. ClawLab opens the adapter through the device context of the one active Intel display; this
///     opens the same adapter by the LUID of the output the caller named, so it never guesses which adapter.
///     Hardware behavior is the Atlas8Tweaks and ClawLab testing's, not this library's.
/// </remarks>
internal static partial class VrrSolutionEscape
{
    /// <summary>The read operation.</summary>
    public const uint Read = 0;

    private const int PayloadSize = 124;
    private const int HeaderSize = 16;
    private const uint DisplayEscapeMajor = 1;
    private const uint VrrEscapeMinor = 10;
    private const uint DriverPrivate = 0;

    /// <summary>The operation that turns one solution on or off.</summary>
    /// <param name="solution">The solution.</param>
    /// <param name="enabled">The request.</param>
    /// <returns>3/4 for the low frame rate solution, 5/6 for the high one.</returns>
    public static uint Operation(GpuVrrSolution solution, bool enabled)
    {
        return solution switch
        {
            GpuVrrSolution.LowFrameRate => enabled ? 3u : 4u,
            GpuVrrSolution.HighFrameRate => enabled ? 5u : 6u,
            _ => throw new ArgumentOutOfRangeException(nameof(solution))
        };
    }

    /// <summary>The request payload for one operation.</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The payload.</returns>
    public static byte[] Request(uint operation)
    {
        var payload = new byte[PayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), DisplayEscapeMajor);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), VrrEscapeMinor);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(HeaderSize), operation);
        return payload;
    }

    /// <summary>The state an answered payload reports.</summary>
    /// <param name="payload">The payload after the escape.</param>
    /// <returns>The flags.</returns>
    public static GpuVrrSolutions Parse(ReadOnlySpan<byte> payload)
    {
        return new GpuVrrSolutions(payload[20] == 1, payload[21] == 1, payload[23] == 1, payload[22] == 1);
    }

    /// <summary>Sends one operation to the adapter with this LUID.</summary>
    /// <param name="luid">The adapter LUID.</param>
    /// <param name="operation">The operation.</param>
    /// <returns>The escape status and, on success, the state it reported.</returns>
    public static unsafe VrrEscapeAnswer Invoke(long luid, uint operation)
    {
        OpenAdapterFromLuid open = default;
        open.AdapterLuid = luid;
        var opened = D3DKMTOpenAdapterFromLuid(ref open);
        if (opened != 0 || open.Adapter == 0)
        {
            return new VrrEscapeAnswer(unchecked((uint)opened), null);
        }

        try
        {
            var payload = Request(operation);
            fixed (byte* data = payload)
            {
                Escape escape = default;
                escape.Adapter = open.Adapter;
                escape.Type = DriverPrivate;
                escape.PrivateDriverData = (nint)data;
                escape.PrivateDriverDataSize = PayloadSize;
                var status = unchecked((uint)D3DKMTEscape(ref escape));
                return status == 0 ? new VrrEscapeAnswer(0, Parse(payload)) : new VrrEscapeAnswer(status, null);
            }
        }
        finally
        {
            CloseAdapter close = default;
            close.Adapter = open.Adapter;
            _ = D3DKMTCloseAdapter(ref close);
        }
    }

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTOpenAdapterFromLuid(ref OpenAdapterFromLuid data);

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTCloseAdapter(ref CloseAdapter data);

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTEscape(ref Escape data);

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromLuid
    {
        public long AdapterLuid;
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter
    {
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Escape
    {
        public uint Adapter;
        public uint Device;
        public uint Type;
        public uint Flags;
        public nint PrivateDriverData;
        public uint PrivateDriverDataSize;
        public uint Context;
    }
}
