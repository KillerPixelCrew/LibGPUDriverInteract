using System.Collections.Concurrent;
using LibGPUDriverInteract.Runtime;
using LibGPUDriverInteract.Testing;
using Xunit;

namespace LibGPUDriverInteract.Tests.Api;

public sealed class GpuDriverTests
{
    [Fact]
    public async Task StartPublishesDescriptorsBeforeStateAndStableCommandsWorkAfterExplicitRestart()
    {
        using var directory = new TemporaryDirectory();
        var fixture = new Fixture();
        await using var driver = fixture.Create(directory.Root);
        var events = new ConcurrentQueue<string>();
        driver.DescriptorsChanged += _ => events.Enqueue("descriptors");
        driver.StateChanged += _ => events.Enqueue("state");
        Assert.Equal(GpuHealth.Ready, await driver.StartAsync());
        Assert.Equal(new[] { "descriptors", "state" }, events.Take(2));
        Assert.Equal(GpuCommandOutcome.AppliedVerified, (await driver.ExecuteAsync(Command())).Outcome);
        Assert.Equal(GpuHealth.Ready, await driver.StartAsync());
        Assert.Equal(1, fixture.Opens);
        Assert.True(await driver.StopAsync());
        Assert.Equal(GpuReasonCode.Quiescing, (await driver.ExecuteAsync(Command())).Reason!.Code);
        Assert.Equal(GpuHealth.Ready, await driver.StartAsync());
        Assert.Equal(GpuCommandOutcome.AppliedVerified, (await driver.ExecuteAsync(Command())).Outcome);
        Assert.Equal(2, fixture.Opens);
    }

