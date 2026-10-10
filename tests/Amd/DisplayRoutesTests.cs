using Xunit;

namespace LibGPUDriverInteract.Amd.Tests;

public sealed class DisplayRoutesTests
{
    [Fact]
    public void NativeDisplayMappingUsesItsLogicalAdapterRatherThanTheFirstAdapterOrFriendlyName()
    {
        var outputs = new[] { new AdlOutputRoute(3, 8), new AdlOutputRoute(4, 9) };
        var adapters = new[] { new AdlAdapterRoute(9, @"\\.\DISPLAY2"), new AdlAdapterRoute(8, @"\\.\DISPLAY1") };
        Assert.Equal(@"\\.\DISPLAY2", AdlDitherApi.ResolveSource(4, outputs, adapters));
    }

    [Fact]
    public void AmbiguousOrUnknownNativeDisplayMappingIsNotGuessed()
    {
        var outputs = new[] { new AdlOutputRoute(3, 8), new AdlOutputRoute(3, 9) };
        var adapters = new[] { new AdlAdapterRoute(9, @"\\.\DISPLAY2"), new AdlAdapterRoute(8, @"\\.\DISPLAY1") };
        Assert.Null(AdlDitherApi.ResolveSource(3, outputs, adapters));
        Assert.Null(AdlDitherApi.ResolveSource(7, outputs, adapters));
    }

    [Fact]
    public void AnEdidLabelIsNotTreatedAsAWindowsDisplaySource()
    {
        Assert.Null(AdlDitherApi.ResolveSource(3, [new AdlOutputRoute(3, 8)],
            [new AdlAdapterRoute(8, "Gaming monitor")]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Gaming monitor")]
    [InlineData("00ff")]
    public void MalformedNativeEdidHasNoPhysicalSerialIdentity(string edid)
    {
        Assert.Null(AdlxSession.EdidIdentity(edid));
    }
}
