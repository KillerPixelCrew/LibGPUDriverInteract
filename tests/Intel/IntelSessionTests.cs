using System.Collections.Concurrent;
using LibGPUDriverInteract.Intel.Graphics;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Intel.Tests.Fakes;
using LibGPUDriverInteract.Runtime;
using LibGPUDriverInteract.Testing;
using Xunit;

namespace LibGPUDriverInteract.Intel.Tests;

public sealed class IntelSessionTests
{
    [Fact]
    public void DiscoverySkipsNonRowsAndKeepsTheReportedWindowedVrrControlWithoutWindowsDisplayCapture()
    {
        using var directory = new TemporaryDirectory();
        var native = new FakeIgclDriver
        {
            Features = new[]
                {
                    ThreeDFeatureCatalog.AppProfileDetails, ThreeDFeatureCatalog.GlobalOrPerApp,
                    ThreeDFeatureCatalog.FrameGenerationControl, ThreeDFeatureCatalog.VrrWindowedBlt
                }
                .Select(id => new Ctl3dFeatureDetails { FeatureType = id, ValueType = (int)IgclValueType.Enum })
                .ToArray()
        };
        var sink = new RecordingDriverSink();
        using var session = native.CreateSession(directory.Root, sink);
        var model = session.Discover();
        var vrr = Assert.Single(model.Controls, control => control.Descriptor.CapabilityId == "graphics.vrr-windowed");
        Assert.Equal(new[] { "auto", "on", "off" }, vrr.Descriptor.Choices.Select(choice => choice.Value));
        Assert.Equal(2, model.Controls.Count);
        Assert.Equal(1, native.Opens);
    }

    [Fact]
    public void SuppliedSessionOwnsItsNativeApiButBorrowsTheRegistryAndMemoryTransport()
    {
        using var directory = new TemporaryDirectory();
        var native = new FakeIgclDriver();
        var sink = new RecordingDriverSink();
        var session = native.CreateSession(directory.Root, sink);
        session.Dispose();
        session.Dispose();
        Assert.Equal(1, native.Closes);
        native.Registry.Set("retained", 42);
        Assert.Equal(42, native.Registry.GetValue("retained"));
    }

