// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract;

public sealed partial class GpuDriver
{
    /// <summary>Reads the range a refresh rate can be fixed in on one display, on the serialized driver lane.</summary>
    /// <returns>The range, or null when this vendor or display cannot fix a rate.</returns>
    /// <remarks>Intel only: the monitor's Arc Sync range from <c>ctlGetIntelArcSyncInfoForMonitor</c>.</remarks>
    public async ValueTask<GpuRefreshRateRange?> ReadRefreshRateRangeAsync(GpuDisplayTarget display,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        return await OnLaneAsync(() => _runtime.ReadRefreshRateRange(display), () => null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Holds one display at a refresh rate without changing its Windows mode.</summary>
    /// <param name="display">The display.</param>
    /// <param name="refreshHz">The rate, fractions allowed; clamped to the range the monitor reports.</param>
    /// <param name="cancellationToken">Cancels the wait for the lane; not a dispatched setter.</param>
    /// <returns>The outcome and the rate the request carried.</returns>
    /// <remarks>
    ///     Intel writes the Custom Arc Sync profile with its minimum and maximum both at the rate. The first
    ///     write captures the profile in use, and <see cref="RestoreRefreshRateAsync" />, a stop, or the next
    ///     start after a crash puts it back. A write through an Arc Sync control hands the profile back to the
    ///     user and ends the fixed rate.
    /// </remarks>
    public async ValueTask<GpuRefreshRateResult> SetRefreshRateAsync(GpuDisplayTarget display, double refreshHz,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (!double.IsFinite(refreshHz) || refreshHz <= 0)
        {
            return Closed("The refresh rate is not a positive number.");
        }

        return await OnLaneAsync(() => _runtime.SetRefreshRate(display, refreshHz, cancellationToken),
            () => Closed("Driver admission is closed."), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Puts back the profile in use before a refresh rate was fixed on one display.</summary>
    /// <returns>Applied when a profile was written back, Unchanged when no rate was fixed.</returns>
    public async ValueTask<GpuRefreshRateResult> RestoreRefreshRateAsync(GpuDisplayTarget display,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        return await OnLaneAsync(() => _runtime.RestoreRefreshRate(display, cancellationToken),
            () => Closed("Driver admission is closed."), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Supplies the Arc Sync profile a released fixed refresh rate returns to on one display, instead of
    ///     the profile captured before the first fixed rate.
    /// </summary>
    /// <param name="display">The display.</param>
    /// <param name="baseline">The profile, or null to return to the captured one again.</param>
    /// <param name="cancellationToken">Cancels the wait for the lane.</param>
    /// <returns>Whether the display has Arc Sync and took the baseline; false on another vendor or display.</returns>
    /// <remarks>
    ///     For the one policy that owns the Arc Sync profile beneath a fixed rate, such as a variable refresh
    ///     range it keeps. Nothing is written now: the baseline is what the next release, the session's close or
    ///     the next start's recovery writes. It lasts for the session.
    /// </remarks>
    public async ValueTask<bool> SetRefreshRateBaselineAsync(GpuDisplayTarget display, GpuArcSyncBaseline? baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        return await OnLaneAsync(() => _runtime.SetRefreshRateBaseline(display, baseline), () => false,
            cancellationToken).ConfigureAwait(false);
    }

    private static GpuRefreshRateResult Closed(string detail)
    {
        return new GpuRefreshRateResult(GpuRefreshRateOutcome.Refused, null, detail);
    }
}
