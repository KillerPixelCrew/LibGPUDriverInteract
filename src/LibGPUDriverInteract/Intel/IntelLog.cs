using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Intel;

/// <summary>Driver diagnostic lines, written through the caller's sink.</summary>
/// <remarks>
///     Every transport takes this sink. Before the sink is known, and in tests, the lines go nowhere. Lines are written
///     whole. Never throws.
/// </remarks>
internal sealed class IntelLog
{
    private readonly IDriverSink? _sink;

    /// <summary>Creates a log that writes through the sink, or nowhere when it is null.</summary>
    /// <param name="sink">The caller's diagnostic sink.</param>
    public IntelLog(IDriverSink? sink)
    {
        _sink = sink;
    }

    /// <summary>A log that writes nothing.</summary>
    public static IntelLog None { get; } = new(null);

    public void Info(string scope, string message)
    {
        Write(GpuLogLevel.Info, scope, message);
    }

    public void Warn(string scope, string message)
    {
        Write(GpuLogLevel.Warn, scope, message);
    }

    public void Error(string scope, string message)
    {
        Write(GpuLogLevel.Error, scope, message);
    }

    /// <summary>Records an exception with its type, which a bare message often leaves ambiguous.</summary>
    /// <param name="scope">Subsystem.</param>
    /// <param name="context">What was being done.</param>
    /// <param name="error">The exception.</param>
    public void Failure(string scope, string context, Exception error)
    {
        Write(GpuLogLevel.Error, scope, $"{context}: {Describe(error)}");
    }

    /// <summary>Records a polled state, written only when that key's value changed.</summary>
    /// <param name="level">Level for the line when it is written.</param>
    /// <param name="scope">Subsystem.</param>
    /// <param name="key">Stable identity of the thing observed.</param>
    /// <param name="message">The current state.</param>
    public void Change(GpuLogLevel level, string scope, string key, string message)
    {
        if (_sink is null)
        {
            return;
        }

        try
        {
            _sink.LogChange(level, scope, key, message);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Diagnostics never take a control path down with them.
        }
    }

    /// <summary>Renders an exception for a trace line: its type and message.</summary>
    /// <param name="error">The exception.</param>
    /// <returns>Plain text.</returns>
    public static string Describe(Exception error)
    {
        return $"{error.GetType().Name}: {error.Message}";
    }

    private void Write(GpuLogLevel level, string scope, string message)
    {
        if (_sink is null)
        {
            return;
        }

        try
        {
            _sink.Log(level, scope, message);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Diagnostics never take a control path down with them.
        }
    }
}
