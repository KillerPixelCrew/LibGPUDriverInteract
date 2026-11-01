using LibGPUDriverInteract.Intel.Display;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Intel.Tests.Fakes;
using LibGPUDriverInteract.Runtime;
using LibGPUDriverInteract.Testing;
using Xunit;

namespace LibGPUDriverInteract.Intel.Tests;

/// <summary>A refresh rate held through the Custom Arc Sync profile, and the profile it returns to.</summary>
/// <remarks>Blind: the fake driver stands in for IGCL. The panel's behaviour needs the attended hardware pass.</remarks>
public sealed class ArcSyncFixedRefreshTests
{
    private const string Instance = "internal-edid-test";
    private static readonly WriteAdmission Admitted = new(CancellationToken.None, static () => true);

    [Fact]
    public void AFixedRateIsTheCustomProfileWithBothBoundsAtTheRate()
    {
        using var fixture = new Fixture();

        Assert.Equal(IgclResult.Success, fixture.Display.WriteFixedRate(72, Admitted, out var applied));

        var written = Assert.Single(fixture.Native.ArcWrites);
        Assert.Equal(72f, applied);
        Assert.Equal((ArcSyncDisplay.ProfileCustom, 72f, 72f),
            (written.Profile, written.MinimumHz, written.MaximumHz));
        Assert.Equal(72f, fixture.Display.FixedHz);
    }

    [Fact]
    public void AFractionalRateIsWrittenAsItIs()
    {
        using var fixture = new Fixture();

        fixture.Display.WriteFixedRate(59.94f, Admitted, out var applied);

        var written = Assert.Single(fixture.Native.ArcWrites);
        Assert.Equal(59.94f, applied);
        Assert.Equal((59.94f, 59.94f), (written.MinimumHz, written.MaximumHz));
    }

    [Theory]
    [InlineData(20f, 48f)]
    [InlineData(144f, 120f)]
    public void ARateOutsideTheMonitorsRangeIsClampedIntoIt(float requested, float expected)
    {
        using var fixture = new Fixture(48, 120);

        fixture.Display.WriteFixedRate(requested, Admitted, out var applied);

        Assert.Equal(expected, applied);
        Assert.Equal(expected, Assert.Single(fixture.Native.ArcWrites).MaximumHz);
    }

    [Fact]
    public void RestoreWritesBackTheProfileInUseBeforeTheFirstFixedRate()
    {
        using var fixture = new Fixture();
        var before = fixture.Native.ArcProfileState;
        fixture.Display.WriteFixedRate(60, Admitted, out _);
        fixture.Display.WriteFixedRate(90, Admitted, out _);

        Assert.Equal(IgclResult.Success, fixture.Display.RestoreRate(Admitted, out var wrote));

        Assert.True(wrote);
        Assert.Equal((before.Profile, before.MinimumHz, before.MaximumHz),
            (fixture.Native.ArcProfileState.Profile, fixture.Native.ArcProfileState.MinimumHz,
                fixture.Native.ArcProfileState.MaximumHz));
        Assert.Null(fixture.Display.FixedHz);
    }

    [Fact]
    public void RestoreWithNothingFixedWritesNothing()
    {
        using var fixture = new Fixture();

        Assert.Equal(IgclResult.Success, fixture.Display.RestoreRate(Admitted, out var wrote));

        Assert.False(wrote);
        Assert.Empty(fixture.Native.ArcWrites);
    }

    [Fact]
    public void AProfileSomebodyElseChangedSinceIsLeftAlone()
    {
        using var fixture = new Fixture();
        fixture.Display.WriteFixedRate(60, Admitted, out _);
        var external = FakeIgclDriver.RecommendedProfile();
        external.Profile = ArcSyncDisplay.ProfileExcellent;
        fixture.Native.ArcProfileState = external;
        fixture.Session.BeginPass();

        fixture.Display.RestoreRate(Admitted, out var wrote);

        Assert.False(wrote);
        Assert.Equal(ArcSyncDisplay.ProfileExcellent, fixture.Native.ArcProfileState.Profile);
        Assert.Null(fixture.Display.FixedHz);
    }

