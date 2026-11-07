using System.Diagnostics;
using System.Security.Cryptography;
using SharpCompress.Archives;

namespace LibGPUDriverInteract;

/// <summary>Downloads, verifies and runs vendor installers without retrying a driver write.</summary>
/// <param name="catalog">The official source client.</param>
/// <param name="protectedRoot">Caller-owned administrator-protected temporary storage.</param>
public sealed class GpuDriverPackageInstaller(GpuDriverUpdateCatalog catalog, string protectedRoot)
{
    private readonly string _root = Path.GetFullPath(protectedRoot);

    /// <summary>Hashes while downloading, verifies Authenticode once, and retains the file handle until installation ends.</summary>
    public async Task<GpuDriverDownload> DownloadAsync(GpuDriverPackage package, IProgress<int>? progress = null,
        CancellationToken token = default)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".exe");
        // Synchronous: WinVerifyTrust reads through this handle without OVERLAPPED, and an overlapped
        // handle makes a correctly signed package fail with TRUST_E_BAD_DIGEST.
        var held = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 81920,
            FileOptions.SequentialScan);
        try
        {
            var referrer = package.Adapter.Vendor == GpuVendor.Amd ? package.Details : null;
            using var response = await catalog.SendAsync(package.Download, HttpMethod.Get, token, referrer).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } length && length != package.Size)
            {
                throw new InvalidDataException("The official package size changed. Check for updates again.");
            }

            using var hash = package.Checksum is { } expected ? IncrementalHash.CreateHash(expected.Length switch
            {
                40 => HashAlgorithmName.SHA1,
                64 => HashAlgorithmName.SHA256,
                128 => HashAlgorithmName.SHA512,
                _ => throw new InvalidDataException("The vendor checksum has an unsupported format.")
            }) : null;
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await GpuDriverUpdateCatalog.CopyBoundedAsync(input, held, package.Size, token, progress, hash).ConfigureAwait(false);
            await held.FlushAsync(token).ConfigureAwait(false);
            if (held.Length != package.Size)
            {
                throw new InvalidDataException("The downloaded package does not match its official size.");
            }

            if (hash is not null && !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(),
                    Convert.FromHexString(package.Checksum!)))
            {
                throw new CryptographicException("The package checksum does not match the official catalog.");
            }

            DriverPackageSignature.Verify(path, held, package.Adapter.Vendor);
            return new GpuDriverDownload(package, path, held);
        }
        catch
        {
            await held.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
            throw;
        }
    }

    /// <summary>Extracts the held verified file, checks admission again, then runs the supported installer.</summary>
    /// <remarks>Cancellation stops preparation only. Once started the installer is awaited without killing or retrying it.</remarks>
    public async Task<GpuDriverInstallResult> InstallAsync(GpuDriverDownload download,
        Func<string?> admission, CancellationToken token = default)
    {
        if (!Path.GetDirectoryName(Path.GetFullPath(download.Path))!.Equals(_root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The package is outside the protected download directory.");
        }

        var package = download.Package;
        var profile = GpuDriverUpdateProfile.For(package.Adapter.Vendor);
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await Task.Run(() => Extract(download.Stream, directory, profile.InstallerName, token), token).ConfigureAwait(false);
            var setup = Path.Combine(directory, profile.InstallerName);
            await using var executable = new FileStream(setup, FileMode.Open, FileAccess.Read, FileShare.Read);
            DriverPackageSignature.Verify(setup, executable, package.Adapter.Vendor);
            token.ThrowIfCancellationRequested();
            if (admission() is { } refusal)
            {
                throw new InvalidOperationException(refusal);
            }

            var resultLog = Path.Combine(directory, "install-result.log");
            using var process = Process.Start(new ProcessStartInfo(setup, profile.InstallArguments(resultLog))
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory
            }) ?? throw new InvalidOperationException("The vendor installer could not be started.");
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return await profile.ReadResultAsync(process.ExitCode, resultLog).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Driver package extraction cleanup: " + error.Message);
            }
        }
    }

    internal static void Extract(FileStream input, string directory, string installerName, CancellationToken token)
    {
        directory = Path.GetFullPath(directory);
        using var archive = OpenPayload(input, installerName);
        using var reader = archive.ExtractAllEntries();
        long extracted = 0;
        while (reader.MoveToNextEntry())
        {
            token.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            var target = Path.GetFullPath(Path.Combine(directory, entry.Key ?? ""));
            if (target.Equals(directory, StringComparison.OrdinalIgnoreCase) && entry.IsDirectory)
            {
                continue;
            }

            if (!target.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || entry.LinkTarget is not null || entry.Size < 0 || (extracted += entry.Size) > 12L * 1024 * 1024 * 1024)
            {
                throw new InvalidDataException("The vendor archive contains an unsafe entry.");
            }

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = reader.OpenEntryStream();
            using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            int count;
            long copied = 0;
            while ((count = source.Read(buffer)) > 0)
            {
                token.ThrowIfCancellationRequested();
                if ((copied += count) > entry.Size)
                {
                    throw new InvalidDataException("An archive entry exceeded its declared size.");
                }

                destination.Write(buffer, 0, count);
            }
        }
    }

    internal static IArchive OpenPayload(Stream input, string installerName)
    {
        input.Position = 0;
        var header = new byte[(int)Math.Min(input.Length, 4 * 1024 * 1024)];
        input.ReadExactly(header);
        foreach (var offset in ArchiveCandidates(header))
        {
            var window = new DriverArchiveStream(input, offset);
            IArchive? archive = null;
            try
            {
                archive = ArchiveFactory.OpenArchive(window);
                // Parse the candidate's archive headers and require the documented root installer.
                // A byte sequence in the PE stub is not proof of an archive, nor is an unrelated embedded archive.
                if (archive.Entries.Any(entry => !entry.IsDirectory && string.Equals(entry.Key, installerName,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return archive;
                }
            }
            catch (Exception error) when (error is not (OutOfMemoryException or OperationCanceledException))
            {
                // Only candidate parsing is retried, never a driver write.
            }

            archive?.Dispose();
            window.Dispose();
        }

        throw new InvalidDataException("The vendor package contains no valid supported archive with its expected installer.");
    }

    private static IEnumerable<int> ArchiveCandidates(byte[] header)
    {
        byte[][] signatures = [[0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c],
            [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00], [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00]];
        SortedSet<int> candidates = [];
        foreach (var signature in signatures)
        {
            var start = 0;
            while (start < header.Length)
            {
                var found = header.AsSpan(start).IndexOf(signature);
                if (found < 0)
                {
                    break;
                }
                start += found;
                candidates.Add(start++);
                if (candidates.Count > 128)
                {
                    throw new InvalidDataException("Too many archive candidates in the vendor stub.");
                }
            }
        }

        return candidates;
    }
}
