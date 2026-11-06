namespace LibGPUDriverInteract;

/// <summary>The completed vendor installer outcome; a non-success must never be replayed automatically.</summary>
public sealed record GpuDriverInstallResult(bool Succeeded, bool RestartRequired, int ExitCode, string Detail);
