using LibGPUDriverInteract.Runtime;
using LibGPUDriverInteract.Testing;
using Xunit;

namespace LibGPUDriverInteract.Tests.Runtime;

public sealed class DriverRuntimeTests
{
    [Fact]
    public void DiscoveryReadsAllControlsBeforeOneProbePerSharedNativeStructure()
    {
        using var directory = new TemporaryDirectory();
        var order = new List<string>();
        var first = new DriverTestControl { SharedSupportKey = "shared" };
        var second = new DriverTestControl("second") { SharedSupportKey = "shared" };
        first.Reading = () =>
        {
            order.Add("first.read");
            return GpuValue.Boolean(false);
        };
        second.Reading = () =>
        {
            order.Add("second.read");
            return GpuValue.Boolean(false);
        };
        first.BeforeWrite = () => order.Add("first.write");
        var session = new DriverTestSession(first, second);
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture", (_, _) => session);
        try
        {
            Assert.Equal(GpuHealth.Ready, runtime.Start(sink, directory.Root, () => true, CancellationToken.None));
            Assert.Equal(new[] { "first.read", "second.read", "first.write" }, order.Take(3));
            Assert.Equal(1, first.Probes);
            Assert.Equal(0, second.Probes);
            Assert.Equal(2, sink.Descriptors!.Descriptors.Count);
            Assert.Equal(1, session.Discoveries);
            runtime.Observe(CancellationToken.None);
            Assert.Equal(1, session.Discoveries);
            Assert.Equal(1, sink.DescriptorPublications);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void FailedReadKeepsWritableControlsAndOnlyExplicitTopologyRetriesItsProbe()
    {
        using var directory = new TemporaryDirectory();
        var control = new DriverTestControl();
        control.Reading = () => throw new DriverFailure("Unreadable fixture setting.");
        var session = new DriverTestSession(control);
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture", (_, _) => session);
        try
        {
            runtime.Start(sink, directory.Root, () => true, CancellationToken.None);
            Assert.Single(sink.Descriptors!.Descriptors);
            Assert.True(sink.States.Last().Available);
            Assert.Equal(0, control.Probes);
            control.Reading = null;
            runtime.Observe(CancellationToken.None);
            Assert.Equal(0, control.Probes);
            runtime.RefreshTopology(CancellationToken.None);
            Assert.Equal(1, control.Probes);
            Assert.Equal(2, session.Discoveries);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void FailedSupportWriteIsOmittedAndNotRepeatedAfterStopStart()
    {
        using var directory = new TemporaryDirectory();
        var control = new DriverTestControl { RefuseProbe = true };
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture", (_, _) => new DriverTestSession(control));
        try
        {
            runtime.Start(sink, directory.Root, () => true, CancellationToken.None);
            Assert.Empty(sink.Descriptors!.Descriptors);
            runtime.Close();
            runtime.Start(sink, directory.Root, () => true, CancellationToken.None);
            Assert.Empty(sink.Descriptors!.Descriptors);
            Assert.Equal(1, control.Probes);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void CancelledPreparationAndClosedAdmissionRejectBeforeSetter()
    {
        using var directory = new TemporaryDirectory();
        using var cancelled = new CancellationTokenSource();
        var control = new DriverTestControl();
        var active = true;
        var runtime = new DriverRuntime("fixture", (_, _) => new DriverTestSession(control));
        try
        {
            runtime.Start(new RecordingDriverSink(), directory.Root, () => active, CancellationToken.None);
            var probes = control.Writes;
            control.BeforeWrite = cancelled.Cancel;
            Assert.Equal(GpuCommandOutcome.Rejected, runtime.Execute(Command(), cancelled.Token).Outcome);
            Assert.Equal(probes, control.Writes);
            active = false;
            Assert.Equal(GpuReasonCode.Quiescing, runtime.Execute(Command(), CancellationToken.None).Reason!.Code);
            Assert.Equal(probes, control.Writes);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void StickyWrittenValueSurvivesUnchangedReadbackUntilTheDriverReportsSomethingNew()
    {
        using var directory = new TemporaryDirectory();
        var native = false;
        var control = new DriverTestControl { IgnoreWrites = true, Reading = () => GpuValue.Boolean(native) };
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture", (_, _) => new DriverTestSession(control));
        try
        {
            runtime.Start(sink, directory.Root, () => true, CancellationToken.None);
            var applied = runtime.Execute(Command(), CancellationToken.None);
            Assert.Equal(GpuCommandOutcome.AppliedUnverified, applied.Outcome);
            Assert.Null(applied.Reason);
            var before = sink.States.Count;
            runtime.Observe(CancellationToken.None);
            Assert.Equal(before, sink.States.Count);
            Assert.True(sink.States.Last().ObservedValue!.BooleanValue);
            native = true;
            runtime.Observe(CancellationToken.None);
            Assert.Equal(GpuStateQuality.Verified, sink.States.Last().Quality);
            native = false;
            runtime.Observe(CancellationToken.None);
            Assert.False(sink.States.Last().ObservedValue!.BooleanValue);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void FailedReadsKeepWrittenStateAndAnUnknownBaselineIsLearnedBeforeMeaningfulChange()
    {
        using var directory = new TemporaryDirectory();
        var native = false;
        var unreadable = false;
        var control = new DriverTestControl { IgnoreWrites = true };
        control.Reading = () => unreadable ? throw new DriverFailure("No readback.") : GpuValue.Boolean(native);
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture", (_, _) => new DriverTestSession(control));
        try
        {
            runtime.Start(sink, directory.Root, () => true, CancellationToken.None);
            control.AfterWrite = () => unreadable = true;
            Assert.Equal(GpuCommandOutcome.AppliedUnverified,
                runtime.Execute(Command(), CancellationToken.None).Outcome);
            runtime.Observe(CancellationToken.None);
            Assert.True(sink.States.Last().ObservedValue!.BooleanValue);
            unreadable = false;
            runtime.Observe(CancellationToken.None);
            Assert.True(sink.States.Last().ObservedValue!.BooleanValue);
            native = true;
            runtime.Observe(CancellationToken.None);
            Assert.Equal(GpuStateQuality.Verified, sink.States.Last().Quality);
            unreadable = true;
            runtime.Observe(CancellationToken.None);
            Assert.True(sink.States.Last().ObservedValue!.BooleanValue);
            unreadable = false;
            native = false;
            runtime.Observe(CancellationToken.None);
            Assert.False(sink.States.Last().ObservedValue!.BooleanValue);
            Assert.Equal(2, control.Writes);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void CancelledTopologyRetainsNewHandlesButNeverRepublishesKnownFailedSetterProbes()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var failed = new DriverTestControl { RefuseProbe = true };
        var good = new DriverTestControl("good");
        var session = new DriverTestSession(failed, good);
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture", (_, _) => session);
        try
        {
            runtime.Start(sink, directory.Root, () => true, CancellationToken.None);
            var replacementFailed = new DriverTestControl();
            var replacementGood = new DriverTestControl("good");
            session.Model = new DriverModel(session.Model.Sections, [replacementFailed, replacementGood]);
            session.BeforeDiscover = cancellation.Cancel;
            Assert.Throws<OperationCanceledException>(() => runtime.RefreshTopology(cancellation.Token));
            Assert.Equal("good", Assert.Single(sink.Descriptors!.Descriptors).CapabilityId);
            Assert.Equal(1, failed.Probes);
            Assert.Equal(0, replacementFailed.Probes);
            var command = new GpuCommand
                { CapabilityId = "good", InstanceId = "driver", RequestedValue = GpuValue.Boolean(true) };
            Assert.Equal(GpuCommandOutcome.AppliedVerified, runtime.Execute(command, CancellationToken.None).Outcome);
            Assert.Equal(1, replacementGood.Writes);
            Assert.Equal(1, good.Writes);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void ExplicitTopologyReopensOnceAfterReadOnlyLossButPollingNeverRetriesAReplacementLoss()
    {
        using var directory = new TemporaryDirectory();
        var first = new DriverTestSession(new DriverTestControl());
        var replacement = new DriverTestSession(new DriverTestControl());
        var failedReplacement = new DriverTestSession(new DriverTestControl())
            { BeforeDiscover = () => throw new DriverFailure("Replacement also lost.", lost: true) };
        var opens = 0;
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture",
            (_, _) => ++opens switch { 1 => first, 2 => replacement, _ => failedReplacement });
        try
        {
            runtime.Start(sink, directory.Root, () => true, CancellationToken.None);
            first.BeforeDiscover = () => throw new DriverFailure("Old read-only session lost.", lost: true);
            runtime.RefreshTopology(CancellationToken.None);
            Assert.Equal(2, opens);
            Assert.Equal(1, first.Disposals);
            Assert.Single(sink.Descriptors!.Descriptors);
            replacement.BeforeDiscover = () => throw new DriverFailure("Replacement lost.", lost: true);
            runtime.RefreshTopology(CancellationToken.None);
            Assert.Equal(3, opens); // One replacement attempt in this second explicit signal.
            runtime.Observe(CancellationToken.None);
            runtime.Observe(CancellationToken.None);
            Assert.Equal(3, opens);
            Assert.Empty(sink.Descriptors!.Descriptors);
            Assert.Equal(1, failedReplacement.Disposals);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void TopologyLossAfterAdmissionClosesNeverOpensAnotherSession()
    {
        using var directory = new TemporaryDirectory();
        var active = true;
        var session = new DriverTestSession(new DriverTestControl());
        var opens = 0;
        var runtime = new DriverRuntime("fixture", (_, _) =>
        {
            opens++;
            return session;
        });
        try
        {
            runtime.Start(new RecordingDriverSink(), directory.Root, () => active, CancellationToken.None);
            session.BeforeDiscover = () =>
            {
                active = false;
                throw new DriverFailure("Lost during stop.", lost: true);
            };
            runtime.RefreshTopology(CancellationToken.None);
            Assert.Equal(1, opens);
            Assert.Equal(1, session.Disposals);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void LostSupportMutationDuringTopologyIsNeverReopenedOrReprobedAutomatically()
    {
        using var directory = new TemporaryDirectory();
        var control = new DriverTestControl { Reading = () => throw new DriverFailure("No support read yet.") };
        var opens = 0;
        var runtime = new DriverRuntime("fixture", (_, _) =>
        {
            opens++;
            return new DriverTestSession(control);
        });
        try
        {
            runtime.Start(new RecordingDriverSink(), directory.Root, () => true, CancellationToken.None);
            control.Reading = null;
            control.BeforeWrite = () => throw new DriverFailure("Dispatched probe lost its session.", true, true);
            runtime.RefreshTopology(CancellationToken.None);
            runtime.Observe(CancellationToken.None);
            Assert.Equal(1, opens);
            Assert.Equal(1, control.Probes);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void LostSessionDoesNotReopenOnObservationOrEmptySyncAndExplicitSyncReportsOpenFailure()
    {
        using var directory = new TemporaryDirectory();
        var control = new DriverTestControl();
        var session = new DriverTestSession(control);
        var opens = 0;
        var runtime = new DriverRuntime("fixture", (_, _) => ++opens == 1
            ? session
            : throw new DriverFailure("Driver remains unavailable."));
        try
        {
            runtime.Start(new RecordingDriverSink(), directory.Root, () => true, CancellationToken.None);
            control.Reading = () => throw new DriverFailure("Session lost.", lost: true);
            runtime.Observe(CancellationToken.None);
            runtime.Observe(CancellationToken.None);
            Assert.Equal(1, opens);
            var result = runtime.Sync(new GpuProfileSync(1, []), CancellationToken.None);
            Assert.NotEmpty(result.Failures);
            Assert.Equal(1, opens);
            var requested = new GpuProfileSync(2, [
                new GpuApplicationProfile("game", "Game", ["game.exe"],
                    [new GpuApplicationValue("enabled", "driver", GpuValue.Boolean(true))])
            ]);
            Assert.NotEmpty(runtime.Sync(requested, CancellationToken.None).Failures);
            Assert.Equal(2, opens);
            Assert.Equal(1, session.Disposals);
        }
        finally
        {
            runtime.Close();
        }
    }

    [Fact]
    public void DescriptorLabelsRetainSafeCharactersAndActionsRequireNull()
    {
        const string label = "Driver (R), Engine (TM), display name by Example?";
        var descriptor = DriverDescriptors.Toggle("enabled", "driver", label + "\u0001", "graphics",
            GpuProfileScope.GlobalOnly);
        Assert.Equal(label, descriptor.Label);
        var action = descriptor with { Role = GpuRole.Action, ValueKind = GpuValueKind.None, SupportsWrite = false };
        Assert.True(DriverDescriptors.Accepts(action, null));
        Assert.False(DriverDescriptors.Accepts(action, GpuValue.None()));
    }

    private static GpuCommand Command()
    {
        return new GpuCommand
        {
            CapabilityId = "enabled", InstanceId = "driver", RequestedValue = GpuValue.Boolean(true)
        };
    }
}
