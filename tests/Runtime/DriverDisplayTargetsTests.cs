using LibGPUDriverInteract.Runtime;
using WindowsDeviceControl;
using Xunit;

namespace LibGPUDriverInteract.Tests.Runtime;

public sealed class DriverDisplayTargetsTests
{
    [Fact]
    public void ConnectedInactiveOutputResolvesItsPhysicalIdentityWithoutAGdiSource()
    {
        var target = Target("monitor-one", 7, "serial-one");
        var routes = new DriverDisplayTargets([target], []);
        var observed = routes.Describe(routes.ByEdid("serial-one"), "native-output");
        Assert.Equal("monitor-one", observed.DevicePath);
        Assert.Equal(7u, observed.TargetId);
        Assert.Null(observed.SourceName);
    }

    [Fact]
    public void DuplicateEdidSerialsAreAmbiguousButExactCcdRouteRemainsUsable()
    {
        var first = Target("monitor-one", 7, "duplicate-serial");
        var second = Target("monitor-two", 8, "duplicate-serial");
        var routes = new DriverDisplayTargets([first, second], []);
        Assert.Null(routes.ByEdid("duplicate-serial"));
        Assert.Equal(second, routes.ByRoute(123, 456, 8));
    }

    [Fact]
    public void ClonedGdiSourceDoesNotGuessOneOfItsPhysicalOutputs()
    {
        var first = Target("monitor-one", 7, "serial-one");
        var second = Target("monitor-two", 8, "serial-two");
        var routes = new DriverDisplayTargets([first, second],
            [new ActiveDisplayPath(first, @"\\.\DISPLAY1", 0, 60, 1),
                new ActiveDisplayPath(second, @"\\.\DISPLAY1", 0, 60, 1)]);
        Assert.Null(routes.BySource(@"\\.\DISPLAY1"));
        Assert.Equal(first, routes.ByEdid("serial-one"));
    }

    [Fact]
    public void ActiveOutputPreservesExactPhysicalAndCurrentWindowsCoordinates()
    {
        var target = Target("monitor-one", 7, "serial-one");
        var routes = new DriverDisplayTargets([target],
            [new ActiveDisplayPath(target, @"\\.\DISPLAY2", 0, 60, 1)]);
        var observed = routes.Describe(routes.BySource(@"\\.\display2"), "native-output");
        Assert.Equal("monitor-one", observed.DevicePath);
        Assert.Equal("serial-one", observed.EdidIdentity);
        Assert.Equal(@"\\.\DISPLAY2", observed.SourceName);
        Assert.Equal(123u, observed.AdapterLowPart);
        Assert.Equal(456, observed.AdapterHighPart);
        Assert.Equal(7u, observed.TargetId);
        Assert.Equal("native-output", observed.NativeOutputId);
    }

    private static DisplayTargetIdentity Target(string path, uint target, string serial)
    {
        return new DisplayTargetIdentity(path, 1, 2, "Identical friendly label", 123, 456, target)
        {
            EdidIdentity = serial
        };
    }
}
