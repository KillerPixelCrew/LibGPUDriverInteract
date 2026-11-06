using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LibGPUDriverInteract;

/// <summary>Authenticode trust and vendor identity for bytes held against replacement.</summary>
internal static class DriverPackageSignature
{
    internal static void Verify(string path, FileStream held, GpuVendor vendor)
    {
        var file = new TrustFile
        {
            Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path,
            Handle = held.SafeFileHandle.DangerousGetHandle()
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(file, pointer, false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Revocation = 1, Choice = 1,
                File = pointer, StateAction = 1, Flags = 0x80
            };
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            var result = WinVerifyTrust(0, ref action, ref data);
            data.StateAction = 2;
            WinVerifyTrust(0, ref action, ref data);
            if (result != 0)
            {
                throw new CryptographicException($"Driver package signature verification failed: 0x{result:x8}.");
            }

#pragma warning disable SYSLIB0057
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            string[] expected = vendor switch
            {
                GpuVendor.Intel => ["Intel Corporation"],
                GpuVendor.Amd => ["Advanced Micro Devices", "Advanced Micro Devices, Inc.", "Advanced Micro Devices Inc."],
                GpuVendor.Nvidia => ["NVIDIA Corporation"],
                _ => throw new NotSupportedException("Unknown driver signer.")
            };
            if (!expected.Contains(signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false), StringComparer.OrdinalIgnoreCase))
            {
                throw new CryptographicException($"The driver package was not signed by {expected[0]}.");
            }
        }
        finally
        {
            Marshal.DestroyStructure<TrustFile>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile
    {
        public uint Size;
        public string Path;
        public nint Handle;
        public nint Subject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public nint Policy;
        public nint Sip;
        public uint Ui;
        public uint Revocation;
        public uint Choice;
        public nint File;
        public uint StateAction;
        public nint State;
        public nint Url;
        public uint Flags;
        public uint Context;
        public nint Signature;
    }
}
