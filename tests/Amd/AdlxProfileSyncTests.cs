using LibGPUDriverInteract.Amd;
using Xunit;

namespace LibGPUDriverInteract.Tests.Amd;

public sealed class AdlxProfileSyncTests
{
    [Fact]
    public void NativeProfileRequestsAreRejectedWithEachRequestedIdentity()
    {
        var sync = new GpuProfileSync(1, [
            new GpuApplicationProfile("game", "Game", ["game.exe"],
                [new GpuApplicationValue("graphics.sharpening", "adapter", GpuValue.Integer(50))])
        ]);
        var failure = Assert.Single(AdlxSession.SyncProfiles(sync, CancellationToken.None).Failures);
        Assert.Equal("game", failure.ProfileId);
        Assert.Equal("game.exe", failure.Executable);
        Assert.Equal("graphics.sharpening", failure.CapabilityId);
        Assert.Contains("not supported", failure.Detail);
    }

    [Fact]
    public void EmptyNativeProfileSetIsANoOpForTheGpuWideBackend()
    {
        Assert.Empty(AdlxSession.SyncProfiles(new GpuProfileSync(1, []), CancellationToken.None).Failures);
        Assert.Empty(AdlxSession.SyncProfiles(new GpuProfileSync(2,
            [new GpuApplicationProfile("game", "Game", ["game.exe"], [])]), CancellationToken.None).Failures);
    }
}