    [Fact]
    public async Task DescriptorCollectionsAreDetachedFromPublisherInputsAndConsumerMutation()
    {
        using var directory = new TemporaryDirectory();
        var choices = new List<GpuChoice> { new("on", "On") };
        var categories = new List<GpuCategory> { new() { CategoryId = "graphics", Title = "Graphics" } };
        var sections = new List<GpuSection>
            { new() { SectionId = "graphics", Title = "Graphics", Categories = categories } };
        var control = new DescriptorControl(
            DriverDescriptors.Toggle("enabled", "driver", "Enabled", "graphics", GpuProfileScope.GlobalOnly)
                with
            {
                Choices = choices
            });
        var session = new DriverTestSession(control) { Model = new DriverModel(sections, [control]) };
        await using var driver = new GpuDriver(new DriverRuntime("fixture", (_, _) => session), directory.Root);
        GpuDescriptorSet? published = null;
        driver.DescriptorsChanged += set => published = set;
        await driver.StartAsync();
        choices.Clear();
        categories.Clear();
        sections.Clear();
        Assert.Single(published!.Sections);
        Assert.Single(published.Sections[0].Categories);
        Assert.Single(published.Descriptors[0].Choices);
        Assert.Throws<NotSupportedException>(() => ((IList<GpuDescriptor>)published.Descriptors).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<GpuChoice>)published.Descriptors[0].Choices).Clear());
    }

    [Fact]
    public async Task SubscriberFailureDoesNotChangeAcceptedWriteOrOtherSubscribers()
    {
        using var directory = new TemporaryDirectory();
        var fixture = new Fixture();
        var logs = new ConcurrentQueue<GpuLogEntry>();
        await using var driver = fixture.Create(directory.Root, logs.Enqueue);
        var states = new ConcurrentQueue<GpuState>();
        driver.DescriptorsChanged += _ => throw new InvalidOperationException("descriptor consumer failed");
        driver.StateChanged += _ => throw new InvalidOperationException("state consumer failed");
        driver.StateChanged += states.Enqueue;
        Assert.Equal(GpuHealth.Ready, await driver.StartAsync());
        Assert.Equal(GpuCommandOutcome.AppliedVerified, (await driver.ExecuteAsync(Command())).Outcome);
        Assert.True(states.Last().ObservedValue!.BooleanValue);
        Assert.Contains(logs, entry => entry.Message == "descriptor consumer failed");
        Assert.Contains(logs, entry => entry.Message == "state consumer failed");
    }

    [Fact]
    public async Task CancelledStartupWaitKeepsTheSharedNativeWorkOwnedAndDoesNotOpenTwice()
    {
        using var directory = new TemporaryDirectory();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = Signal();
        var fixture = new Fixture
        {
            BeforeDiscover = () =>
            {
                entered.TrySetResult();
                release.Wait();
            }
        };
        await using var driver = fixture.Create(directory.Root);
        var first = driver.StartAsync(cancellation.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            Assert.Equal(0, fixture.Disposals);
            var second = driver.StartAsync().AsTask();
            Assert.False(second.IsCompleted);
            Assert.Equal(1, fixture.Opens);
            release.Set();
            Assert.Equal(GpuHealth.Ready, await second.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, fixture.Opens);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task StopClosesAdmissionAndRestartWaitsForNativeRetirementAfterTheCallerCancels()
    {
        using var directory = new TemporaryDirectory();
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        var fixture = new Fixture();
        await using var driver = fixture.Create(directory.Root);
        await driver.StartAsync();
        fixture.Last!.BeforeDispose = () =>
        {
            entered.TrySetResult();
            release.Wait();
        };
        Task<GpuHealth>? restart = null;
        try
        {
            Assert.False(await driver.StopAsync(new CancellationToken(true)));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(GpuReasonCode.Quiescing, (await driver.ExecuteAsync(Command())).Reason!.Code);
            restart = driver.StartAsync().AsTask();
            Assert.False(restart.IsCompleted);
            Assert.Equal(1, fixture.Opens);
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(GpuHealth.Ready, await restart!.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, fixture.Opens);
        Assert.Equal(1, fixture.Disposals);
    }

    [Fact]
    public async Task QueuedCancelledCommandNeverDispatchesAnotherSetter()
    {
        using var directory = new TemporaryDirectory();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = Signal();
        var fixture = new Fixture();
        await using var driver = fixture.Create(directory.Root);
        await driver.StartAsync();
        fixture.Control.BeforeWrite = () =>
        {
            entered.TrySetResult();
            release.Wait();
        };
        var first = driver.ExecuteAsync(Command()).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queued = driver.ExecuteAsync(Command(), cancellation.Token).AsTask();
            cancellation.Cancel();
            Assert.Equal(GpuCommandOutcome.Rejected, (await queued.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
            Assert.Equal(0, fixture.Control.Writes);
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(GpuCommandOutcome.AppliedVerified, (await first.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.Equal(1, fixture.Control.Writes);
    }

    [Fact]
    public async Task CancellationAfterDispatchedWriteStillPublishesTheWrittenValue()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture();
        await using var driver = fixture.Create(directory.Root);
        var states = new ConcurrentQueue<GpuState>();
        driver.StateChanged += states.Enqueue;
        await driver.StartAsync();
        fixture.Control.IgnoreWrites = true;
        fixture.Control.AfterWrite = cancellation.Cancel;
        var result = await driver.ExecuteAsync(Command(), cancellation.Token);
        Assert.Equal(GpuCommandOutcome.AppliedUnverified, result.Outcome);
        Assert.True(states.Last().ObservedValue!.BooleanValue);
        Assert.Equal(GpuStateQuality.Observed, states.Last().Quality);
    }

    [Fact]
    public async Task DisposeWaitsForFinalPublicationAndNativeRetirementExactlyOnce()
    {
        using var directory = new TemporaryDirectory();
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        var fixture = new Fixture();
        var driver = fixture.Create(directory.Root);
        await driver.StartAsync();
        driver.StatusChanged += status =>
        {
            if (status.Detail == "The driver is stopped.")
            {
                entered.TrySetResult();
                release.Wait();
            }
        };
        var stop = driver.StopAsync().AsTask();
        Task? disposal = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disposal = driver.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            Assert.Equal(1, fixture.Disposals);
        }
        finally
        {
            release.Set();
        }

        Assert.True(await stop.WaitAsync(TimeSpan.FromSeconds(5)));
        await disposal!.WaitAsync(TimeSpan.FromSeconds(5));
        await driver.DisposeAsync();
        Assert.Equal(1, fixture.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => driver.StartAsync().AsTask());
    }

    [Fact]
    public async Task StopDuringBlockedSessionLoadingPreventsNativeInitializationWritesAndKeepsTheCallOwned()
    {
        using var directory = new TemporaryDirectory();
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        var loading = false;
        var cleanupWrites = 0;
        var session = new DriverTestSession
        {
            BeforeInitialize = (admission, _) =>
            {
                admission.Check();
                cleanupWrites++;
            },
            BeforeDispose = () => Assert.False(loading)
        };
        var runtime = new DriverRuntime("fixture", (_, _) =>
        {
            loading = true;
            entered.TrySetResult();
            release.Wait();
            loading = false;
            return session;
        });
        await using var driver = new GpuDriver(runtime, directory.Root);
        var startup = driver.StartAsync().AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await driver.StopAsync(new CancellationToken(true)));
            Assert.True(loading);
            Assert.Equal(0, session.Disposals);
            Assert.Equal(0, cleanupWrites);
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(GpuHealth.Unavailable, await startup.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await driver.StopAsync());
        Assert.Equal(0, session.Initializations);
        Assert.Equal(0, cleanupWrites);
        Assert.Equal(1, session.Disposals);
    }

    private static TaskCompletionSource Signal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static GpuCommand Command()
    {
        return new GpuCommand
        { CapabilityId = "enabled", InstanceId = "driver", RequestedValue = GpuValue.Boolean(true) };
    }

    private sealed class Fixture
    {
        internal DriverTestControl Control { get; } = new();
        internal List<DriverTestSession> Sessions { get; } = [];
        internal DriverTestSession? Last => Sessions.LastOrDefault();
        internal int Opens => Sessions.Count;
        internal int Disposals => Sessions.Sum(session => session.Disposals);
        internal Action? BeforeDiscover { get; init; }

        internal GpuDriver Create(string directory, Action<GpuLogEntry>? diagnostics = null)
        {
            return new GpuDriver(
                new DriverRuntime("fixture", (_, sink) =>
                {
                    var session = new DriverTestSession(Control) { Sink = sink, BeforeDiscover = BeforeDiscover };
                    Sessions.Add(session);
                    return session;
                }), directory, diagnostics);
        }
    }

    private sealed class DescriptorControl(GpuDescriptor descriptor) : DriverControl(descriptor)
    {
        internal override GpuValue Read()
        {
            return GpuValue.Boolean(false);
        }

        internal override void Write(GpuValue value, WriteAdmission admission)
        {
            admission.Check();
        }
    }
}
