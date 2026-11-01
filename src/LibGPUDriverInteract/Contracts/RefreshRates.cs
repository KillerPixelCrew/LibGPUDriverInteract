// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract;

/// <summary>The refresh rates a driver can hold one display at, without a Windows mode change.</summary>
/// <param name="Display">The display the range belongs to.</param>
/// <param name="MinimumHz">The lowest rate the monitor reports.</param>
/// <param name="MaximumHz">The highest rate the monitor reports.</param>
/// <param name="FixedHz">The rate this driver owner holds now, or null when none is fixed.</param>
public sealed record GpuRefreshRateRange(GpuDisplayTarget Display, double MinimumHz, double MaximumHz, double? FixedHz);

/// <summary>What a refresh-rate write or release did.</summary>
public enum GpuRefreshRateOutcome
{
    /// <summary>The driver accepted the setter.</summary>
    Applied,

    /// <summary>Nothing needed writing.</summary>
    Unchanged,

    /// <summary>The write was refused or the driver answered with a failure; nothing is retried.</summary>
    Refused
}

/// <summary>The result of a refresh-rate write or release.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="RefreshHz">The rate now fixed, after clamping to the monitor's range, or null when none is.</param>
/// <param name="Detail">A sentence for the log.</param>
public sealed record GpuRefreshRateResult(GpuRefreshRateOutcome Outcome, double? RefreshHz, string Detail);
