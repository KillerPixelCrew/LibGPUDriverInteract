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

    private ArcSyncDisplay? ArcSyncFor(GpuDisplayTarget display)
    {
        return FindOutput(display) is { } output ? _model?.ArcSync(output) : null;
    }

    private static GpuRefreshRateResult Refused(string detail)
    {
        return new GpuRefreshRateResult(GpuRefreshRateOutcome.Refused, null, detail);
    }
}
