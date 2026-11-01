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

/// <summary>An Intel Arc Sync profile, as the driver names them.</summary>
public enum GpuArcSyncProfile
{
    /// <summary>The driver's recommended profile.</summary>
    Recommended = 1,

    /// <summary>The Excellent profile.</summary>
    Excellent = 2,

    /// <summary>The Good profile.</summary>
    Good = 3,

    /// <summary>The Compatible profile.</summary>
    Compatible = 4,

    /// <summary>Variable refresh off.</summary>
    Off = 5,

    /// <summary>The VESA profile.</summary>
    Vesa = 6,

    /// <summary>A custom refresh range and frame time limits.</summary>
    Custom = 7
}

/// <summary>The Arc Sync profile a released fixed refresh rate returns to on one display.</summary>
/// <param name="Profile">The profile.</param>
/// <param name="MinimumHz">The lowest refresh rate the profile uses.</param>
/// <param name="MaximumHz">The highest refresh rate the profile uses.</param>
/// <param name="MaxFrameTimeIncreaseUs">The frame time increase limit, in microseconds.</param>
/// <param name="MaxFrameTimeDecreaseUs">The frame time decrease limit, in microseconds.</param>
public sealed record GpuArcSyncBaseline(
    GpuArcSyncProfile Profile,
    double MinimumHz,
    double MaximumHz,
    uint MaxFrameTimeIncreaseUs = 0,
    uint MaxFrameTimeDecreaseUs = 0);

/// <summary>One display's Intel Arc Sync state, as the policy that owns the profile reads it.</summary>
/// <param name="Display">The display.</param>
/// <param name="MonitorMinimumHz">The lowest rate the monitor reports for variable refresh.</param>
/// <param name="MonitorMaximumHz">The highest rate the monitor reports for variable refresh.</param>
/// <param name="Profile">The profile the driver runs now; the Custom fixed profile while a rate is fixed.</param>
/// <param name="FixedHz">The rate this driver owner holds now, or null when none is fixed.</param>
/// <param name="BeforeFixed">While a rate is fixed, the profile in use before the first fixed write; otherwise null.</param>
public sealed record GpuArcSyncState(
    GpuDisplayTarget Display,
    double MonitorMinimumHz,
    double MonitorMaximumHz,
    GpuArcSyncBaseline Profile,
    double? FixedHz,
    GpuArcSyncBaseline? BeforeFixed);

/// <summary>One of Intel's variable refresh frame rate solutions.</summary>
public enum GpuVrrSolution
{
    /// <summary>The low frame rate solution (LFC), which repeats frames below the panel's range.</summary>
    LowFrameRate,

    /// <summary>The high frame rate solution.</summary>
    HighFrameRate
}

/// <summary>Intel's variable refresh state for the adapter driving a display, from its private VRR escape.</summary>
/// <param name="Supported">Whether the adapter reports variable refresh support.</param>
/// <param name="Enabled">Whether variable refresh is enabled.</param>
/// <param name="LowFrameRate">Whether the low frame rate solution (LFC) is on.</param>
/// <param name="HighFrameRate">Whether the high frame rate solution is on.</param>
public sealed record GpuVrrSolutions(bool Supported, bool Enabled, bool LowFrameRate, bool HighFrameRate);

/// <summary>The result of a display write the owning policy made.</summary>
/// <param name="Applied">Whether the driver accepted the setter.</param>
/// <param name="Detail">A sentence for the log.</param>
public sealed record GpuDisplayWriteResult(bool Applied, string Detail);
