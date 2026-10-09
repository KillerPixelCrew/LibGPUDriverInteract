// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract.Runtime;

/// <summary>Native diagnostics use the owner's sink and its single exception boundary.</summary>
internal static class DriverLog
{
    internal static IDriverSink None { get; } = new NullSink();

    internal static void Info(this IDriverSink sink, string scope, string message)
    {
        sink.Log(GpuLogLevel.Info, scope, message);
    }

    internal static void Warn(this IDriverSink sink, string scope, string message)
    {
        sink.Log(GpuLogLevel.Warn, scope, message);
    }

    internal static void Error(this IDriverSink sink, string scope, string message)
    {
        sink.Log(GpuLogLevel.Error, scope, message);
    }

    internal static void Failure(this IDriverSink sink, string scope, string context, Exception error)
    {
        sink.Error(scope, context + ": " + Describe(error));
    }

    internal static void Change(this IDriverSink sink, GpuLogLevel level, string scope, string key, string message)
    {
        sink.LogChange(level, scope, key, message);
    }

    internal static string Describe(Exception error)
    {
        return $"{error.GetType().Name}: {error.Message}";
    }

    private sealed class NullSink : IDriverSink
    {
        public ValueTask PublishDescriptorsAsync(GpuDescriptorSet descriptors, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishCapabilityStateAsync(GpuState state, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public void ReportHealth(GpuHealth health, string detail)
        {
        }

        public void Log(GpuLogLevel level, string scope, string message)
        {
        }

        public void LogChange(GpuLogLevel level, string scope, string key, string message)
        {
        }
    }
}
