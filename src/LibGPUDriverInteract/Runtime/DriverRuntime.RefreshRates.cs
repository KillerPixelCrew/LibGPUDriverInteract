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

    internal bool SetRefreshRateBaseline(GpuDisplayTarget display, GpuArcSyncBaseline? baseline)
    {
        try
        {
            return (_session as IntelSession)?.SetRefreshRateBaseline(display, baseline) ?? false;
        }
        catch (DriverFailure failure) when (failure.Lost)
        {
            Lose(failure.Message);
            return false;
        }
    }

    internal GpuArcSyncState? ReadArcSync(GpuDisplayTarget display)
    {
        try
        {
            return (_session as IntelSession)?.ReadArcSync(display);
        }
        catch (DriverFailure failure) when (failure.Lost)
        {
            Lose(failure.Message);
            return null;
        }
    }

    internal GpuVrrSolutions? ReadVrrSolutions(GpuDisplayTarget display)
    {
        try
        {
            return (_session as IntelSession)?.ReadVrrSolutions(display);
        }
        catch (DriverFailure failure) when (failure.Lost)
        {
            Lose(failure.Message);
            return null;
        }
    }

    internal GpuDisplayWriteResult SetArcSyncProfile(GpuDisplayTarget display, GpuArcSyncBaseline profile,
        CancellationToken token)
    {
        return OnIntelWrite(token,
            session => session.SetArcSyncProfile(display, profile, new WriteAdmission(token, _active)));
    }

    internal GpuDisplayWriteResult SetVrrSolution(GpuDisplayTarget display, GpuVrrSolution solution, bool enabled,
        CancellationToken token)
    {
        return OnIntelWrite(token,
            session => session.SetVrrSolution(display, solution, enabled, new WriteAdmission(token, _active)));
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

    private GpuDisplayWriteResult OnIntelWrite(CancellationToken token,
        Func<IntelSession, GpuDisplayWriteResult> work)
    {
        var result = OnRefreshSession(token, session =>
        {
            var write = work(session);
            return new GpuRefreshRateResult(
                write.Applied ? GpuRefreshRateOutcome.Applied : GpuRefreshRateOutcome.Refused, null, write.Detail);
        });
        return new GpuDisplayWriteResult(result.Outcome is GpuRefreshRateOutcome.Applied, result.Detail);
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
