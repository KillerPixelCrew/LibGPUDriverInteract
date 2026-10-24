using System.Runtime.InteropServices;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Runtime;
using Microsoft.Win32;
using Xunit;

namespace LibGPUDriverInteract.Tests.Runtime;

public sealed class ScaledResolutionCodecTests
{
    [Fact]
    public void NvidiaStorageUsesMultiStringAndRefusesOtherKindsBeforeWriting()
    {
        ScaledResolutionRegistry.ValidateNvidiaStorage(null, null);
        ScaledResolutionRegistry.ValidateNvidiaStorage(new[] { "{*}SHV 1760x1100=1" }, RegistryValueKind.MultiString);
        Assert.Throws<InvalidDataException>(() => ScaledResolutionRegistry.ValidateNvidiaStorage(
            "{*}SHV 1760x1100=1", RegistryValueKind.String));
        Assert.Throws<InvalidDataException>(() => ScaledResolutionRegistry.ValidateNvidiaStorage(
            new byte[] { 1, 2 }, RegistryValueKind.Binary));
    }

    [Fact]
    public void IntelAbiUsesNativeAlignmentAndOnlySourceDimensions()
    {
        Assert.Equal(8, Marshal.SizeOf<CtlCustomSourceMode>());
        Assert.Equal(16, Marshal.OffsetOf<CtlCustomModeArgs>(nameof(CtlCustomModeArgs.Modes)).ToInt32());
        Assert.Equal(IntPtr.Size == 8 ? 24 : 20, Marshal.SizeOf<CtlCustomModeArgs>());
    }

    [Fact]
    public void AmdAdditionRemovalPreservesExistingRefreshFlagsMalformedRecordsAndTrailingBytes()
    {
        var mode = new GpuScaledResolution(1760, 1100);
        byte[] unrelated = [0x19, 0x20, 0x10, 0x80, 0, 0, 0x00, 0x60];
        var added = ScaledResolutionRegistry.AddAmdBytes(unrelated, mode);
        Assert.Equal(new byte[] { 0x17, 0x60, 0x11, 0x00, 0, 0, 0, 0 }, added[8..]);
        Assert.Equal(unrelated, ScaledResolutionRegistry.EditAmdBytes(added, mode, remove: true));
        byte[] malformed = [.. added, 0xff, 0x12, 0x34];
        byte[] expected = [.. unrelated, 0xff, 0x12, 0x34];
        Assert.Equal(expected,
            ScaledResolutionRegistry.EditAmdBytes(malformed, mode, remove: true));
        Assert.Throws<InvalidDataException>(() => ScaledResolutionRegistry.AddAmdBytes(malformed, mode));
    }

    [Fact]
    public void AmdRemovalDoesNotDeleteOtherRefreshOrFlagsForTheSameDimensions()
    {
        var mode = new GpuScaledResolution(1760, 1100);
        byte[] bytes = [0x17, 0x60, 0x11, 0x00, 1, 0, 0, 60];
        Assert.Equal(bytes, ScaledResolutionRegistry.EditAmdBytes(bytes, mode, remove: true));
    }

    [Fact]
    public void NvidiaRemovalPreservesOtherGroupsQualifiersAndFlags()
    {
        const string original = "{DISPLAY-A}SHV 1920x1080 1760x1100=1 {DISPLAY-B}SHV 1280x720=2";
        var edited = ScaledResolutionRegistry.EditNvidiaText(original, new GpuScaledResolution(1760, 1100));
        Assert.Equal("{DISPLAY-A}SHV 1920x1080=1 {DISPLAY-B}SHV 1280x720=2", edited);
        Assert.Equal(edited, ScaledResolutionRegistry.EditNvidiaText(edited, new GpuScaledResolution(1760, 1100)));
    }

    [Fact]
    public void NvidiaRepeatedOwnedGroupRemovalLeavesNoBogusOrEmptyGroups()
    {
        var mode = new GpuScaledResolution(1760, 1100);
        for (var count = 0; count < 3; count++)
        {
            Assert.Empty(ScaledResolutionRegistry.EditNvidiaText("{*}SHV 1760x1100=1", mode));
        }
    }

    [Fact]
    public void NvidiaMalformedAndExtendedDriverDataIsPreserved()
    {
        const string original = "  vendor-private 1760x1100x32@60=opaque {A}NEWFORMAT 1760x1100=1  ";
        Assert.Equal(original, ScaledResolutionRegistry.EditNvidiaText(original, new GpuScaledResolution(1760, 1100)));
    }
}
