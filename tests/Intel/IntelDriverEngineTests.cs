using System.Collections.Concurrent;
using LibGPUDriverInteract.Intel.Graphics;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Intel.Tests.Fakes;
using LibGPUDriverInteract.Runtime;
using LibGPUDriverInteract.Testing;
using Xunit;

namespace LibGPUDriverInteract.Intel.Tests;

public sealed class IntelDriverEngineTests
{
    [Fact]
    public async Task DiscoverySkipsNonRowsAndPublishesTheReportedWindowedVrrControl()
    {
        using var directory = new TemporaryDirectory();
        var driver = new FakeIgclDriver
        {
            Features = new[]
            {
                ThreeDFeatureCatalog.AppProfileDetails, ThreeDFeatureCatalog.GlobalOrPerApp,
                ThreeDFeatureCatalog.FrameGenerationControl, ThreeDFeatureCatalog.VrrWindowedBlt
            }.Select(id => new Ctl3dFeatureDetails { FeatureType = id, ValueType = (int)IgclValueType.Enum }).ToArray()
        };
        var host = new Host();
        await using var engine = driver.CreateEngine();
        var context = Context(directory.Root);
        await engine.StartAsync(host, context, CancellationToken.None);
        try
        {
            var vrr = Assert.Single(host.Last.Descriptors,
                descriptor => descriptor.CapabilityId == "graphics.vrr-windowed");
            Assert.Equal(new[] { "auto", "on", "off" }, vrr.Choices.Select(choice => choice.Value));
            Assert.Equal(2, host.Last.Descriptors.Count);
        }
        finally
        {
            await engine.StopAsync(context, CancellationToken.None);
        }
    }

