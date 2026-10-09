using LibGPUDriverInteract.Runtime;
using Xunit;

namespace LibGPUDriverInteract.Tests.Runtime;

/// <summary>
///     The deadline every lifecycle context and command carries. It must not expire because the
///     process was frozen: on 2026-09-22 and 2026-09-28 a suspend mid-flight at sleep came back with a
///     wall-clock deadline already spent and failed on a machine that had just woken.
/// </summary>
public sealed class DriverDeadlineTests
{
    [Fact]
    public void AFreezeCountsAsOneStepAtMost()
    {
        Assert.Equal(DriverActiveClock.MaximumStep, DriverActiveClock.CountedStep(TimeSpan.FromMinutes(61)));
        Assert.Equal(DriverActiveClock.MaximumStep, DriverActiveClock.CountedStep(TimeSpan.FromHours(9)));
    }

    [Fact]
    public void OrdinaryStepsCountInFull()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), DriverActiveClock.CountedStep(TimeSpan.FromMilliseconds(250)));
        Assert.Equal(DriverActiveClock.MaximumStep, DriverActiveClock.CountedStep(DriverActiveClock.MaximumStep));
    }

    [Fact]
    public void ABackwardStepCountsAsNothing()
    {
        Assert.Equal(TimeSpan.Zero, DriverActiveClock.CountedStep(TimeSpan.FromMilliseconds(-5)));
    }

    [Fact]
    public void RemainingNeverGoesNegativeAndTheEndsBehave()
    {
        Assert.Equal(TimeSpan.Zero, DriverDeadline.Expired.Remaining);
        Assert.True(DriverDeadline.Expired.HasExpired);
        Assert.False(DriverDeadline.Never.HasExpired);
        Assert.Equal(TimeSpan.MaxValue, DriverDeadline.Never.Remaining);
        Assert.True(DriverDeadline.After(TimeSpan.FromSeconds(-3)).HasExpired);
        Assert.InRange(DriverDeadline.After(TimeSpan.FromSeconds(30)).Remaining, TimeSpan.FromSeconds(29),
            TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void EarliestAndOrderingAgree()
    {
        var soon = DriverDeadline.After(TimeSpan.FromSeconds(1));
        var later = DriverDeadline.After(TimeSpan.FromSeconds(60));

        Assert.Equal(soon, soon.Earliest(later));
        Assert.Equal(soon, later.Earliest(soon));
        Assert.True(soon < later);
        Assert.True(DriverDeadline.Expired < soon && later < DriverDeadline.Never);
    }

    [Fact]
    public void AnExpiredDriverDeadlineHandsOutACancelledSource()
    {
        using var source = DriverDeadline.Expired.CreateCancellationSource();

        Assert.True(source.IsCancellationRequested);
    }

    [Fact]
    public async Task TheDriverActiveClockCancelsAShortDriverDeadlineOnTime()
    {
        using var source = DriverDeadline.After(TimeSpan.FromMilliseconds(40)).CreateCancellationSource();

        await Task.Delay(TimeSpan.FromSeconds(2), source.Token).ContinueWith(_ => { }, TaskScheduler.Default);

        Assert.True(source.IsCancellationRequested);
    }

    [Fact]
    public void ALinkedTokenStillCancels()
    {
        using var caller = new CancellationTokenSource();
        using var source = DriverDeadline.Never.CreateCancellationSource(caller.Token);

        caller.Cancel();

        Assert.True(source.IsCancellationRequested);
    }

    [Fact]
    public void AWallClockDriverDeadlineConvertsFromNow()
    {
        Assert.True(DriverDeadline.At(DateTimeOffset.UtcNow.AddSeconds(-1)).HasExpired);
        Assert.InRange(DriverDeadline.At(DateTimeOffset.UtcNow.AddSeconds(10)).Remaining, TimeSpan.FromSeconds(9),
            TimeSpan.FromSeconds(10));
    }
}
