// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract.Runtime;

internal interface IDriverSink
{
    ValueTask PublishDescriptorsAsync(GpuDescriptorSet descriptors, CancellationToken cancellationToken);
    ValueTask PublishCapabilityStateAsync(GpuState state, CancellationToken cancellationToken);
    void ReportHealth(GpuHealth health, string detail);
    void Log(GpuLogLevel level, string scope, string message);
    void LogChange(GpuLogLevel level, string scope, string key, string message);
}

internal static class CommandResults
{
    internal static GpuCommandResult Verified(GpuCommand command, GpuValue readback)
    {
        return new GpuCommandResult { Outcome = GpuCommandOutcome.AppliedVerified, ReadbackValue = readback };
    }

    internal static GpuCommandResult Unverified(GpuCommand command, string? detail = null)
    {
        return new GpuCommandResult
        {
            Outcome = GpuCommandOutcome.AppliedUnverified,
            Reason = detail is null ? null : new GpuReason(GpuReasonCode.TransportFaulted, detail)
        };
    }

    internal static GpuCommandResult Rejected(GpuCommand command, GpuReasonCode code, string detail,
        bool retryable = false)
    {
        return new GpuCommandResult
        {
            Outcome = GpuCommandOutcome.Rejected, Reason = new GpuReason(code, detail, retryable)
        };
    }

    internal static GpuCommandResult Indeterminate(GpuCommand command, GpuReasonCode code, string detail)
    {
        return new GpuCommandResult
        {
            Outcome = GpuCommandOutcome.Indeterminate, Reason = new GpuReason(code, detail)
        };
    }
}
