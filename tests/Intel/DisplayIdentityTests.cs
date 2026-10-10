using LibGPUDriverInteract.Intel.Display;
using LibGPUDriverInteract.Intel.Tests.Fakes;
using LibGPUDriverInteract.Runtime;
using LibGPUDriverInteract.Testing;
using WindowsDeviceControl;
using Xunit;

namespace LibGPUDriverInteract.Intel.Tests;

/// <summary>The display instance ids WSGM remembers settings under.</summary>
public sealed class DisplayIdentityTests
{
    private const string Path = @"\\?\DISPLAY#BOE0B78#4&2a8b4c3&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    [Fact]
    public void DriverEnumerationRetainsAttachedInactiveMonitorsAndRejectsUnattachedConnectors()
    {
        var native = new FakeIgclDriver { DisplayFlags = [3, 2, 1, 0] };
        using var session = native.OpenSession();
        Assert.Equal(new[] { 100u, 101u }, session.Outputs.Select(output => output.TargetId));
    }

    [Fact]
    public void ConnectedInactiveIntelOutputRetainsItsPhysicalIdentityAndExactRoute()
    {
        var native = new FakeIgclDriver { DisplayFlags = [2] };
        using var session = native.OpenSession();
        var output = Assert.Single(session.Outputs);
        var target = new DisplayTargetIdentity(Path, 0x1234, 0x5678, "Monitor", 0, 0, 100)
        {
            EdidIdentity = "serial-one"
        };
        var resolved = DisplayIdentityResolver.Resolve(output, new RecordingDriverSink(),
            new DriverDisplayTargets([target], []));
        Assert.Equal(Path, resolved.Target.DevicePath);
        Assert.Equal("serial-one", resolved.Target.EdidIdentity);
        Assert.Equal(100u, resolved.Target.TargetId);
        Assert.Null(resolved.Target.SourceName);
    }

    [Fact]
    public void TheBuiltInPanelStartsWithInternal()
    {
        // WSGM's host gives Valve's single variable refresh row to an instance starting with "internal".
        var id = DisplayIdentityResolver.InstanceId(true, "boe", 0x0b78, Path);

        Assert.StartsWith("internal-edid-boe0b78-", id);
    }

    [Fact]
    public void AnExternalMonitorHasNoPrefix()
    {
        Assert.StartsWith("edid-sam", DisplayIdentityResolver.InstanceId(false, "sam", 0x7061, Path));
    }

    [Fact]
    public void WithoutEdidCodesTheFingerprintAloneNamesIt()
    {
        Assert.Matches("^internal-display-[0-9a-f]{8}$", DisplayIdentityResolver.InstanceId(true, null, 0, Path));
    }

    [Fact]
    public void TheIdIsStableForTheSameMonitorAndConnector()
    {
        Assert.Equal(
            DisplayIdentityResolver.InstanceId(false, "boe", 0x0b78, Path),
            DisplayIdentityResolver.InstanceId(false, "boe", 0x0b78, Path.ToUpperInvariant()));
    }
}
