using System.Reflection;
using LibGPUDriverInteract.Runtime;
using LibGPUDriverInteract.Testing;
using Xunit;

namespace LibGPUDriverInteract.Tests.Api;

public sealed class GpuDriverTests
{
    [Fact]
    public async Task CycleNotificationPrecedesDescriptorsAndResumeRejectsOldCommands()
    {
        using var directory = new TemporaryDirectory();
        var engine = new Engine();
        await using var driver = new GpuDriver(engine, directory.Root);
        var events = new List<string>();
        driver.CycleStarted += cycle => events.Add("cycle:" + cycle);
        driver.DescriptorsChanged += set => events.Add("descriptors:" + set.CycleGeneration);

        Assert.Equal(GpuHealth.Ready, await driver.StartAsync());
        var oldCommand = Command(driver);
        Assert.Equal(new[] { "cycle:1", "descriptors:1" }, events.Take(2));
        Assert.Equal(GpuCommandOutcome.AppliedVerified, (await driver.ExecuteAsync(oldCommand)).Outcome);
        await driver.SuspendAsync();
        await driver.ResumeAsync();

        Assert.Equal(2, driver.CycleGeneration);
        Assert.Equal(new[] { "cycle:2", "descriptors:2" }, events.TakeLast(2));
        var rejected = await driver.ExecuteAsync(oldCommand);
        Assert.Equal(GpuCommandOutcome.Rejected, rejected.Outcome);
        Assert.Equal(GpuReasonCode.GenerationChanged, rejected.Reason!.Code);
        Assert.Equal(1, engine.Commands);
        Assert.True(await driver.StopAsync());
    }

    [Fact]
    public async Task AdmissionClosesBeforeEngineRetirementAndBlocksNewWrites()
    {
        using var directory = new TemporaryDirectory();
        var engine = new Engine();
        await using var driver = new GpuDriver(engine, directory.Root);
        await driver.StartAsync();
        var command = Command(driver);
        var closed = false;
        driver.AdmissionClosed += _ => closed = true;
        engine.BeforeStop = () => Assert.True(closed);

        Assert.True(await driver.StopAsync());
        var rejected = await driver.ExecuteAsync(command);
        Assert.Equal(GpuReasonCode.Quiescing, rejected.Reason!.Code);
        Assert.Equal(0, engine.Commands);
        await driver.StartAsync();
        Assert.Equal(2, driver.CycleGeneration);
        Assert.True(await driver.StopAsync());
    }

