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

    /// <summary>Reads one display's Arc Sync range and profile, on the serialized driver lane.</summary>
    /// <param name="display">The display.</param>
    /// <param name="cancellationToken">Cancels the wait for the lane.</param>
    /// <returns>The state, or null when this vendor or display has no Arc Sync or the profile could not be read.</returns>
    /// <remarks>Intel only: <c>ctlGetIntelArcSyncInfoForMonitor</c> and <c>ctlGetIntelArcSyncProfile</c>.</remarks>
    public async ValueTask<GpuArcSyncState?> ReadArcSyncAsync(GpuDisplayTarget display,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        return await OnLaneAsync(() => _runtime.ReadArcSync(display), () => null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Writes one display's Arc Sync profile for the policy that owns it, not for a user's row.</summary>
    /// <param name="display">The display.</param>
    /// <param name="profile">The profile and its values, written as given.</param>
    /// <param name="cancellationToken">Cancels the wait for the lane; not a dispatched setter.</param>
    /// <returns>Whether the driver accepted it.</returns>
    /// <remarks>
    ///     While this owner holds a fixed refresh rate on the display nothing is written and the result says so:
    ///     supply the profile through <see cref="SetRefreshRateBaselineAsync" /> and the release writes it. Unlike a
    ///     write through an Arc Sync row this does not hand the profile to the user, and turning variable refresh
    ///     back on returns to it. The write is never retried.
    /// </remarks>
    public async ValueTask<GpuDisplayWriteResult> SetArcSyncProfileAsync(GpuDisplayTarget display,
        GpuArcSyncBaseline profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(profile);
        return await OnLaneAsync(() => _runtime.SetArcSyncProfile(display, profile, cancellationToken),
            () => new GpuDisplayWriteResult(false, "Driver admission is closed."), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads Intel's variable refresh frame rate solutions for the adapter driving a display.</summary>
    /// <param name="display">The display.</param>
    /// <param name="cancellationToken">Cancels the wait for the lane.</param>
    /// <returns>The state, or null on another vendor or display, or when the escape failed (logged).</returns>
    /// <remarks>Intel only: the driver's private D3DKMT VRR escape, read operation.</remarks>
    public async ValueTask<GpuVrrSolutions?> ReadVrrSolutionsAsync(GpuDisplayTarget display,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        return await OnLaneAsync(() => _runtime.ReadVrrSolutions(display), () => null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Turns one of Intel's variable refresh frame rate solutions on or off for the adapter driving a display.
    /// </summary>
    /// <param name="display">The display.</param>
    /// <param name="solution">The solution.</param>
    /// <param name="enabled">The request.</param>
    /// <param name="cancellationToken">Cancels the wait for the lane; not a dispatched setter.</param>
    /// <returns>Whether the driver accepted it.</returns>
    /// <remarks>
    ///     Intel only: the private VRR escape's operations 3 and 4 (low frame rate on, off) and 5 and 6 (high
    ///     frame rate on, off). The setting is adapter-wide driver state that outlives the process; the caller
    ///     owns putting it back. The write is never retried.
    /// </remarks>
    public async ValueTask<GpuDisplayWriteResult> SetVrrSolutionAsync(GpuDisplayTarget display,
        GpuVrrSolution solution, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        return await OnLaneAsync(() => _runtime.SetVrrSolution(display, solution, enabled, cancellationToken),
            () => new GpuDisplayWriteResult(false, "Driver admission is closed."), cancellationToken)
            .ConfigureAwait(false);
    }

    private static GpuRefreshRateResult Closed(string detail)
    {
        return new GpuRefreshRateResult(GpuRefreshRateOutcome.Refused, null, detail);
    }
}
