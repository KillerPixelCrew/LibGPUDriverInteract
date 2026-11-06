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
            new Uri("https://www.amd.com/notes"), "Windows Driver Store Version 32.0.32015.12012", 10, null, "-INSTALL");
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
        var result = GpuDriverPackageInstaller.ReadAmdResult(0, log);
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
        var result = GpuDriverPackageInstaller.ReadExitCode(vendor, code);
        Assert.Equal(success, result.Succeeded);
        Assert.Equal(restart, result.RestartRequired);
    }

    private static GpuDriverPackage Package(GpuVendor vendor, string installed, string offered) => new(
        new GpuInstalledDriver("adapter", "hardware", vendor, "GPU", installed, new DateTime(2026, 1, 1)),
        offered, new DateTime(2026, 10, 30), new Uri("https://downloadmirror.intel.com/driver.exe"),
        new Uri("https://www.intel.com/notes"), "Notes", 10, null, "-s");
}