    [Fact]
    public void AnArcSyncRowWriteHandsTheProfileBackToTheUser()
    {
        using var fixture = new Fixture();
        fixture.Display.WriteFixedRate(60, Admitted, out _);

        fixture.Display.WriteProfile(ArcSyncDisplay.ProfileGood, Admitted);

        Assert.Null(fixture.Display.FixedHz);
        fixture.Display.RestoreRate(Admitted, out var wrote);
        Assert.False(wrote);
    }

    [Fact]
    public void TheFixedProfileIsNotRememberedAsTheOneVariableRefreshReturnsTo()
    {
        using var fixture = new Fixture();
        var before = FakeIgclDriver.RecommendedProfile();
        before.Profile = ArcSyncDisplay.ProfileExcellent;
        fixture.Native.ArcProfileState = before;
        fixture.Session.BeginPass();
        fixture.Display.WriteFixedRate(60, Admitted, out _);
        fixture.Session.BeginPass();
        fixture.Display.ReadProfile(out _);

        // Off and on again through the variable-refresh row: on must not mean the fixed 60 Hz.
        fixture.Display.WriteEnabled(false, Admitted);
        fixture.Display.WriteEnabled(true, Admitted);

        Assert.Equal(ArcSyncDisplay.ProfileExcellent, fixture.Native.ArcProfileState.Profile);
    }

    [Fact]
    public void AnUnreadableProfileFixesNothingBecauseNothingCouldBePutBack()
    {
        using var fixture = new Fixture();
        fixture.Native.ArcReadResult = unchecked((int)0x40000001);
        fixture.Session.BeginPass();

        Assert.NotEqual(IgclResult.Success, fixture.Display.WriteFixedRate(60, Admitted, out _));

        Assert.Empty(fixture.Native.ArcWrites);
        Assert.Null(fixture.Display.FixedHz);
    }

    [Fact]
    public void ARefusedFirstWriteLeavesNoRecord()
    {
        using var fixture = new Fixture();
        fixture.Native.ArcWriteResult = unchecked((int)0x40000001);

        fixture.Display.WriteFixedRate(60, Admitted, out _);

        Assert.Null(fixture.Display.FixedHz);
        Assert.False(File.Exists(fixture.RecordPath));
    }

    [Fact]
    public void ARateAPreviousProcessLeftFixedIsRestoredOnceInTheNextSession()
    {
        using var directory = new TemporaryDirectory();
        var native = new FakeIgclDriver();
        var before = native.ArcProfileState;
        using (var first = new Fixture(directory.Root, native))
        {
            first.Display.WriteFixedRate(72, Admitted, out _);
        }

        Assert.True(File.Exists(Path.Combine(directory.Root, "arc-sync-fixed-refresh.v1.json")));
        using var second = new Fixture(directory.Root, native);

        second.Display.RecoverStale(Admitted);
        second.Display.RecoverStale(Admitted);

        Assert.Single(native.ArcWrites);
        Assert.Equal(before.Profile, native.ArcProfileState.Profile);
        Assert.False(File.Exists(Path.Combine(directory.Root, "arc-sync-fixed-refresh.v1.json")));
    }

    [Fact]
    public void ASuppliedBaselineIsWhatAReleasedRateReturnsTo()
    {
        using var fixture = new Fixture();
        var baseline = FakeIgclDriver.RecommendedProfile();
        baseline.Profile = ArcSyncDisplay.ProfileExcellent;
        baseline.MinimumHz = 28;
        fixture.Store.SetBaseline(Instance, baseline);
        fixture.Display.WriteFixedRate(60, Admitted, out _);

        fixture.Display.RestoreRate(Admitted, out _);

        Assert.Equal((ArcSyncDisplay.ProfileExcellent, 28f),
            (fixture.Native.ArcProfileState.Profile, fixture.Native.ArcProfileState.MinimumHz));
    }

