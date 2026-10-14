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

        public NvAdaptiveSync AdaptiveSync(uint display)
        {
            return Values[display];
        }

        public void SetAdaptiveSync(uint display, NvAdaptiveSync current, bool enabled, WriteAdmission admission)
        {
            admission.Check();
            Values[display] = current with { Flags = (current.Flags & 2u) | (enabled ? 0u : 1u) };
            Written.Add(display);
        }
    }
}
