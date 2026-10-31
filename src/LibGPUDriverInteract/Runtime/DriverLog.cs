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

    /// <summary>Runs one discovery step, reporting a failure and continuing unless the driver was lost.</summary>
    /// <param name="report">Receives the step key and the failure message.</param>
    /// <param name="key">The step.</param>
    /// <param name="action">The step's work.</param>
    internal static void Try(Action<string, string> report, string key, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is not DriverFailure { Lost: true })
        {
            report(key, error.Message);
        }
    }

    internal static string Describe(Exception error)
    {
        return $"{error.GetType().Name}: {error.Message}";
    }

    private sealed class NullSink : IDriverSink
    {
        public void PublishDescriptors(GpuDescriptorSet descriptors)
        {
        }

        public void PublishCapabilityState(GpuState state)
        {
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
