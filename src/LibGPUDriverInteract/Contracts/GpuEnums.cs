// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace LibGPUDriverInteract;

/// <summary>The installed driver family to open.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuVendor>))]
public enum GpuVendor
{
    /// <summary>Intel Graphics Control Library.</summary>
    Intel,

    /// <summary>AMD ADLX.</summary>
    Amd,

    /// <summary>NVIDIA NVAPI and DRS.</summary>
    Nvidia
}

/// <summary>The meaning and presentation shape of a driver control.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuRole>))]
public enum GpuRole
{
    /// <summary>A Boolean setting.</summary>
    Toggle,

    /// <summary>A selection from named values.</summary>
    Choice,

    /// <summary>An integer range.</summary>
    Range,

    /// <summary>An observed value which cannot be changed.</summary>
    ReadOnly,

    /// <summary>A one-shot operation.</summary>
    Action,

    /// <summary>Variable refresh rate enablement.</summary>
    VariableRefreshRate
}

/// <summary>The field carrying a typed value.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuValueKind>))]
public enum GpuValueKind
{
    /// <summary>No value.</summary>
    None,

    /// <summary>A Boolean value.</summary>
    Boolean,

    /// <summary>An integer value.</summary>
    Integer,

    /// <summary>A named choice.</summary>
    Choice
}

/// <summary>The numeric value's unit.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuUnit>))]
public enum GpuUnit
{
    /// <summary>No physical unit.</summary>
    None,

    /// <summary>Percentage.</summary>
    Percent
}

/// <summary>How long a written setting survives.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuPersistence>))]
public enum GpuPersistence
{
    /// <summary>Stored by the driver or device across restart.</summary>
    DevicePersistent,

    /// <summary>Lost when the session or device restarts.</summary>
    Volatile
}

/// <summary>The driver mechanism available to a consumer's application profiles.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuProfileScope>))]
public enum GpuProfileScope
{
    /// <summary>Only global state is available.</summary>
    GlobalOnly,

    /// <summary>The consumer applies global values when its selected application changes.</summary>
    Switched,

    /// <summary>The driver supports executable-specific values through profile synchronization.</summary>
    NativePerApplication
}

/// <summary>When a successfully written setting takes effect.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuApplyTiming>))]
public enum GpuApplyTiming
{
    /// <summary>Immediately.</summary>
    Immediate,

    /// <summary>When the target application next starts.</summary>
    NextApplicationStart,

    /// <summary>After Windows restarts.</summary>
    SystemRestart
}

/// <summary>The evidence supporting an observed value.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuStateQuality>))]
public enum GpuStateQuality
{
    /// <summary>No known value.</summary>
    Unknown,

    /// <summary>Read or accepted by a setter without matching independent confirmation.</summary>
    Observed,

    /// <summary>Independent readback matched the written value.</summary>
    Verified
}

/// <summary>The reason a setting or command cannot complete cleanly.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuReasonCode>))]
public enum GpuReasonCode
{
    /// <summary>The command or observation belongs to an old generation.</summary>
    GenerationChanged,

    /// <summary>The driver session is unavailable.</summary>
    DriverUnavailable,

    /// <summary>A required driver, DLL or service is missing.</summary>
    PrerequisiteMissing,

    /// <summary>Cancellation, deadline or lifecycle closed write admission.</summary>
    Quiescing,

    /// <summary>A native transport operation failed.</summary>
    TransportFaulted,

    /// <summary>The driver does not support the requested setting.</summary>
    Unsupported,

    /// <summary>The requested value does not match the current descriptor.</summary>
    ValueOutOfRange
}

/// <summary>The truthful result of a command; uncertain results never authorize an automatic retry.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuCommandOutcome>))]
public enum GpuCommandOutcome
{
    /// <summary>A setter completed and independent readback matched.</summary>
    AppliedVerified,

    /// <summary>A setter completed without independent matching readback.</summary>
    AppliedUnverified,

    /// <summary>Refused before a setter was dispatched.</summary>
    Rejected,

    /// <summary>A write may have reached the driver before failure.</summary>
    Indeterminate,

    /// <summary>The caller's budget ended with an unknown write outcome.</summary>
    TimedOut,

    /// <summary>Saved for later application, without a current native write.</summary>
    Accepted
}

/// <summary>Current driver availability.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuHealth>))]
public enum GpuHealth
{
    /// <summary>At least one supported driver control is available.</summary>
    Ready,

    /// <summary>No driver session or supported controls are available.</summary>
    Unavailable,

    /// <summary>Startup or lifecycle failed.</summary>
    Failed
}

/// <summary>Diagnostic severity.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuLogLevel>))]
public enum GpuLogLevel
{
    /// <summary>Informational transition.</summary>
    Info,

    /// <summary>Recoverable failure.</summary>
    Warn,

    /// <summary>Operation failure.</summary>
    Error
}

/// <summary>Simple grouping hint for a driver section.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GpuSectionKind>))]
public enum GpuSectionKind
{
    /// <summary>Adapter settings.</summary>
    Adapter,

    /// <summary>Display settings.</summary>
    Display,

    /// <summary>Another group of driver settings.</summary>
    Other
}
