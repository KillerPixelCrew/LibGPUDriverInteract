// SPDX-License-Identifier: MIT

using LibGPUDriverInteract.Intel.Display;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Intel;

internal sealed partial class IntelSession
{
    /// <summary>Admits the writes the session itself owns: releasing fixed rates at close and at start.</summary>
    private static WriteAdmission Unbounded => new(CancellationToken.None, static () => true);

    internal GpuRefreshRateRange? ReadRefreshRateRange(GpuDisplayTarget display)
    {
        EnsureSession();
        return ArcSyncFor(display) is { RateRangeAvailable: true } arcSync
            ? new GpuRefreshRateRange(display, arcSync.Monitor.MinimumHz, arcSync.Monitor.MaximumHz, arcSync.FixedHz)
            : null;
    }

    internal GpuRefreshRateResult SetRefreshRate(GpuDisplayTarget display, double hz, WriteAdmission admission)
    {
        EnsureSession();
        if (ArcSyncFor(display) is not { RateRangeAvailable: true } arcSync)
        {
            return Refused("This display has no Intel Arc Sync range to fix a refresh rate in.");
        }

        _session.BeginPass();
        var result = arcSync.WriteFixedRate((float)hz, admission, out var applied);
        return result == IgclResult.Success
            ? new GpuRefreshRateResult(GpuRefreshRateOutcome.Applied, applied,
                $"The Custom Arc Sync profile holds {applied:0.###} Hz.")
            : new GpuRefreshRateResult(GpuRefreshRateOutcome.Refused, arcSync.FixedHz,
                "Intel returned " + IgclResult.Describe(result) + ".");
    }

    internal GpuRefreshRateResult RestoreRefreshRate(GpuDisplayTarget display, WriteAdmission admission)
    {
        EnsureSession();
        if (ArcSyncFor(display) is not { } arcSync)
        {
            return new GpuRefreshRateResult(GpuRefreshRateOutcome.Unchanged, null, "This display has no Arc Sync.");
        }

        _session.BeginPass();
        var result = arcSync.RestoreRate(admission, out var wrote);
        return result == IgclResult.Success
            ? new GpuRefreshRateResult(wrote ? GpuRefreshRateOutcome.Applied : GpuRefreshRateOutcome.Unchanged, null,
                wrote ? "The Arc Sync profile in use before is back." : "No refresh rate was fixed.")
            : new GpuRefreshRateResult(GpuRefreshRateOutcome.Refused, arcSync.FixedHz,
                "Intel returned " + IgclResult.Describe(result) + "; the rate stays fixed.");
    }

    internal bool SetRefreshRateBaseline(GpuDisplayTarget display, GpuArcSyncBaseline? baseline)
    {
        EnsureSession();
        if (ArcSyncFor(display) is not { } arcSync)
        {
            return false;
        }

        arcSync.SetBaseline(baseline is null ? null : ArcSyncDisplay.ToNative(baseline));
        return true;
    }

    internal GpuArcSyncState? ReadArcSync(GpuDisplayTarget display)
    {
        EnsureSession();
        if (ArcSyncFor(display) is not { } arcSync)
        {
            return null;
        }

        _session.BeginPass();
        return arcSync.ReadState(out var profile, out var beforeFixed) == IgclResult.Success
            ? new GpuArcSyncState(display, arcSync.Monitor.MinimumHz, arcSync.Monitor.MaximumHz, ToPublic(profile),
                arcSync.FixedHz, beforeFixed is { } before ? ToPublic(before) : null)
            : null;
    }

    internal GpuDisplayWriteResult SetArcSyncProfile(GpuDisplayTarget display, GpuArcSyncBaseline profile,
        WriteAdmission admission)
    {
        EnsureSession();
        if (ArcSyncFor(display) is not { } arcSync)
        {
            return new GpuDisplayWriteResult(false, "This display has no Intel Arc Sync.");
        }

        _session.BeginPass();
        var result = arcSync.WritePolicyProfile(ArcSyncDisplay.ToNative(profile), admission, out var heldByFixed);
        return heldByFixed
            ? new GpuDisplayWriteResult(false, "A fixed refresh rate holds the display; nothing was written.")
            : new GpuDisplayWriteResult(result == IgclResult.Success,
                "Intel returned " + IgclResult.Describe(result) + ".");
    }

    internal GpuVrrSolutions? ReadVrrSolutions(GpuDisplayTarget display)
    {
        EnsureSession();
        if (FindOutput(display) is not { } output)
        {
            return null;
        }

        var answer = VrrSolutionEscape.Invoke(output.Adapter.Luid, VrrSolutionEscape.Read);
        if (answer.Status != 0)
        {
            _log.Warn("vrr", $"The Intel private VRR escape failed: 0x{answer.Status:X8}.");
            return null;
        }

        return answer.State;
    }

    internal GpuDisplayWriteResult SetVrrSolution(GpuDisplayTarget display, GpuVrrSolution solution, bool enabled,
        WriteAdmission admission)
    {
        EnsureSession();
        if (FindOutput(display) is not { } output)
        {
            return new GpuDisplayWriteResult(false, "No Intel output drives this display.");
        }

        var operation = VrrSolutionEscape.Operation(solution, enabled);
        admission.Check();
        var answer = VrrSolutionEscape.Invoke(output.Adapter.Luid, operation);
        _log.Info("vrr", $"{solution} solution {(enabled ? "on" : "off")} (escape operation {operation}): "
                         + (answer.Status == 0 ? "accepted." : $"0x{answer.Status:X8}."));
        return new GpuDisplayWriteResult(answer.Status == 0,
            answer.Status == 0 ? "The Intel VRR escape accepted it." : $"The Intel VRR escape failed: 0x{answer.Status:X8}.");
    }

    private static GpuArcSyncBaseline ToPublic(CtlArcSyncProfileParams profile)
    {
        return new GpuArcSyncBaseline((GpuArcSyncProfile)profile.Profile, profile.MinimumHz, profile.MaximumHz,
            profile.MaxFrameTimeIncreaseUs, profile.MaxFrameTimeDecreaseUs);
    }

    private ArcSyncDisplay? ArcSyncFor(GpuDisplayTarget display)
    {
        return FindOutput(display) is { } output ? _model?.ArcSync(output) : null;
    }

    private static GpuRefreshRateResult Refused(string detail)
    {
        return new GpuRefreshRateResult(GpuRefreshRateOutcome.Refused, null, detail);
    }
}
