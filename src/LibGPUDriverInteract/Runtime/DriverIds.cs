// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LibGPUDriverInteract.Runtime;

internal static class DriverIds
{
    internal static string Key(string capabilityId, string? instanceId)
    {
        return capabilityId + "|" + instanceId;
    }

    internal static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();
    }
}

internal static class SystemLibrary
{
    internal static nint Load(string name)
    {
        return NativeLibrary.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name));
    }

    internal static bool TryLoad(string name, out nint library)
    {
        return NativeLibrary.TryLoad(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name),
            out library);
    }
}