    [Fact]
    public async Task CollectionsAreDetachedFromPublisherInputsAndConsumerMutation()
    {
        using var directory = new TemporaryDirectory();
        var engine = new Engine();
        await using var driver = new GpuDriver(engine, directory.Root);
        await driver.StartAsync();
        var choices = new List<GpuChoice> { new("on", "On") };
        var categories = new List<GpuCategory>
        {
            new() { CategoryId = "graphics", Title = "Graphics" }
        };
        var descriptors = new List<GpuDescriptor>
        {
            DriverDescriptors.Toggle("enabled", "gpu", "Enabled", "adapter", GpuProfileScope.GlobalOnly) with
            {
                Choices = choices
            }
        };
        var sections = new List<GpuSection>
        {
            new() { SectionId = "adapter", Title = "Adapter", Categories = categories }
        };
        await engine.Sink!.PublishDescriptorsAsync(new GpuDescriptorSet
        {
            Generation = 2, CycleGeneration = driver.CycleGeneration,
            Sections = sections, Descriptors = descriptors
        }, CancellationToken.None);

        choices.Clear();
        categories.Clear();
        descriptors.Clear();
        sections.Clear();
        var first = driver.DescriptorSet;
        var second = driver.DescriptorSet;
        Assert.Single(first.Sections);
        Assert.Single(first.Sections[0].Categories);
        Assert.Single(first.Descriptors[0].Choices);
        Assert.NotSame(first.Descriptors, second.Descriptors);
        Assert.Throws<NotSupportedException>(() => ((IList<GpuDescriptor>)first.Descriptors).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<GpuChoice>)first.Descriptors[0].Choices).Clear());
        Assert.True(await driver.StopAsync());
    }

    [Fact]
    public async Task StaleStatesAreDiscardedAndSubscriberFailureDoesNotChangeAcceptedWrite()
    {
        using var directory = new TemporaryDirectory();
        var logs = new List<GpuLogEntry>();
        var engine = new Engine();
        await using var driver = new GpuDriver(engine, directory.Root, logs.Add);
        driver.DescriptorsChanged += _ => throw new InvalidOperationException("consumer event failure");
        Assert.Equal(GpuHealth.Ready, await driver.StartAsync());
        Assert.Single(driver.DescriptorSet.Descriptors);

        await engine.Sink!.PublishCapabilityStateAsync(State(driver, 0), CancellationToken.None);
        Assert.Empty(driver.States);
        await engine.Sink.PublishCapabilityStateAsync(State(driver, 1), CancellationToken.None);
        Assert.Single(driver.States);
        Assert.Equal(GpuCommandOutcome.AppliedVerified, (await driver.ExecuteAsync(Command(driver))).Outcome);
        Assert.Contains(logs, entry => entry.Message == "consumer event failure");
        Assert.True(await driver.StopAsync());
    }

    [Fact]
    public async Task CancelledStopRetainsOwnedCallAndResumeWaitsForRetirement()
    {
        using var directory = new TemporaryDirectory();
        var engine = new Engine
        {
            RetirementRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var driver = new GpuDriver(engine, directory.Root);
        await driver.StartAsync();
        Assert.False(await driver.StopAsync(new CancellationToken(true)));
        await engine.RetirementEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(engine.Retired);
        var resume = driver.ResumeAsync().AsTask();
        Assert.False(resume.IsCompleted);
        Assert.Equal(1, driver.CycleGeneration);
        engine.RetirementRelease.SetResult();
        await resume.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(engine.Retired);
        Assert.Equal(2, driver.CycleGeneration);
        Assert.True(await driver.StopAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledStartupReleasesCallerAndKeepsRetirementAndRestartBehindNativeWork(bool resume)
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var engine = new Engine();
        await using var driver = new GpuDriver(engine, directory.Root);
        if (resume)
        {
            await driver.StartAsync();
            await driver.SuspendAsync();
        }

        engine.StartupRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StartupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.RetirementEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.IgnoreStartupCancellation = true;
        engine.BeforeStop = () => Assert.False(engine.StartInFlight);
        var admissionClosed = false;
        driver.AdmissionClosed += _ => admissionClosed = true;
        var startup = resume
            ? driver.ResumeAsync(cancellation.Token).AsTask()
            : driver.StartAsync(cancellation.Token).AsTask();
        try
        {
            await engine.StartupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(admissionClosed);
            Assert.True(engine.StartInFlight);
            Assert.False(engine.RetirementEntered.Task.IsCompleted);
            Assert.Equal(GpuReasonCode.Quiescing, (await driver.ExecuteAsync(Command(driver))).Reason!.Code);

            var starts = engine.Starts;
            var cycle = driver.CycleGeneration;
            var restart = driver.StartAsync().AsTask();
            Assert.False(restart.IsCompleted);
            Assert.Equal(starts, engine.Starts);
            Assert.Equal(cycle, driver.CycleGeneration);

            engine.StartupRelease.SetResult();
            Assert.Equal(GpuHealth.Ready, await restart.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(engine.RetirementEntered.Task.IsCompleted);
            Assert.Equal(starts + 1, engine.Starts);
            Assert.Equal(cycle + 1, driver.CycleGeneration);
            Assert.True(await driver.StopAsync());
        }
        finally
        {
            engine.StartupRelease.TrySetResult();
        }
    }

    [Fact]
    public async Task DisposeJoinsFinishedNativeRetirementAndDisposesEngineExactlyOnce()
    {
        using var directory = new TemporaryDirectory();
        using var releasePublication = new ManualResetEventSlim();
        var finalPublication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new Engine();
        await using var driver = new GpuDriver(engine, directory.Root);
        await driver.StartAsync();
        driver.StatusChanged += status =>
        {
            if (status.Detail != "The driver is stopped.")
            {
                return;
            }

            finalPublication.TrySetResult();
            releasePublication.Wait();
        };
        engine.BeforeDispose = () =>
        {
            // Observe the retained task boundary: native stop has finished but the facade may still publish/release.
            var retirement =
                (Task)typeof(GpuDriver).GetField("_retirement", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(driver)!;
            Assert.True(retirement.IsCompleted);
            Assert.False(engine.StartInFlight);
        };
        var stop = driver.StopAsync().AsTask();
        try
        {
            await finalPublication.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(engine.Retired);
            Assert.False(stop.IsCompleted);
            await driver.DisposeAsync();
            await driver.DisposeAsync();
            Assert.Equal(0, engine.Disposals);
        }
        finally
        {
            releasePublication.Set();
        }

        Assert.True(await stop.WaitAsync(TimeSpan.FromSeconds(5)));
        await engine.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, engine.Disposals);
        await driver.DisposeAsync();
        Assert.Equal(1, engine.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopInvalidatesQueuedStartupBeforeItsCycleAndLaterExplicitStartStillOpens(bool resume)
    {
        using var directory = new TemporaryDirectory();
        var engine = new Engine();
        var scheduled = new Queue<(Func<Task<GpuHealth>> Work, TaskCompletionSource<GpuHealth> Completion)>();
        await using var driver = new GpuDriver(engine, directory.Root, scheduleStartup: work =>
        {
            var completion = new TaskCompletionSource<GpuHealth>(TaskCreationOptions.RunContinuationsAsynchronously);
            scheduled.Enqueue((work, completion));
            return completion.Task;
        });
        if (resume)
        {
            var first = driver.StartAsync().AsTask();
            await RunNextStartupAsync();
            await first;
            await driver.SuspendAsync();
        }

        var starts = engine.Starts;
        var cycle = driver.CycleGeneration;
        var queued = resume ? driver.ResumeAsync().AsTask() : driver.StartAsync().AsTask();
        Assert.Single(scheduled);
        Assert.True(await driver.StopAsync());
        Assert.Equal(starts, engine.Starts);
        Assert.Equal(cycle, driver.CycleGeneration);

        // Run the accepted startup only after stop has already confirmed retirement.
        await RunNextStartupAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(starts, engine.Starts);
        Assert.Equal(cycle, driver.CycleGeneration);
        Assert.Equal(GpuHealth.Unavailable, driver.Status.Health);

        var restart = driver.StartAsync().AsTask();
        await RunNextStartupAsync();
        Assert.Equal(GpuHealth.Ready, await restart);
        Assert.Equal(starts + 1, engine.Starts);
        Assert.Equal(cycle + 1, driver.CycleGeneration);
        Assert.True(await driver.StopAsync());

        async Task RunNextStartupAsync()
        {
            var (work, completion) = scheduled.Dequeue();
            try
            {
                completion.SetResult(await work());
            }
            catch (OperationCanceledException cancellation)
            {
                completion.SetCanceled(cancellation.CancellationToken);
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        }
    }

    private static GpuCommand Command(GpuDriver driver)
    {
        return new GpuCommand
        {
            CommandId = Guid.NewGuid(), CapabilityId = "enabled", InstanceId = "gpu",
            ExpectedCycleGeneration = driver.CycleGeneration,
            ExpectedDescriptorGeneration = driver.DescriptorSet.Generation,
            RequestedValue = GpuValue.Boolean(true), Deadline = DriverDeadline.Never
        };
    }

    private static GpuState State(GpuDriver driver, long generation)
    {
        return new GpuState
        {
            CapabilityId = "enabled", InstanceId = "gpu", CycleGeneration = driver.CycleGeneration,
            DescriptorGeneration = generation, Available = true, ObservedValue = GpuValue.Boolean(false),
            Quality = GpuStateQuality.Observed
        };
    }

    private sealed class Engine : IDriverEngine
    {
        internal IDriverSink? Sink { get; private set; }
        internal int Commands { get; private set; }
        internal int Starts { get; private set; }
        internal bool StartInFlight { get; private set; }
        internal bool IgnoreStartupCancellation { get; set; }
        internal TaskCompletionSource? StartupRelease { get; set; }

        internal TaskCompletionSource StartupEntered { get; set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Action? BeforeStop { get; set; }
        internal Action? BeforeDispose { get; set; }
        internal bool Retired { get; private set; }
        internal int Disposals { get; private set; }

        internal TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource? RetirementRelease { get; init; }

        internal TaskCompletionSource RetirementEntered { get; set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<GpuHealth> StartAsync(IDriverSink sink, DriverContext context,
            CancellationToken cancellationToken)
        {
            Starts++;
            StartInFlight = true;
            StartupEntered.TrySetResult();
            try
            {
                if (StartupRelease is not null)
                {
                    // A dispatched native call may ignore cancellation until it returns.
                    await StartupRelease.Task;
                }

                Sink = sink;
                await sink.PublishDescriptorsAsync(new GpuDescriptorSet
                {
                    Generation = 1, CycleGeneration = sink.CycleGeneration,
                    Descriptors =
                        [DriverDescriptors.Toggle("enabled", "gpu", "Enabled", "adapter", GpuProfileScope.GlobalOnly)]
                }, IgnoreStartupCancellation ? CancellationToken.None : cancellationToken);
                sink.ReportHealth(GpuHealth.Ready, "Fixture controls available.");
                return GpuHealth.Ready;
            }
            finally
            {
                StartInFlight = false;
            }
        }

        public ValueTask SessionChangedAsync(DriverContext context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public async ValueTask SuspendAsync(DriverContext context, CancellationToken cancellationToken)
        {
            await StopAsync(context, cancellationToken);
        }

        public async ValueTask ResumeAsync(DriverContext context, CancellationToken cancellationToken)
        {
            await StartAsync(Sink!, context, cancellationToken);
        }

        public async ValueTask<bool> StopAsync(DriverContext context, CancellationToken cancellationToken)
        {
            BeforeStop?.Invoke();
            RetirementEntered.TrySetResult();
            if (RetirementRelease is not null)
            {
                await RetirementRelease.Task.WaitAsync(cancellationToken);
            }

            Retired = true;
            return true;
        }

        public ValueTask<GpuCommandResult> ExecuteCommandAsync(GpuCommand command, CancellationToken cancellationToken)
        {
            Commands++;
            return ValueTask.FromResult(CommandResults.Verified(command, command.RequestedValue!));
        }

        public ValueTask<GpuProfileSyncResult> SyncApplicationProfilesAsync(GpuProfileSync sync,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new GpuProfileSyncResult(0, 0, []));
        }

        public ValueTask DisposeAsync()
        {
            BeforeDispose?.Invoke();
            Disposals++;
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
