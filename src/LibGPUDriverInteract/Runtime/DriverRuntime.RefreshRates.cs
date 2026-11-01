// SPDX-License-Identifier: MIT

using LibGPUDriverInteract.Intel;

namespace LibGPUDriverInteract.Runtime;

internal sealed partial class DriverRuntime
{
    internal GpuRefreshRateRange? ReadRefreshRateRange(GpuDisplayTarget display)
    {
        try
        {
            return (_session as IntelSession)?.ReadRefreshRateRange(display);
        }
        catch (DriverFailure failure) when (failure.Lost)
        {
            Lose(failure.Message);
            return null;
        }
    }

    internal GpuRefreshRateResult SetRefreshRate(GpuDisplayTarget display, double refreshHz, CancellationToken token)
    {
        return OnRefreshSession(token, session => session.SetRefreshRate(display, refreshHz,
            new WriteAdmission(token, _active)));
    }

    internal GpuRefreshRateResult RestoreRefreshRate(GpuDisplayTarget display, CancellationToken token)
    {
        return OnRefreshSession(token, session => session.RestoreRefreshRate(display,
            new WriteAdmission(token, _active)));
    }

    private GpuRefreshRateResult OnRefreshSession(CancellationToken token,
        Func<IntelSession, GpuRefreshRateResult> work)
    {
        if (_session is not IntelSession session)
        {
            return new GpuRefreshRateResult(GpuRefreshRateOutcome.Refused, null,
                Vendor == GpuVendor.Intel
                    ? "The Intel graphics driver session is not open."
                    : "Only the Intel driver holds a display at a refresh rate.");
        }

        try
        {
            token.ThrowIfCancellationRequested();
            return work(session);
        }
        catch (DriverFailure failure)
        {
            if (failure.Lost)
            {
                Lose(failure.Message);
            }

            return new GpuRefreshRateResult(GpuRefreshRateOutcome.Refused, null, failure.Message);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new GpuRefreshRateResult(GpuRefreshRateOutcome.Refused, null, "Cancelled before dispatch.");
        }
    }
}
