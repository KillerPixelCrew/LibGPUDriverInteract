using LibGPUDriverInteract.Intel.Display;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Intel.Tests.Fakes;
using LibGPUDriverInteract.Runtime;
using LibGPUDriverInteract.Testing;
using Xunit;

namespace LibGPUDriverInteract.Intel.Tests;

/// <summary>The Arc Sync profile written by the policy that owns it, and Intel's private VRR escape payload.</summary>
/// <remarks>Blind: the fake driver stands in for IGCL, and the escape is checked as bytes, not on hardware.</remarks>
public sealed class ArcSyncPolicyTests
{
    private const string Instance = "internal-edid-test";
    private static readonly WriteAdmission Admitted = new(CancellationToken.None, static () => true);

    [Fact]
    public void APolicyProfileIsWrittenAsGivenAndIsWhatEnablingReturnsTo()
    {
        using var fixture = new Fixture(28, 120);
        var excellent = ArcSyncDisplay.ToNative(new GpuArcSyncBaseline(GpuArcSyncProfile.Excellent, 28, 120));

        Assert.Equal(IgclResult.Success, fixture.Display.WritePolicyProfile(excellent, Admitted, out var held));

        Assert.False(held);
        var written = Assert.Single(fixture.Native.ArcWrites);
        Assert.Equal((ArcSyncDisplay.ProfileExcellent, 28f, 120f), (written.Profile, written.MinimumHz,
            written.MaximumHz));
        fixture.Display.WriteEnabled(false, Admitted);
        fixture.Display.WriteEnabled(true, Admitted);
        Assert.Equal(ArcSyncDisplay.ProfileExcellent, fixture.Native.ArcProfileState.Profile);
    }

    [Fact]
    public void AFixedRateTheDriverStillRunsKeepsThePolicyWriteBack()
    {
        using var fixture = new Fixture(28, 120);
        fixture.Display.WriteFixedRate(60, Admitted, out _);
        fixture.Native.ArcWrites.Clear();
        fixture.Session.BeginPass();

        var excellent = ArcSyncDisplay.ToNative(new GpuArcSyncBaseline(GpuArcSyncProfile.Excellent, 28, 120));
        fixture.Display.WritePolicyProfile(excellent, Admitted, out var held);

        Assert.True(held);
        Assert.Empty(fixture.Native.ArcWrites);
        Assert.Equal(60f, fixture.Display.FixedHz);
    }

    [Fact]
    public void AFixedRateSomebodyElseReplacedIsDroppedAndThePolicyProfileWritten()
    {
        using var fixture = new Fixture(28, 120);
        fixture.Display.WriteFixedRate(60, Admitted, out _);
        fixture.Native.ArcProfileState = FakeIgclDriver.RecommendedProfile();
        fixture.Native.ArcWrites.Clear();
        fixture.Session.BeginPass();

        var excellent = ArcSyncDisplay.ToNative(new GpuArcSyncBaseline(GpuArcSyncProfile.Excellent, 28, 120));
        fixture.Display.WritePolicyProfile(excellent, Admitted, out var held);

        Assert.False(held);
        Assert.Single(fixture.Native.ArcWrites);
        Assert.Null(fixture.Display.FixedHz);
    }

    [Fact]
    public void TheStateNamesTheProfileBeneathAFixedRate()
    {
        using var fixture = new Fixture(28, 120);
        var before = fixture.Native.ArcProfileState;
        fixture.Display.WriteFixedRate(60, Admitted, out _);
        fixture.Session.BeginPass();

        Assert.Equal(IgclResult.Success, fixture.Display.ReadState(out var profile, out var beforeFixed));

        Assert.Equal(ArcSyncDisplay.ProfileCustom, profile.Profile);
        Assert.Equal(before.Profile, beforeFixed!.Value.Profile);
    }

    [Theory]
    [InlineData(GpuVrrSolution.LowFrameRate, true, 3u)]
    [InlineData(GpuVrrSolution.LowFrameRate, false, 4u)]
    [InlineData(GpuVrrSolution.HighFrameRate, true, 5u)]
    [InlineData(GpuVrrSolution.HighFrameRate, false, 6u)]
    public void TheEscapeOperationsAreClawLabs(GpuVrrSolution solution, bool enabled, uint operation)
    {
        Assert.Equal(operation, VrrSolutionEscape.Operation(solution, enabled));
    }

    [Fact]
    public void TheEscapePayloadCarriesTheDisplayVrrHeaderAndTheOperation()
    {
        var payload = VrrSolutionEscape.Request(4);

        Assert.Equal(124, payload.Length);
        Assert.Equal(1u, BitConverter.ToUInt32(payload, 8));
        Assert.Equal(10u, BitConverter.ToUInt32(payload, 12));
        Assert.Equal(4u, BitConverter.ToUInt32(payload, 16));
    }

    [Fact]
    public void TheAnswerReadsSupportEnabledHighThenLow()
    {
        var payload = new byte[124];
        payload[20] = 1;
        payload[21] = 1;
        payload[22] = 1;
        payload[23] = 0;

        Assert.Equal(new GpuVrrSolutions(true, true, false, true), VrrSolutionEscape.Parse(payload));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();

        public Fixture(float minimum, float maximum)
        {
            Native = new FakeIgclDriver { MinimumHz = minimum, MaximumHz = maximum };
            Session = Native.OpenSession();
            var store = FixedRefreshStore.Load(_directory.Root, DriverLog.None);
            var output = new IgclOutput(3, Assert.Single(Session.Adapters), 0, default, true);
            Display = ArcSyncDisplay.TryCreate(Session, output, DriverLog.None, "Fixture", store, Instance)!;
            Native.ArcWrites.Clear();
        }

        public FakeIgclDriver Native { get; }

        public IgclSession Session { get; }

        public ArcSyncDisplay Display { get; }

        public void Dispose()
        {
            Session.Dispose();
            _directory.Dispose();
        }
    }
}
