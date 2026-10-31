// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract.Runtime;

internal interface IDriverSink
{
    void PublishDescriptors(GpuDescriptorSet descriptors);
    void PublishCapabilityState(GpuState state);
    void ReportHealth(GpuHealth health, string detail);
    void Log(GpuLogLevel level, string scope, string message);
    void LogChange(GpuLogLevel level, string scope, string key, string message);
}

internal static class CommandResults
{
    internal static GpuCommandResult Verified(GpuValue readback)
    {
        return new GpuCommandResult { Outcome = GpuCommandOutcome.AppliedVerified, ReadbackValue = readback };
    }

    internal static GpuCommandResult Unverified(string? detail = null)
    {
        return new GpuCommandResult
        {
            Outcome = GpuCommandOutcome.AppliedUnverified,
            Reason = detail is null ? null : new GpuReason(GpuReasonCode.TransportFaulted, detail)
        };
    }

    internal static GpuCommandResult Rejected(GpuReasonCode code, string detail, bool retryable = false)
    {
        return new GpuCommandResult
        {
            Outcome = GpuCommandOutcome.Rejected, Reason = new GpuReason(code, detail, retryable)
        };
    }

    internal static GpuCommandResult Indeterminate(GpuReasonCode code, string detail)
    {
        return new GpuCommandResult
        {
            Outcome = GpuCommandOutcome.Indeterminate, Reason = new GpuReason(code, detail)
        };
    }
}
