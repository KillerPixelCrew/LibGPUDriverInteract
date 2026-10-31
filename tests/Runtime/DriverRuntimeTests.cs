using LibGPUDriverInteract.Runtime;
using LibGPUDriverInteract.Testing;
using Xunit;

namespace LibGPUDriverInteract.Tests.Runtime;

public sealed class DriverRuntimeTests
{
    /// <summary>The fixture sessions never touch the state directory.</summary>
    private static readonly string StateDirectory = Path.GetTempPath();

    [Fact]
    public void TopologyPublishesNewPhysicalRoutingMetadataEvenWhenLabelsAndControlsAreUnchanged()
    {
        var control = new DriverTestControl();
        var section = DriverDescriptors.Section("graphics", "Same monitor label", true) with
        {
            DisplayTarget = new GpuDisplayTarget { DevicePath = "monitor-one", TargetId = 7 }
        };
        var session = new DriverTestSession(control) { Model = new DriverModel([section], [control]) };
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture", (_, _) => session);
        runtime.Start(sink, StateDirectory, () => true, CancellationToken.None);
        session.Model = new DriverModel([section with
        {
            DisplayTarget = section.DisplayTarget! with { TargetId = 8 }
        }], [control]);
        runtime.RefreshTopology(CancellationToken.None);
        Assert.Equal(2, sink.DescriptorPublications);
        Assert.Equal(8u, sink.Descriptors!.Sections[0].DisplayTarget!.TargetId);
        Assert.Equal(0, control.Writes);
    }

    [Fact]
    public void StartupObservationAndTopologyNeverDispatchSupportWrites()
    {
        var order = new List<string>();
        var first = new DriverTestControl();
        var second = new DriverTestControl("second");
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
        Assert.Equal(GpuHealth.Ready, runtime.Start(sink, StateDirectory, () => true, CancellationToken.None));
        Assert.Equal(new[] { "first.read", "second.read" }, order);
        Assert.Equal(0, first.Writes);
        Assert.Equal(0, second.Writes);
        Assert.Equal(2, sink.Descriptors!.Descriptors.Count);
        Assert.Equal(1, session.Discoveries);
        runtime.Observe(CancellationToken.None);
        Assert.Equal(1, session.Discoveries);
        Assert.Equal(1, sink.DescriptorPublications);
        runtime.RefreshTopology(CancellationToken.None);
        Assert.Equal(0, first.Writes);
        Assert.Equal(0, second.Writes);
    }

    [Fact]
    public void StartWhileInactiveReportsUnavailableAfterAReadyStart()
    {
        var session = new DriverTestSession(new DriverTestControl());
        var runtime = new DriverRuntime("fixture", (_, _) => session);
        Assert.Equal(GpuHealth.Ready,
            runtime.Start(new RecordingDriverSink(), StateDirectory, () => true, CancellationToken.None));
        runtime.Close();
        Assert.Equal(GpuHealth.Unavailable,
            runtime.Start(new RecordingDriverSink(), StateDirectory, () => false, CancellationToken.None));
    }

    [Fact]
    public void FailedReadKeepsWritableControlsWithoutWritingDuringTopology()
    {
        var control = new DriverTestControl();
        control.Reading = () => throw new DriverFailure("Unreadable fixture setting.");
        var session = new DriverTestSession(control);
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture", (_, _) => session);
        runtime.Start(sink, StateDirectory, () => true, CancellationToken.None);
        Assert.Single(sink.Descriptors!.Descriptors);
        Assert.True(sink.States.Last().Available);
        Assert.Equal(0, control.Writes);
        control.Reading = null;
        runtime.Observe(CancellationToken.None);
        Assert.Equal(0, control.Writes);
        runtime.RefreshTopology(CancellationToken.None);
        Assert.Equal(0, control.Writes);
        Assert.Equal(2, session.Discoveries);
    }

    [Fact]
    public void ASetterRefusalIsReportedOnlyForAnExplicitCommandAndDoesNotHideDiscoveredSupport()
    {
        var control = new DriverTestControl { RefuseWrites = true };
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture", (_, _) => new DriverTestSession(control));
        runtime.Start(sink, StateDirectory, () => true, CancellationToken.None);
        Assert.Single(sink.Descriptors!.Descriptors);
        Assert.Equal(GpuCommandOutcome.Rejected,
            runtime.Execute(DriverTestControl.EnableCommand(), CancellationToken.None).Outcome);
        runtime.Close();
        runtime.Start(sink, StateDirectory, () => true, CancellationToken.None);
        Assert.Single(sink.Descriptors!.Descriptors);
        Assert.Equal(0, control.Writes);
    }

