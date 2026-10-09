// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract.Runtime;

internal sealed record DriverContext(long Generation, DriverDeadline Deadline, string StateDirectory);

internal interface IDriverEngine : IAsyncDisposable
{
    ValueTask<GpuHealth> StartAsync(IDriverSink sink, DriverContext context, CancellationToken cancellationToken);
    ValueTask SessionChangedAsync(DriverContext context, CancellationToken cancellationToken);
    ValueTask SuspendAsync(DriverContext context, CancellationToken cancellationToken);
    ValueTask ResumeAsync(DriverContext context, CancellationToken cancellationToken);
    ValueTask<bool> StopAsync(DriverContext context, CancellationToken cancellationToken);
    ValueTask<GpuCommandResult> ExecuteCommandAsync(GpuCommand command, CancellationToken cancellationToken);

    ValueTask<GpuProfileSyncResult> SyncApplicationProfilesAsync(GpuProfileSync sync,
        CancellationToken cancellationToken);
}

internal interface IDriverSink
{
    long CycleGeneration { get; }
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
        return new GpuCommandResult
        {
            CommandId = command.CommandId, Outcome = GpuCommandOutcome.AppliedVerified, ReadbackValue = readback
        };
    }

    internal static GpuCommandResult Unverified(GpuCommand command, string? detail = null)
    {
        return new GpuCommandResult
        {
            CommandId = command.CommandId, Outcome = GpuCommandOutcome.AppliedUnverified,
            Reason = detail is null ? null : new GpuReason(GpuReasonCode.TransportFaulted, detail)
        };
    }

    internal static GpuCommandResult Rejected(GpuCommand command, GpuReasonCode code, string detail,
        bool retryable = false)
    {
        return Rejected(command, new GpuReason(code, detail, retryable));
    }

    internal static GpuCommandResult Rejected(GpuCommand command, GpuReason reason)
    {
        return new GpuCommandResult
        {
            CommandId = command.CommandId, Outcome = GpuCommandOutcome.Rejected, Reason = reason
        };
    }

    internal static GpuCommandResult Indeterminate(GpuCommand command, GpuReasonCode code, string detail)
    {
        return new GpuCommandResult
        {
            CommandId = command.CommandId, Outcome = GpuCommandOutcome.Indeterminate,
            Reason = new GpuReason(code, detail)
        };
    }
}
