using System.IO;
using Xunit;

namespace LibGPUDriverInteract.Tests.Updates;

public sealed class DriverUpdateSafetyTests
{
    [Theory]
    [InlineData("http://downloadmirror.intel.com/driver.exe")]
    [InlineData("https://downloadmirror.intel.com.evil.example/driver.exe")]
    [InlineData("https://user@downloadmirror.intel.com/driver.exe")]
    [InlineData("https://drivers.amd.com:8443/driver.exe")]
    [InlineData("file:///C:/driver.exe")]
    public void RejectsUnofficialOrNonHttpsPackageSources(string value)
    {
        Assert.Throws<InvalidDataException>(() => GpuDriverUpdateCatalog.CheckedUri(value));
    }

    [Theory]
    [InlineData("32.0.15.7216", "572.16", false)]
    [InlineData("32.0.15.7216", "621.14", true)]
    [InlineData("32.0.15.7216", "571.96", false)]
    public void NvidiaComparesWindowsAndPublishedVersions(string installed, string offered, bool expected)
    {
        Assert.Equal(expected, GpuDriverUpdateCatalog.IsNewer(Package(GpuVendor.Nvidia, installed, offered)));
    }

    [Fact]
    public void UnknownInstalledVersionDoesNotClaimUpToDate()
    {
        Assert.Throws<InvalidDataException>(() => GpuDriverUpdateCatalog.IsNewer(Package(GpuVendor.Intel,
            "Unknown", "32.0.101.7092")));
    }

    [Fact]
    public void AmdSameDriverStoreVersionDoesNotReinstallThePackage()
    {
        var package = new GpuDriverPackage(new GpuInstalledDriver("adapter", "VEN_1002&DEV_747E", GpuVendor.Amd,
                "AMD Radeon RX 7800 XT", "32.0.32015.12012", new DateTime(2026, 1, 1)),
            "26.10.2", new DateTime(2026, 10, 30), new Uri("https://drivers.amd.com/driver.exe"),
            new Uri("https://www.amd.com/notes"), "Windows Driver Store Version 32.0.32015.12012", 10, null);
        Assert.False(GpuDriverUpdateCatalog.IsNewer(package));
    }

