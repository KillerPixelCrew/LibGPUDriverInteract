using LibGPUDriverInteract.Runtime;
using Xunit;

namespace LibGPUDriverInteract.Nvidia.Tests;

public sealed class NvGsyncTests
{
    [Fact]
    public void DisablingOneOutputPreservesTheOtherOutputAndItsFrameSettings()
    {
        var api = new AdaptiveSyncApi();
        var first = new NvGsyncControl(api, 7, "output-first", "first");
        var second = new NvGsyncControl(api, 9, "output-second", "second");
        first.Write(GpuValue.Boolean(false), new WriteAdmission(CancellationToken.None, () => true));

        Assert.False(first.Read().BooleanValue);
        Assert.True(second.Read().BooleanValue);
        Assert.Equal(new NvAdaptiveSync(16667, 3), api.Values[7]);
        Assert.Equal(new NvAdaptiveSync(8333, 0), api.Values[9]);
        Assert.Equal([7u], api.Written);
        Assert.Equal("output-first", first.Descriptor.InstanceId);
        Assert.Equal("display.gsync", first.Descriptor.CapabilityId);
        Assert.Equal(GpuRole.Toggle, first.Descriptor.Role);
    }

    [Fact]
    public void MissingFlipDataDoesNotInventReadback()
    {
        var api = new AdaptiveSyncApi { HasFlipData = false };
        var control = new NvGsyncControl(api, 7, "output-first", "first");

        Assert.Throws<DriverFailure>(() => control.Read());
        Assert.Empty(api.Written);
    }

    [Theory]
    [InlineData(true, 0u)]
    [InlineData(false, 1u)]
    public void MissingFlipDataStillWritesOnceUsingEdidDefaults(bool enabled, uint flags)
    {
        var api = new AdaptiveSyncApi { HasFlipData = false };
        var control = new NvGsyncControl(api, 7, "output-first", "first");

        control.Write(GpuValue.Boolean(enabled), new WriteAdmission(CancellationToken.None, () => true));

        Assert.Equal(new NvAdaptiveSync(0, flags), api.Values[7]);
        Assert.Equal(new NvAdaptiveSync(8333, 0), api.Values[9]);
        Assert.Equal([7u], api.Written);
    }

    [Fact]
    public void RevokedAdmissionDoesNotWriteEitherOutput()
    {
        var api = new AdaptiveSyncApi();
        var control = new NvGsyncControl(api, 7, "output-first", "first");
        Assert.Throws<DriverFailure>(() => control.Write(GpuValue.Boolean(false),
            new WriteAdmission(CancellationToken.None, () => false)));
        Assert.Empty(api.Written);
        Assert.True(control.Read().BooleanValue);
    }

    private sealed class AdaptiveSyncApi : INvAdaptiveSync
    {
        internal readonly Dictionary<uint, NvAdaptiveSync> Values = new()
        {
            [7] = new NvAdaptiveSync(16667, 2),
            [9] = new NvAdaptiveSync(8333, 0)
        };

        internal readonly List<uint> Written = [];

        internal bool HasFlipData { get; init; } = true;

        public NvAdaptiveSync? AdaptiveSync(uint display)
        {
            return HasFlipData ? Values[display] : null;
        }

        public void SetAdaptiveSync(uint display, NvAdaptiveSync current, bool enabled, WriteAdmission admission)
        {
            admission.Check();
            Values[display] = current with { Flags = (current.Flags & 2u) | (enabled ? 0u : 1u) };
            Written.Add(display);
        }
    }
}