    [Fact]
    public void UnreadableApplicationJournalKeepsGlobalControlsUsableAndPreservesItsBytes()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Root, "application-profiles.v1.json");
        File.WriteAllText(path, "corrupt ownership record");
        var native = new FakeIgclDriver();
        var sink = new RecordingDriverSink();
        var runtime = Runtime(native);
        try
        {
            Assert.Equal(GpuHealth.Ready, runtime.Start(sink, directory.Root, () => true, CancellationToken.None));
            var command = Command(sink.Descriptors!);
            var sync = new GpuProfileSync(1, [
                new GpuApplicationProfile("game", "Game", ["game.exe"],
                    [new GpuApplicationValue(command.CapabilityId, command.InstanceId, command.RequestedValue!)])
            ]);
            Assert.Single(runtime.Sync(sync, CancellationToken.None).Failures);
            Assert.Equal(GpuCommandOutcome.AppliedVerified, runtime.Execute(command, CancellationToken.None).Outcome);
            Assert.Equal("corrupt ownership record", File.ReadAllText(path));
            Assert.Equal(0, native.Closes);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void ReportedNativeControlIsNotTestWrittenDuringStartupOrRestart()
    {
        using var directory = new TemporaryDirectory();
        var native = new FakeIgclDriver { WriteResult = 0x40000017 };
        var sink = new RecordingDriverSink();
        var runtime = Runtime(native);
        try
        {
            runtime.Start(sink, directory.Root, () => true, CancellationToken.None);
            Assert.Single(sink.Descriptors!.Descriptors);
            Assert.Equal(0, native.Writes);
            runtime.Close();
            runtime.Start(sink, directory.Root, () => true, CancellationToken.None);
            Assert.Single(sink.Descriptors!.Descriptors);
            Assert.Equal(0, native.Writes);
            Assert.Equal(2, native.Opens);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void UnreadableSupportValueKeepsTheReportedControlWithoutTopologyWrites()
    {
        using var directory = new TemporaryDirectory();
        var native = new FakeIgclDriver
        { ReadResult = read => read == 1 ? IgclResult.InvalidArgument : IgclResult.Success };
        var sink = new RecordingDriverSink();
        var runtime = Runtime(native);
        try
        {
            runtime.Start(sink, directory.Root, () => true, CancellationToken.None);
            var descriptor = Assert.Single(sink.Descriptors!.Descriptors);
            Assert.Equal("graphics.retro-scaling", descriptor.CapabilityId);
            Assert.Equal("pci-8086-4688-00-02-0", descriptor.InstanceId);
            Assert.Equal(0, native.Writes);
            runtime.Observe(CancellationToken.None);
            Assert.Equal(0, native.Writes);
            runtime.RefreshTopology(CancellationToken.None);
            Assert.Equal(0, native.Writes);
            Assert.Equal(1, native.Opens);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Theory]
    [InlineData(IgclResult.UnsupportedFeature)]
    [InlineData(IgclResult.NotImplemented)]
    [InlineData(IgclResult.PlatformNotSupported)]
    [InlineData(IgclResult.SetFbcNotSupported)]
    public void MissingReadbackDoesNotOverrideTheReportedWritableCapability(int result)
    {
        using var directory = new TemporaryDirectory();
        var native = new FakeIgclDriver { ReadResult = _ => result };
        var sink = new RecordingDriverSink();
        var runtime = Runtime(native);
        try
        {
            runtime.Start(sink, directory.Root, () => true, CancellationToken.None);
            Assert.Single(sink.Descriptors!.Descriptors);
            Assert.True(sink.States.Last().Available);
            Assert.Equal(0, native.Writes);
            Assert.Equal(GpuCommandOutcome.AppliedUnverified,
                runtime.Execute(Command(sink.Descriptors), CancellationToken.None).Outcome);
            Assert.Equal(1, native.Writes);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public async Task IntelNativeCommandAndReadbackLeaveTheCallerThreadThroughTheSharedOwner()
    {
        using var directory = new TemporaryDirectory();
        var native = new FakeIgclDriver();
        await using var driver = new GpuDriver(Runtime(native), directory.Root);
        GpuDescriptorSet? descriptors = null;
        driver.DescriptorsChanged += set => descriptors = set;
        await driver.StartAsync();
        native.ReadThreads.Clear();
        native.WriteThreads.Clear();
        var completion =
            new TaskCompletionSource<(int Caller, GpuCommandResult Result)>(TaskCreationOptions
                .RunContinuationsAsynchronously);
        var caller = new Thread(() =>
        {
            try
            {
                completion.SetResult((Environment.CurrentManagedThreadId,
                    driver.ExecuteAsync(Command(descriptors!)).AsTask().GetAwaiter().GetResult()));
            }
            catch (Exception failure)
            {
                completion.SetException(failure);
            }
        })
        { IsBackground = true };
        caller.Start();
        var (thread, result) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(GpuCommandOutcome.AppliedVerified, result.Outcome);
        Assert.NotEmpty(native.WriteThreads);
        Assert.NotEmpty(native.ReadThreads);
        Assert.All(native.WriteThreads.Concat(native.ReadThreads), actual => Assert.NotEqual(thread, actual));
    }

    [Fact]
    public async Task CancellationAfterNativeDispatchStillPublishesTheWrittenValue()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var native = new FakeIgclDriver { IgnoreWrites = true };
        await using var driver = new GpuDriver(Runtime(native), directory.Root);
        GpuDescriptorSet? descriptors = null;
        var states = new ConcurrentQueue<GpuState>();
        driver.DescriptorsChanged += set => descriptors = set;
        driver.StateChanged += states.Enqueue;
        await driver.StartAsync();
        native.BeforeWrite = cancellation.Cancel;
        var result = await driver.ExecuteAsync(Command(descriptors!), cancellation.Token);
        Assert.Equal(GpuCommandOutcome.AppliedUnverified, result.Outcome);
        Assert.Null(result.Reason);
        Assert.Equal(GpuValue.Choice("integer"), states.Last().ObservedValue);
    }

    [Fact]
    public async Task CallerCancelledStopDoesNotCloseTheIntelSessionUnderABlockedSetter()
    {
        using var directory = new TemporaryDirectory();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var native = new FakeIgclDriver();
        await using var driver = new GpuDriver(Runtime(native), directory.Root);
        GpuDescriptorSet? descriptors = null;
        driver.DescriptorsChanged += set => descriptors = set;
        await driver.StartAsync();
        native.BeforeWrite = () =>
        {
            entered.TrySetResult();
            release.Wait();
        };
        var write = driver.ExecuteAsync(Command(descriptors!)).AsTask();
        Task<GpuHealth>? restart = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await driver.StopAsync(new CancellationToken(true)));
            Assert.Equal(0, native.Closes);
            restart = driver.StartAsync().AsTask();
            Assert.False(restart.IsCompleted);
            Assert.Equal(1, native.Opens);
        }
        finally
        {
            release.Set();
        }

        await write.WaitAsync(TimeSpan.FromSeconds(5));
        await restart!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, native.Opens);
        Assert.Equal(1, native.Closes);
    }

    private static DriverRuntime Runtime(FakeIgclDriver native)
    {
        return new DriverRuntime("intel", native.CreateSession);
    }

    private static GpuCommand Command(GpuDescriptorSet set)
    {
        var descriptor = Assert.Single(set.Descriptors);
        return new GpuCommand
        {
            CapabilityId = descriptor.CapabilityId,
            InstanceId = descriptor.InstanceId,
            RequestedValue = GpuValue.Choice("integer")
        };
    }
}