    [Fact]
    public void CancelledPreparationAndClosedAdmissionRejectBeforeSetter()
    {
        using var cancelled = new CancellationTokenSource();
        var control = new DriverTestControl();
        var active = true;
        var runtime = new DriverRuntime("fixture", (_, _) => new DriverTestSession(control));
        runtime.Start(new RecordingDriverSink(), StateDirectory, () => active, CancellationToken.None);
        var probes = control.Writes;
        control.BeforeWrite = cancelled.Cancel;
        Assert.Equal(GpuCommandOutcome.Rejected,
            runtime.Execute(DriverTestControl.EnableCommand(), cancelled.Token).Outcome);
        Assert.Equal(probes, control.Writes);
        active = false;
        Assert.Equal(GpuReasonCode.Quiescing,
            runtime.Execute(DriverTestControl.EnableCommand(), CancellationToken.None).Reason!.Code);
        Assert.Equal(probes, control.Writes);
    }

    [Fact]
    public void StickyWrittenValueSurvivesUnchangedReadbackUntilTheDriverReportsSomethingNew()
    {
        var native = false;
        var control = new DriverTestControl { IgnoreWrites = true, Reading = () => GpuValue.Boolean(native) };
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture", (_, _) => new DriverTestSession(control));
        runtime.Start(sink, StateDirectory, () => true, CancellationToken.None);
        var applied = runtime.Execute(DriverTestControl.EnableCommand(), CancellationToken.None);
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

    [Fact]
    public void FailedReadsKeepWrittenStateAndAnUnknownBaselineIsLearnedBeforeMeaningfulChange()
    {
        var native = false;
        var unreadable = false;
        var control = new DriverTestControl { IgnoreWrites = true };
        control.Reading = () => unreadable ? throw new DriverFailure("No readback.") : GpuValue.Boolean(native);
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture", (_, _) => new DriverTestSession(control));
        runtime.Start(sink, StateDirectory, () => true, CancellationToken.None);
        control.AfterWrite = () => unreadable = true;
        Assert.Equal(GpuCommandOutcome.AppliedUnverified,
            runtime.Execute(DriverTestControl.EnableCommand(), CancellationToken.None).Outcome);
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
        Assert.Equal(1, control.Writes);
    }

    [Fact]
    public void CancelledTopologyRetainsNewHandlesWithoutDispatchingSetters()
    {
        using var cancellation = new CancellationTokenSource();
        var failed = new DriverTestControl { RefuseWrites = true };
        var good = new DriverTestControl("good");
        var session = new DriverTestSession(failed, good);
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture", (_, _) => session);
        runtime.Start(sink, StateDirectory, () => true, CancellationToken.None);
        var replacementFailed = new DriverTestControl();
        var replacementGood = new DriverTestControl("good");
        session.Model = new DriverModel(session.Model.Sections, [replacementFailed, replacementGood]);
        session.BeforeDiscover = cancellation.Cancel;
        Assert.Throws<OperationCanceledException>(() => runtime.RefreshTopology(cancellation.Token));
        Assert.Equal(2, sink.Descriptors!.Descriptors.Count);
        Assert.Equal(0, failed.Writes);
        Assert.Equal(0, replacementFailed.Writes);
        var command = new GpuCommand
        { CapabilityId = "good", InstanceId = "driver", RequestedValue = GpuValue.Boolean(true) };
        Assert.Equal(GpuCommandOutcome.AppliedVerified, runtime.Execute(command, CancellationToken.None).Outcome);
        Assert.Equal(1, replacementGood.Writes);
        Assert.Equal(0, good.Writes);
    }

    [Fact]
    public void ExplicitTopologyReopensOnceAfterReadOnlyLossButPollingNeverRetriesAReplacementLoss()
    {
        var first = new DriverTestSession(new DriverTestControl());
        var replacement = new DriverTestSession(new DriverTestControl());
        var failedReplacement = new DriverTestSession(new DriverTestControl())
        { BeforeDiscover = () => throw new DriverFailure("Replacement also lost.", lost: true) };
        var opens = 0;
        var sink = new RecordingDriverSink();
        var runtime = new DriverRuntime("fixture",
            (_, _) => ++opens switch { 1 => first, 2 => replacement, _ => failedReplacement });
        runtime.Start(sink, StateDirectory, () => true, CancellationToken.None);
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

    [Fact]
    public void TopologyLossAfterAdmissionClosesNeverOpensAnotherSession()
    {
        var active = true;
        var session = new DriverTestSession(new DriverTestControl());
        var opens = 0;
        var runtime = new DriverRuntime("fixture", (_, _) =>
        {
            opens++;
            return session;
        });
        runtime.Start(new RecordingDriverSink(), StateDirectory, () => active, CancellationToken.None);
        session.BeforeDiscover = () =>
        {
            active = false;
            throw new DriverFailure("Lost during stop.", lost: true);
        };
        runtime.RefreshTopology(CancellationToken.None);
        Assert.Equal(1, opens);
        Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public void TopologyNeverInvokesASetterEvenWhenItsCurrentValueBecomesReadable()
    {
        var control = new DriverTestControl { Reading = () => throw new DriverFailure("No support read yet.") };
        var opens = 0;
        var runtime = new DriverRuntime("fixture", (_, _) =>
        {
            opens++;
            return new DriverTestSession(control);
        });
        runtime.Start(new RecordingDriverSink(), StateDirectory, () => true, CancellationToken.None);
        control.Reading = null;
        control.BeforeWrite = () => throw new DriverFailure("Dispatched probe lost its session.", true, true);
        runtime.RefreshTopology(CancellationToken.None);
        runtime.Observe(CancellationToken.None);
        Assert.Equal(1, opens);
        Assert.Equal(0, control.Writes);
    }

    [Fact]
    public void LostSessionDoesNotReopenOnObservationOrEmptySyncAndExplicitSyncReportsOpenFailure()
    {
        var control = new DriverTestControl();
        var session = new DriverTestSession(control);
        var opens = 0;
        var runtime = new DriverRuntime("fixture", (_, _) => ++opens == 1
            ? session
            : throw new DriverFailure("Driver remains unavailable."));
        runtime.Start(new RecordingDriverSink(), StateDirectory, () => true, CancellationToken.None);
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

    [Fact]
    public void ASetterReportingFailureIsVerifiedWhenTheDriverSettlesOnTheRequestedValue()
    {
        var control = new DriverTestControl { FailAfterWrite = "Fixture SetBoolean returned FAIL (3).", SettleReads = 2 };
        var sink = new RecordingDriverSink();
        var pauses = new List<TimeSpan>();
        var runtime = new DriverRuntime("fixture", (_, _) => new DriverTestSession(control))
        {
            Pause = (delay, _) => pauses.Add(delay)
        };
        runtime.Start(sink, StateDirectory, () => true, CancellationToken.None);
        var result = runtime.Execute(DriverTestControl.EnableCommand(), CancellationToken.None);
        Assert.Equal(GpuCommandOutcome.AppliedVerified, result.Outcome);
        Assert.True(result.ReadbackValue!.BooleanValue);
        Assert.Equal(1, control.Writes);
        Assert.Equal(DriverRuntime.SettleDelays.Take(2), pauses);
        Assert.Equal(GpuStateQuality.Verified, sink.States.Last().Quality);
        Assert.True(sink.States.Last().ObservedValue!.BooleanValue);
        Assert.Contains(sink.Logs, log => log.Contains("returned FAIL (3)") && log.Contains("readback"));
    }

    [Fact]
    public void ASetterReportingFailureStaysIndeterminateAndPublishesTheDriverValueWhenNothingSettles()
    {
        var control = new DriverTestControl { FailAfterWrite = "Fixture SetBoolean returned FAIL (3).", IgnoreWrites = true };
        var sink = new RecordingDriverSink();
        var pauses = new List<TimeSpan>();
        var runtime = new DriverRuntime("fixture", (_, _) => new DriverTestSession(control))
        {
            Pause = (delay, _) => pauses.Add(delay)
        };
        runtime.Start(sink, StateDirectory, () => true, CancellationToken.None);
        var result = runtime.Execute(DriverTestControl.EnableCommand(), CancellationToken.None);
        Assert.Equal(GpuCommandOutcome.Indeterminate, result.Outcome);
        Assert.Equal("Fixture SetBoolean returned FAIL (3). The driver still reports off.",
            result.Reason!.Detail);
        Assert.Equal(1, control.Writes);
        Assert.Equal(DriverRuntime.SettleDelays, pauses);
        Assert.Equal(GpuStateQuality.Observed, sink.States.Last().Quality);
        Assert.False(sink.States.Last().ObservedValue!.BooleanValue);
        runtime.Observe(CancellationToken.None);
        Assert.False(sink.States.Last().ObservedValue!.BooleanValue);
    }

    [Fact]
    public void ASetterReportingFailureStopsSettlingWhenTheCallerCancels()
    {
        using var cancellation = new CancellationTokenSource();
        var control = new DriverTestControl { FailAfterWrite = "Fixture SetBoolean returned FAIL (3).", IgnoreWrites = true };
        var pauses = 0;
        var runtime = new DriverRuntime("fixture", (_, _) => new DriverTestSession(control))
        {
            Pause = (_, _) =>
            {
                pauses++;
                cancellation.Cancel();
            }
        };
        runtime.Start(new RecordingDriverSink(), StateDirectory, () => true, CancellationToken.None);
        var result = runtime.Execute(DriverTestControl.EnableCommand(), cancellation.Token);
        Assert.Equal(GpuCommandOutcome.Indeterminate, result.Outcome);
        Assert.Equal(1, pauses);
        Assert.Equal(1, control.Writes);
    }
}