    [Fact]
    public async Task BoundedResponsesRejectOversizedInput()
    {
        using var input = new MemoryStream(new byte[20]);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => GpuDriverUpdateCatalog.CopyBoundedAsync(input,
            output, 10, CancellationToken.None));
    }

    [Theory]
    [InlineData("[ResponseResult]\nResultCode = 0\n[Details]\nErrorCode = 0", true, false)]
    [InlineData("[ResponseResult]\nResultCode = 0\n[Details]\nErrorCode = 3", true, true)]
    [InlineData("[ResponseResult]\nResultCode = 0\n[Details]\nErrorCode = 1", false, false)]
    [InlineData("[ResponseResult]\nResultCode = 2\n[Details]\nErrorCode = 2", false, true)]
    [InlineData("", false, false)]
    public void AmdResultLogPreservesPartialFailureAndRestart(string log, bool success, bool restart)
    {
        var result = GpuDriverUpdateProfile.ReadAmdResult(0, log);
        Assert.Equal(success, result.Succeeded);
        Assert.Equal(restart, result.RestartRequired);
    }

    [Theory]
    [InlineData(GpuVendor.Intel, 0, true, false)]
    [InlineData(GpuVendor.Intel, 1, false, false)]
    [InlineData(GpuVendor.Intel, 13, false, false)]
    [InlineData(GpuVendor.Intel, 14, true, true)]
    [InlineData(GpuVendor.Intel, 18, true, true)]
    [InlineData(GpuVendor.Nvidia, 1, true, true)]
    [InlineData(GpuVendor.Amd, 1, false, false)]
    public void VendorExitCodesAreNotInterchangeable(GpuVendor vendor, int code, bool success, bool restart)
    {
        var result = GpuDriverUpdateProfile.For(vendor).ReadExitCode(code);
        Assert.Equal(success, result.Succeeded);
        Assert.Equal(restart, result.RestartRequired);
    }

    [Theory]
    [InlineData("9-5-2026", 2026, 9, 5)]
    [InlineData("09-25-2026", 2026, 9, 25)]
    public void RegistryDatesUseTheDocumentedMonthDayYearFormat(string text, int year, int month, int day)
    {
        Assert.Equal(new DateTime(year, month, day), GpuInstalledDriver.ParseDate(text));
        Assert.Null(GpuInstalledDriver.ParseDate("25-09-2026"));
    }

    [Theory]
    [InlineData(39, 40)]
    [InlineData(63, 64)]
    [InlineData(127, 128)]
    public void NumericallyEncodedChecksumsPreserveTheirAlgorithmWidth(int supplied, int width)
    {
        var checksum = GpuDriverUpdateCatalog.NormalizeChecksum(new string('f', supplied));
        Assert.Equal(width, checksum.Length);
        Assert.StartsWith("0", checksum);
        Assert.Equal(width / 2, Convert.FromHexString(checksum).Length);
    }

    [Fact]
    public void NvidiaDateUsesTheRecordedEnglishLookupFormat()
    {
        Assert.Equal(new DateTime(2026, 11, 4), GpuDriverUpdateCatalog.ParseNvidiaDate("Wed Nov 04, 2026"));
        Assert.Throws<FormatException>(() => GpuDriverUpdateCatalog.ParseNvidiaDate("04.11.2026"));
    }

    [Fact]
    public void MissingAmdDateIsAnUnavailableComparisonRatherThanAnErrorOrAnInstallOffer()
    {
        var package = new GpuDriverPackage(new GpuInstalledDriver("adapter", "VEN_1002&DEV_747E", GpuVendor.Amd,
                "AMD Radeon", "32.0.25000.0", null), "26.10.2", new DateTime(2026, 10, 30),
            new Uri("https://drivers.amd.com/driver.exe"), new Uri("https://www.amd.com/notes"),
            "Windows Driver Store Version 32.0.21045.20006; Windows Driver Store Version 32.0.32015.12012", 10, null);
        Assert.Null(GpuDriverUpdateCatalog.IsNewer(package));
    }

    [Fact]
    public async Task CopyHashesTheStreamAndDoesNotPublishEveryChunk()
    {
        var data = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(data);
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var progress = new List<int>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await GpuDriverUpdateCatalog.CopyBoundedAsync(input, output, data.Length, CancellationToken.None,
            new ProgressSink(progress), hash);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(data), hash.GetHashAndReset());
        Assert.Equal(data.Length, output.Length);
        Assert.Equal(100, progress[^1]);
        Assert.InRange(progress.Count, 1, (int)(clock.Elapsed.TotalMilliseconds / 250) + 3);
    }

    [Fact]
    public async Task DownloadOwnerRetainsItsWriteAndDeleteExclusionUntilDisposed()
    {
        var path = Path.GetTempFileName();
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var download = new GpuDriverDownload(Package(GpuVendor.Intel, "1.0", "2.0"), path, stream);
            Assert.Throws<IOException>(() => File.Open(path, FileMode.Open, FileAccess.Write).Dispose());
            await download.DisposeAsync();
            using var writable = File.Open(path, FileMode.Open, FileAccess.Write);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ArchiveParsingSkipsAStubSignatureAndAllowsAnEntryCalledPayload()
    {
        // A small inert 7-Zip fixture containing setup.exe text and an entry named payload.7z.
        var archive = Convert.FromBase64String("N3q8ryccAAS57Jn3ngAAAAAAAAAgAAAAAAAAAIintdNmaXh0dXJlIHBheWxvYWQgZW50cnlmaXh0dXJlIHNldHVwOyBuZXZlciBleGVjdXRlZAAAgTMHrjGZpkgVnMO602zcmn47PFdh/LOe1V5/pD2GHVQftM2NwTVN+eGHeE90yEqV3GvFgftvcFVvqY9vg8BIRWyVC/QSDqZF/GzJ9X4k/xgJfWqbIOAQhUZF55FQKvub9ODFT5lG6zcAABcGMgEJbAAHCwEAASMDAQEFXQAQAAAMfgoB1IltFwAA");
        var root = Path.Combine(Path.GetTempPath(), "driver-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "package.exe");
        try
        {
            var stub = new byte[96];
            Convert.FromHexString("377ABCAF271C").CopyTo(stub, 16);
            File.WriteAllBytes(path, [.. stub, .. archive]);
            using var held = File.OpenRead(path);
            var destination = Path.Combine(root, "extracted");
            Directory.CreateDirectory(destination);
            GpuDriverPackageInstaller.Extract(held, destination, "setup.exe", CancellationToken.None);
            Assert.Equal("fixture payload entry", File.ReadAllText(Path.Combine(destination, "payload.7z")));
            Assert.Equal("fixture setup; never executed", File.ReadAllText(Path.Combine(destination, "setup.exe")));
            Assert.True(held.CanRead);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ProgressSink(List<int> values) : IProgress<int>
    {
        public void Report(int value) => values.Add(value);
    }

    private static GpuDriverPackage Package(GpuVendor vendor, string installed, string offered) => new(
        new GpuInstalledDriver("adapter", "hardware", vendor, "GPU", installed, new DateTime(2026, 1, 1)),
        offered, new DateTime(2026, 10, 30), new Uri("https://downloadmirror.intel.com/driver.exe"),
        new Uri("https://www.intel.com/notes"), "Notes", 10, null);
}
