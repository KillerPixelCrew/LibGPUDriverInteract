using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SharpCompress.Archives;

namespace LibGPUDriverInteract;

/// <summary>Downloads, verifies and runs vendor installers without retrying a driver write.</summary>
public sealed class GpuDriverPackageInstaller(GpuDriverUpdateCatalog catalog)
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibGPUDriverInteract", "DriverUpdates");

    /// <summary>Downloads into an administrator-protected directory and checks the catalog size, hash and signer.</summary>
    public async Task<string> DownloadAsync(GpuDriverPackage package, IProgress<int>? progress = null,
        CancellationToken token = default)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            using var response = await catalog.SendAsync(package.Download, HttpMethod.Get, token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } length && length != package.Size)
            {
                throw new InvalidDataException("The official package size changed. Check for updates again.");
            }

            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                var reported = -1;
                int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > package.Size)
                    {
                        throw new InvalidDataException("The package exceeds the official size.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    var percent = (int)(100 * total / package.Size);
                    if (percent != reported)
                    {
                        reported = percent;
                        progress?.Report(percent);
                    }
                }
            }

            await using var held = OpenHeld(path);
            await VerifyAsync(package, path, held, token).ConfigureAwait(false);
            return path;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    /// <summary>Reverifies and extracts the selected package, checks admission again, then runs its supported installer.</summary>
    /// <remarks>Cancellation stops preparation only. Once started the installer is awaited without killing or retrying it.</remarks>
    public async Task<GpuDriverInstallResult> InstallAsync(GpuDriverPackage package, string path,
        Func<string?> admission, CancellationToken token = default)
    {
        if (!Path.GetDirectoryName(Path.GetFullPath(path))!.Equals(Root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The package is outside the protected download directory.");
        }

        await using var held = OpenHeld(path);
        await VerifyAsync(package, path, held, token).ConfigureAwait(false);
        var directory = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            return await RunAsync().ConfigureAwait(false);
        }
        finally
        {
            // A fresh GUID child of the protected root, never a caller-selected deletion target.
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Driver package extraction cleanup: " + error.Message);
            }
        }

        async Task<GpuDriverInstallResult> RunAsync()
        {
            await Task.Run(() => Extract(held, directory, token), token).ConfigureAwait(false);
            var setup = Path.Combine(directory, package.Adapter.Vendor == GpuVendor.Intel ? "Installer.exe" : "setup.exe");
            if (!File.Exists(setup))
            {
                throw new InvalidDataException("The vendor package did not contain its expected installer.");
            }

            await using var executable = OpenHeld(setup);
            DriverPackageSignature.Verify(setup, executable, package.Adapter.Vendor);
            token.ThrowIfCancellationRequested();
            if (admission() is { } refusal)
            {
                throw new InvalidOperationException(refusal);
            }

            var amdLog = Path.Combine(directory, "install-result.log");
            var arguments = package.Adapter.Vendor == GpuVendor.Amd
                ? package.Arguments + " -LOG \"" + amdLog + "\""
                : package.Arguments;
            using var process = Process.Start(new ProcessStartInfo(setup, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(setup)!
            }) ?? throw new InvalidOperationException("The vendor installer could not be started.");
            // A caller cancellation must never terminate a live display-driver installation.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            var code = process.ExitCode;
            if (package.Adapter.Vendor == GpuVendor.Amd && code is 0 or 3010)
            {
                return File.Exists(amdLog)
                    ? ReadAmdResult(code, await File.ReadAllTextAsync(amdLog, CancellationToken.None).ConfigureAwait(false))
                    : new GpuDriverInstallResult(false, code == 3010, code,
                        "AMD did not publish its installation result. Check the installed version before another explicit attempt.");
            }

            return ReadExitCode(package.Adapter.Vendor, code);
        }
    }

    internal static GpuDriverInstallResult ReadExitCode(GpuVendor vendor, int code)
    {
        var reboot = code == 3010 || (vendor == GpuVendor.Nvidia && code == 1)
            || (vendor == GpuVendor.Intel && code is 2 or 14 or 17 or 18);
        var success = code == 0 || reboot;
        var detail = vendor == GpuVendor.Intel && code is 17 or 18
            ? "Intel driver installed. Shut Windows down, then power the device on to finish."
            : success ? reboot ? "Driver installed. Restart Windows to finish." : "The vendor installer completed successfully."
            : $"The vendor installer returned {code}. Check the installed driver before another explicit attempt."
                + (vendor == GpuVendor.Intel && code == 13 ? " Restart Windows before trying again." : "");
        return new GpuDriverInstallResult(success, reboot, code, detail);
    }

    internal static GpuDriverInstallResult ReadAmdResult(int exitCode, string log)
    {
        var result = Regex.Matches(log, @"(?im)^\s*ResultCode\s*=\s*(\d+)\s*$");
        var packages = Regex.Matches(log, @"(?im)^\s*ErrorCode\s*=\s*(\d+)\s*$")
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var overall = result.Count == 1 ? int.Parse(result[0].Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture) : -1;
        var succeeded = overall == 0 && packages.All(code => code is 0 or 3);
        var reboot = exitCode == 3010 || overall == 2 || packages.Any(code => code is 2 or 3);
        return new GpuDriverInstallResult(succeeded, reboot, exitCode, succeeded
            ? reboot ? "AMD driver installed. Restart Windows to finish." : "AMD driver installation completed."
            : "AMD reported a failed or unconfirmed installation. Check the installed version before another explicit attempt."
                + (reboot ? " Restart Windows before trying again." : ""));
    }

    private static FileStream OpenHeld(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    private static async Task VerifyAsync(GpuDriverPackage package, string path, FileStream held,
        CancellationToken token)
    {
        if (held.Length != package.Size)
        {
            throw new InvalidDataException("The downloaded package does not match its official size.");
        }

        if (package.Checksum is { } expected)
        {
            using var hash = IncrementalHash.CreateHash(expected.Length switch
            {
                40 => HashAlgorithmName.SHA1,
                64 => HashAlgorithmName.SHA256,
                128 => HashAlgorithmName.SHA512,
                _ => throw new InvalidDataException("The vendor checksum has an unsupported format.")
            });
            var buffer = new byte[81920];
            held.Position = 0;
            int count;
            while ((count = await held.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, count);
            }

            if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(expected)))
            {
                throw new CryptographicException("The package checksum does not match the official catalog.");
            }
        }

        DriverPackageSignature.Verify(path, held, package.Adapter.Vendor);
    }

    private static void Extract(FileStream input, string directory, CancellationToken token)
    {
        input.Position = 0;
        var header = new byte[(int)Math.Min(input.Length, 4 * 1024 * 1024)];
        input.ReadExactly(header);
        ReadOnlySpan<byte> signature = [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c];
        var offset = header.AsSpan().IndexOf(signature);
        if (offset < 0)
        {
            throw new InvalidDataException("The vendor installer contains no supported 7-Zip payload.");
        }

        var payload = Path.Combine(directory, "payload.7z");
        input.Position = offset;
        using (var output = new FileStream(payload, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            input.CopyTo(output);
        }

        using var archive = ArchiveFactory.OpenArchive(payload);
        using var reader = archive.ExtractAllEntries();
        long extracted = 0;
        while (reader.MoveToNextEntry())
        {
            token.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            var target = Path.GetFullPath(Path.Combine(directory, entry.Key ?? ""));
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
            while ((count = source.Read(buffer)) > 0)
            {
                token.ThrowIfCancellationRequested();
                destination.Write(buffer, 0, count);
            }
        }
    }
}
