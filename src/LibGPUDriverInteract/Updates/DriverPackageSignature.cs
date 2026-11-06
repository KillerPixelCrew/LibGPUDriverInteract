using System.Security.Cryptography;
using WindowsDeviceControl;

namespace LibGPUDriverInteract;

/// <summary>Applies the vendor's expected signer names through the shared held-handle Authenticode primitive.</summary>
internal static class DriverPackageSignature
{
    internal static void Verify(string path, FileStream held, GpuVendor vendor)
    {
        using var result = Authenticode.Verify(path, held.SafeFileHandle, AuthenticodeRevocation.ChainExcludingRoot,
            AuthenticodeSigner.WithName(GpuDriverUpdateProfile.For(vendor).SignerNames));
        if (result.Outcome != AuthenticodeOutcome.Trusted || result.TrustStatus != 0)
        {
            throw new CryptographicException($"Driver package verification failed: {result.Outcome}, "
                + $"WinVerifyTrust 0x{result.TrustStatus:x8}. {result.SignerError}");
        }
    }
}