    [Fact]
    public void APublicBaselineIsWhatTheNextReleaseWrites()
    {
        // The variable refresh range fix keeps Excellent with a 28 Hz floor beneath any fixed rate.
        using var fixture = new Fixture(28, 120);
        fixture.Display.SetBaseline(ArcSyncDisplay.ToNative(new GpuArcSyncBaseline(GpuArcSyncProfile.Excellent, 28,
            120)));
        fixture.Display.WriteFixedRate(60, Admitted, out _);

        fixture.Display.RestoreRate(Admitted, out _);

        Assert.Equal((ArcSyncDisplay.ProfileExcellent, 28f, 120f),
            (fixture.Native.ArcProfileState.Profile, fixture.Native.ArcProfileState.MinimumHz,
                fixture.Native.ArcProfileState.MaximumHz));
    }

    [Fact]
    public void ClearingTheBaselineReturnsToTheCapturedProfile()
    {
        using var fixture = new Fixture();
        var before = fixture.Native.ArcProfileState.Profile;
        fixture.Display.SetBaseline(ArcSyncDisplay.ToNative(new GpuArcSyncBaseline(GpuArcSyncProfile.Excellent, 30,
            120)));
        fixture.Display.SetBaseline(null);
        fixture.Display.WriteFixedRate(60, Admitted, out _);

        fixture.Display.RestoreRate(Admitted, out _);

        Assert.Equal(before, fixture.Native.ArcProfileState.Profile);
    }

    [Theory]
    [InlineData(GpuArcSyncProfile.Recommended, ArcSyncDisplay.ProfileRecommended)]
    [InlineData(GpuArcSyncProfile.Excellent, ArcSyncDisplay.ProfileExcellent)]
    [InlineData(GpuArcSyncProfile.Good, ArcSyncDisplay.ProfileGood)]
    [InlineData(GpuArcSyncProfile.Compatible, ArcSyncDisplay.ProfileCompatible)]
    [InlineData(GpuArcSyncProfile.Off, ArcSyncDisplay.ProfileOff)]
    [InlineData(GpuArcSyncProfile.Vesa, ArcSyncDisplay.ProfileVesa)]
    [InlineData(GpuArcSyncProfile.Custom, ArcSyncDisplay.ProfileCustom)]
    public void ThePublicProfilesAreTheDriversOwnValues(GpuArcSyncProfile profile, int native)
    {
        Assert.Equal(native, ArcSyncDisplay.ToNative(new GpuArcSyncBaseline(profile, 30, 120)).Profile);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory? _directory;

        public Fixture(float minimum = 30, float maximum = 120)
            : this(null, new FakeIgclDriver { MinimumHz = minimum, MaximumHz = maximum })
        {
        }

        public Fixture(string? root, FakeIgclDriver native)
        {
            if (root is null)
            {
                _directory = new TemporaryDirectory();
                root = _directory.Root;
            }

            RecordPath = Path.Combine(root, "arc-sync-fixed-refresh.v1.json");
            Native = native;
            Session = native.OpenSession();
            Store = FixedRefreshStore.Load(root, DriverLog.None);
            var output = new IgclOutput(3, Assert.Single(Session.Adapters), 0, default, true);
            Display = ArcSyncDisplay.TryCreate(Session, output, DriverLog.None, "Fixture", Store, Instance)!;
            native.ArcWrites.Clear();
        }

        public FakeIgclDriver Native { get; }

        public IgclSession Session { get; }

        public FixedRefreshStore Store { get; }

        public ArcSyncDisplay Display { get; }

        public string RecordPath { get; }

        public void Dispose()
        {
            Session.Dispose();
            _directory?.Dispose();
        }
    }
}