    [Fact]
    public async Task UnreadablePerApplicationRecordDoesNotDisableGlobalControls()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Root, "application-profiles.v1.json");
        File.WriteAllText(path, "corrupt ownership record");
        var driver = new FakeIgclDriver();
        var host = new Host();
        await using var engine = driver.CreateEngine();
        var context = Context(directory.Root);
        Assert.Equal(GpuHealth.Ready, await engine.StartAsync(host, context, CancellationToken.None));
        try
        {
            await host.Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var command = Command(host);
            var sync = new GpuProfileSync(1, 1,
            [
                new GpuApplicationProfile("game", "Game", ["game.exe"],
                [
                    new GpuApplicationValue(command.CapabilityId, command.InstanceId, command.RequestedValue!)
                ])
            ]);
            Assert.Single((await engine.SyncApplicationProfilesAsync(sync, CancellationToken.None)).Failures);
            Assert.Equal(GpuCommandOutcome.AppliedVerified,
                (await engine.ExecuteCommandAsync(command, CancellationToken.None)).Outcome);
            Assert.Equal("corrupt ownership record", File.ReadAllText(path));
            Assert.Equal(1, driver.Opens);
            Assert.Equal(0, driver.Closes);
        }
        finally
        {
            await engine.StopAsync(context, CancellationToken.None);
        }
    }

    [Fact]
    public async Task FailedNativeSupportProbeIsNotRepeatedOnResume()
    {
        using var directory = new TemporaryDirectory();
        var driver = new FakeIgclDriver { WriteResult = 0x40000017 };
        var host = new Host();
        await using var engine = driver.CreateEngine();
        var context = Context(directory.Root);
        await engine.StartAsync(host, context, CancellationToken.None);
        try
        {
            Assert.Empty(host.Last.Descriptors);
            Assert.Equal(1, driver.Writes);
            await engine.SuspendAsync(context, CancellationToken.None);
            host.CycleGeneration = 2;
            await engine.ResumeAsync(context with { Generation = 2 }, CancellationToken.None);
            Assert.Empty(host.Last.Descriptors);
            Assert.Equal(1, driver.Writes);
            Assert.Equal(2, driver.Opens);
        }
        finally
        {
            await engine.StopAsync(context, CancellationToken.None);
        }
    }

    [Fact]
    public async Task FailedSupportReadRecoversWithoutCachingASetterFailure()
    {
        using var directory = new TemporaryDirectory();
        var driver = new FakeIgclDriver
            { ReadResult = read => read == 1 ? IgclResult.InvalidArgument : IgclResult.Success };
        var host = new Host();
        await using var engine = driver.CreateEngine();
        var context = Context(directory.Root);
        await engine.StartAsync(host, context, CancellationToken.None);
        try
        {
            await host.Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(host.Descriptors.First().Descriptors);
            var descriptor = Assert.Single(host.Last.Descriptors);
            Assert.Equal("graphics.retro-scaling", descriptor.CapabilityId);
            Assert.Equal("pci-8086-4688-00-02-0", descriptor.InstanceId);
            Assert.Equal("graphics", Assert.Single(host.Last.Sections).SectionId);
            Assert.Equal(1, driver.Writes);
            Assert.Equal(1, driver.Opens);
            Assert.Equal(0, driver.Closes);
        }
        finally
        {
            await engine.StopAsync(context, CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(IgclResult.UnsupportedFeature)]
    [InlineData(IgclResult.NotImplemented)]
    [InlineData(IgclResult.PlatformNotSupported)]
    [InlineData(IgclResult.SetFbcNotSupported)]
    public async Task ExplicitUnsupportedReadPublishesNoControlAndDoesNotProbe(int result)
    {
        using var directory = new TemporaryDirectory();
        var driver = new FakeIgclDriver { ReadResult = _ => result };
        var host = new Host();
        await using var engine = driver.CreateEngine();
        var context = Context(directory.Root);
        await engine.StartAsync(host, context, CancellationToken.None);
        Assert.Empty(host.Last.Descriptors);
        Assert.Equal(0, driver.Writes);
        Assert.Equal(0, driver.Closes);
        Assert.True(await engine.StopAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task RefusedDescriptorPublicationRetriesWithoutClosingTheSession()
    {
        using var directory = new TemporaryDirectory();
        var driver = new FakeIgclDriver();
        var host = new Host { RefuseDescriptors = 1 };
        await using var engine = driver.CreateEngine();
        var context = Context(directory.Root);
        await engine.StartAsync(host, context, CancellationToken.None);
        try
        {
            await host.Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, host.DescriptorAttempts);
            Assert.Single(host.Last.Descriptors);
            Assert.Equal(2, host.Last.Generation);
            Assert.Equal(1, driver.Opens);
            Assert.Equal(0, driver.Closes);
            Assert.Equal(1, driver.Writes);
            Assert.All(host.Health, health => Assert.Equal(GpuHealth.Ready, health.Health));
        }
        finally
        {
            await engine.StopAsync(context, CancellationToken.None);
        }
    }

    [Fact]
    public async Task CommandWriteAndReadbackLeaveTheCallerThread()
    {
        using var directory = new TemporaryDirectory();
        var driver = new FakeIgclDriver();
        var host = new Host();
        await using var engine = driver.CreateEngine();
        var context = Context(directory.Root);
        await engine.StartAsync(host, context, CancellationToken.None);
        try
        {
            await host.Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            driver.ReadThreads.Clear();
            driver.WriteThreads.Clear();
            var completion = new TaskCompletionSource<(int Caller, GpuCommandResult Result)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var caller = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
                try
                {
                    var result = engine.ExecuteCommandAsync(Command(host), CancellationToken.None).AsTask()
                        .GetAwaiter().GetResult();
                    completion.SetResult((Environment.CurrentManagedThreadId, result));
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
            }) { IsBackground = true };
            caller.Start();
            var (thread, outcome) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(GpuCommandOutcome.AppliedVerified, outcome.Outcome);
            Assert.NotEmpty(driver.WriteThreads);
            Assert.NotEmpty(driver.ReadThreads);
            Assert.All(driver.WriteThreads.Concat(driver.ReadThreads),
                nativeThread => Assert.NotEqual(thread, nativeThread));
        }
        finally
        {
            await engine.StopAsync(context, CancellationToken.None);
        }
    }

    [Fact]
    public async Task CancellationAfterAcceptedWriteStillPublishesTheWrittenValue()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var driver = new FakeIgclDriver { IgnoreWrites = true };
        var host = new Host();
        await using var engine = driver.CreateEngine();
        var context = Context(directory.Root);
        await engine.StartAsync(host, context, CancellationToken.None);
        try
        {
            await host.Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            driver.BeforeWrite = cancellation.Cancel;
            host.CheckPublicationToken = true;
            var result = await engine.ExecuteCommandAsync(Command(host), cancellation.Token);
            Assert.Equal(GpuCommandOutcome.AppliedUnverified, result.Outcome);
            Assert.Null(result.Reason);
            Assert.Equal(GpuValue.Choice("integer"), host.States.Last().ObservedValue);
            Assert.Equal(GpuStateQuality.Observed, host.States.Last().Quality);
        }
        finally
        {
            await engine.StopAsync(context, CancellationToken.None);
        }
    }

    [Fact]
    public async Task CancelledCommandBeforeAdmissionIsRejectedWithoutASetter()
    {
        using var directory = new TemporaryDirectory();
        var driver = new FakeIgclDriver();
        var host = new Host();
        await using var engine = driver.CreateEngine();
        var context = Context(directory.Root);
        await engine.StartAsync(host, context, CancellationToken.None);
        try
        {
            await host.Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var writes = driver.Writes;
            var result = await engine.ExecuteCommandAsync(Command(host), new CancellationToken(true));
            Assert.Equal(GpuCommandOutcome.Rejected, result.Outcome);
            Assert.Equal(GpuReasonCode.Quiescing, result.Reason!.Code);
            Assert.Equal(writes, driver.Writes);
        }
        finally
        {
            await engine.StopAsync(context, CancellationToken.None);
        }
    }

    [Fact]
    public async Task TimedOutSuspendAndResumeDoNotOpenUnderABlockedCall()
    {
        using var directory = new TemporaryDirectory();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FakeIgclDriver();
        var host = new Host();
        await using var engine = driver.CreateEngine();
        var context = Context(directory.Root);
        await engine.StartAsync(host, context, CancellationToken.None);
        await host.Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        driver.BeforeWrite = () =>
        {
            entered.TrySetResult();
            release.Wait();
        };
        var command = engine.ExecuteCommandAsync(Command(host), CancellationToken.None).AsTask();
        Task? resume = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                engine.SuspendAsync(context, new CancellationToken(true)).AsTask());
            host.CycleGeneration = 2;
            resume = engine.ResumeAsync(context with { Generation = 2 }, CancellationToken.None).AsTask();
            Assert.False(resume.IsCompleted);
            Assert.Equal(1, driver.Opens);
            Assert.Equal(0, driver.Closes);
        }
        finally
        {
            release.Set();
            await command.WaitAsync(TimeSpan.FromSeconds(5));
            if (resume is not null)
            {
                await resume.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await engine.StopAsync(context, CancellationToken.None);
        }

        Assert.Equal(2, driver.Opens);
        Assert.Equal(2, driver.Closes);
        Assert.Equal(2, driver.Writes);
        Assert.Equal(2, host.Last.CycleGeneration);
    }

    [Fact]
    public async Task StopReturnsWithinItsBudgetWhileCloseRetainsTheNativeLane()
    {
        using var directory = new TemporaryDirectory();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FakeIgclDriver();
        var host = new Host();
        await using var engine = driver.CreateEngine();
        var context = Context(directory.Root);
        await engine.StartAsync(host, context, CancellationToken.None);
        await host.Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        driver.BeforeClose = () =>
        {
            entered.TrySetResult();
            release.Wait();
        };
        Task<GpuHealth>? restart = null;
        try
        {
            Assert.False(await engine.StopAsync(context, new CancellationToken(true)));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            host.CycleGeneration = 2;
            restart = engine.StartAsync(host, context with { Generation = 2 }, CancellationToken.None).AsTask();
            Assert.False(restart.IsCompleted);
            Assert.Equal(1, driver.Opens);
            Assert.Equal(0, driver.Closes);
        }
        finally
        {
            release.Set();
            if (restart is not null)
            {
                await restart.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await engine.StopAsync(context, CancellationToken.None);
        }

        Assert.Equal(2, driver.Opens);
        Assert.Equal(2, driver.Closes);
    }

    [Fact]
    public async Task BoundedStopKeepsTheDriverOpenUntilTheBlockedSetterReturns()
    {
        using var directory = new TemporaryDirectory();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FakeIgclDriver();
        var host = new Host();
        await using var engine = driver.CreateEngine();
        var context = Context(directory.Root);
        await engine.StartAsync(host, context, CancellationToken.None);
        await host.Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        driver.BeforeWrite = () =>
        {
            entered.TrySetResult();
            release.Wait();
        };
        var command = engine.ExecuteCommandAsync(Command(host), CancellationToken.None).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await engine.StopAsync(context, new CancellationToken(true)));
            Assert.Equal(0, driver.Closes);
            Assert.Equal(1, driver.Opens);
        }
        finally
        {
            release.Set();
            await command.WaitAsync(TimeSpan.FromSeconds(5));
            await engine.StopAsync(context, CancellationToken.None);
        }

        Assert.Equal(1, driver.Closes);
    }

    private static DriverContext Context(string directory)
    {
        return new DriverContext(1, DriverDeadline.Never, directory);
    }

    private static GpuCommand Command(Host host)
    {
        var descriptor = Assert.Single(host.Last.Descriptors);
        return new GpuCommand
        {
            CommandId = Guid.NewGuid(),
            CapabilityId = descriptor.CapabilityId,
            InstanceId = descriptor.InstanceId,
            ExpectedCycleGeneration = host.CycleGeneration,
            ExpectedDescriptorGeneration = host.Last.Generation,
            Deadline = DriverDeadline.Never,
            RequestedValue = GpuValue.Choice("integer")
        };
    }

    private sealed class Host : IDriverSink
    {
        private int _descriptorAttempts;
        internal ConcurrentQueue<GpuDescriptorSet> Descriptors { get; } = new();
        internal ConcurrentQueue<GpuState> States { get; } = new();
        internal ConcurrentQueue<(GpuHealth Health, string Detail)> Health { get; } = new();
        internal TaskCompletionSource Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal GpuDescriptorSet Last => Descriptors.Last();
        internal int RefuseDescriptors { get; init; }
        internal int DescriptorAttempts => Volatile.Read(ref _descriptorAttempts);
        internal bool CheckPublicationToken { get; set; }
        public long CycleGeneration { get; set; } = 1;

        public ValueTask PublishDescriptorsAsync(GpuDescriptorSet descriptors, CancellationToken token)
        {
            if (Interlocked.Increment(ref _descriptorAttempts) <= RefuseDescriptors)
            {
                throw new InvalidOperationException("Fixture refused descriptors.");
            }

            Descriptors.Enqueue(descriptors);
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishCapabilityStateAsync(GpuState state, CancellationToken token)
        {
            if (CheckPublicationToken)
            {
                token.ThrowIfCancellationRequested();
            }

            States.Enqueue(state);
            Observed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public void ReportHealth(GpuHealth health, string detail)
        {
            Health.Enqueue((health, detail));
        }

        public void Log(GpuLogLevel level, string scope, string message)
        {
        }

        public void LogChange(GpuLogLevel level, string scope, string key, string message)
        {
        }
    }
}
