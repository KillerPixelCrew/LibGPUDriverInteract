using LibGPUDriverInteract.Runtime;
using Xunit;

namespace LibGPUDriverInteract.Tests.Api;

public sealed class GpuVendorDetectionTests
{
    [Fact]
    public void PresentDevicesIdentifyAllVendorsWithoutOptionalDriverRegistryMetadata()
    {
        const string devices = "PCI\\VEN_8086&DEV_64A0\\3&one\0"
                               + "PCI\\VEN_1002&DEV_1900\\3&two\0"
                               + "pci\\ven_10de&dev_28a0\\3&three\0"
                               + "PCI\\VEN_8086&DEV_1234\\3&four\0\0";
        uint Size(out uint length)
        {
            length = (uint)devices.Length;
            return 0;
        }

        uint Read(char[] buffer, uint length)
        {
            Assert.Equal((uint)devices.Length, length);
            devices.CopyTo(0, buffer, 0, devices.Length);
            return 0;
        }

        Assert.Equal(new[] { GpuVendor.Intel, GpuVendor.Amd, GpuVendor.Nvidia }, WindowsGpuVendors.Collect(Size, Read));
    }

    [Fact]
    public void VirtualMalformedAndInstanceSuffixVendorStringsDoNotAdmitADriver()
    {
        const string devices = "ROOT\\VEN_8086&DEV_7D55\\one\0"
                               + "PCI\\XVEN_1002&DEV_1900\\two\0"
                               + "PCI\\VEN_10DE1&DEV_28A0\\three\0"
                               + "PCI\\VEN_1234&DEV_5678\\VEN_8086\0\0";
        Assert.Empty(WindowsGpuVendors.Parse(devices));
    }

    [Fact]
    public void EnumerationRereadsOnlyWhenHotplugGrowsTheDeviceList()
    {
        const string devices = "PCI\\VEN_8086&DEV_7D55\\one\0\0";
        var reads = 0;
        var sizes = 0;
        uint Size(out uint length)
        {
            sizes++;
            length = sizes == 1 ? 2u : (uint)devices.Length;
            return 0;
        }

        uint Read(char[] buffer, uint length)
        {
            reads++;
            if (reads == 1)
            {
                return WindowsGpuVendors.BufferSmall;
            }

            Assert.Equal((uint)devices.Length, length);
            devices.CopyTo(0, buffer, 0, devices.Length);
            return 0;
        }

        Assert.Equal(new[] { GpuVendor.Intel }, WindowsGpuVendors.Collect(Size, Read));
        Assert.Equal(2, sizes);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void EnumerationFailureIsReportedRatherThanPretendingNoGpuExists()
    {
        uint Size(out uint length)
        {
            length = 0;
            return 0x33;
        }

        var error = Assert.Throws<InvalidOperationException>(() => WindowsGpuVendors.Collect(Size,
            (_, _) => throw new InvalidOperationException("The failed size lookup must not read.")));
        Assert.Contains("0x00000033", error.Message);
    }

    [Fact]
    public void ContinuouslyChangingInventoryCannotLoopForever()
    {
        var reads = 0;
        uint Size(out uint length)
        {
            length = 2;
            return 0;
        }

        var error = Assert.Throws<InvalidOperationException>(() => WindowsGpuVendors.Collect(Size, (_, _) =>
        {
            reads++;
            return WindowsGpuVendors.BufferSmall;
        }));
        Assert.Equal(3, reads);
        Assert.Contains("0x0000001a", error.Message);
    }
}
