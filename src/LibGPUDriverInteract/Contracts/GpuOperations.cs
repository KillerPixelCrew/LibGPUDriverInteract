// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract;

/// <summary>A typed driver setting value.</summary>
public sealed record GpuValue
{
    /// <summary>The field carrying this value.</summary>
    public required GpuValueKind Kind { get; init; }

    /// <summary>The Boolean field.</summary>
    public bool? BooleanValue { get; init; }

    /// <summary>The integer field.</summary>
    public int? IntegerValue { get; init; }

    /// <summary>The named option field.</summary>
    public string? ChoiceValue { get; init; }

    /// <summary>Creates a value with no payload.</summary>
    /// <returns>The empty value.</returns>
    public static GpuValue None()
    {
        return new GpuValue { Kind = GpuValueKind.None };
    }

    /// <summary>Creates a Boolean value.</summary>
    /// <param name="value">The payload.</param>
    /// <returns>The typed value.</returns>
    public static GpuValue Boolean(bool value)
    {
        return new GpuValue { Kind = GpuValueKind.Boolean, BooleanValue = value };
    }

    /// <summary>Creates an integer value.</summary>
    /// <param name="value">The payload.</param>
    /// <returns>The typed value.</returns>
    public static GpuValue Integer(int value)
    {
        return new GpuValue { Kind = GpuValueKind.Integer, IntegerValue = value };
    }

    /// <summary>Creates a named choice.</summary>
    /// <param name="value">The machine option.</param>
    /// <returns>The typed value.</returns>
    public static GpuValue Choice(string value)
    {
        return new GpuValue { Kind = GpuValueKind.Choice, ChoiceValue = value };
    }
}

/// <summary>An observation, separate from a consumer's requested setting.</summary>
public sealed record GpuState
{
    /// <summary>Setting identity.</summary>
    public required string CapabilityId { get; init; }

    /// <summary>Adapter or display discriminator.</summary>
    public string? InstanceId { get; init; }

    /// <summary>Lifecycle cycle owning this state.</summary>
    public required long CycleGeneration { get; init; }

    /// <summary>Descriptor revision owning this state.</summary>
    public required long DescriptorGeneration { get; init; }

    /// <summary>Last read or accepted value, or null when unknown.</summary>
    public GpuValue? ObservedValue { get; init; }

    /// <summary>Whether this setting can currently be used.</summary>
    public required bool Available { get; init; }

    /// <summary>UTC observation time, or null before observation.</summary>
    public DateTimeOffset? ObservedAt { get; init; }

    /// <summary>Why the state is unavailable or degraded.</summary>
    public GpuReason? Reason { get; init; }

    /// <summary>Evidence supporting the observed value.</summary>
    public required GpuStateQuality Quality { get; init; }
}

/// <summary>A request validated against both live generations immediately before dispatch.</summary>
public sealed record GpuCommand
{
    /// <summary>Correlation identity.</summary>
    public required Guid CommandId { get; init; }

    /// <summary>Lifecycle cycle used to author this request.</summary>
    public required long ExpectedCycleGeneration { get; init; }

    /// <summary>Descriptor revision used to author this request.</summary>
    public required long ExpectedDescriptorGeneration { get; init; }

    /// <summary>Setting identity.</summary>
    public required string CapabilityId { get; init; }

    /// <summary>Adapter or display discriminator.</summary>
    public string? InstanceId { get; init; }

    /// <summary>Requested typed value, or null for an action.</summary>
    public GpuValue? RequestedValue { get; init; }

    /// <summary>Active-time deadline; it cannot terminate a dispatched native call.</summary>
    public required DriverDeadline Deadline { get; init; }
}

/// <summary>A command outcome; no rollback or automatic retry is implied.</summary>
public sealed record GpuCommandResult
{
    /// <summary>The command answered.</summary>
    public required Guid CommandId { get; init; }

    /// <summary>Truthful native-write outcome.</summary>
    public required GpuCommandOutcome Outcome { get; init; }

    /// <summary>Reason for rejection, uncertainty or failed confirmation.</summary>
    public GpuReason? Reason { get; init; }

    /// <summary>Matching independent readback for a verified write only.</summary>
    public GpuValue? ReadbackValue { get; init; }
}

/// <summary>A structured failure or availability reason.</summary>
/// <param name="Code">Machine-readable reason.</param>
/// <param name="Detail">Plain diagnostic detail.</param>
/// <param name="Retryable">Whether a fresh user action could succeed later; never authorizes automatic retry.</param>
public sealed record GpuReason(GpuReasonCode Code, string Detail, bool Retryable = false);

/// <summary>Detached driver availability.</summary>
/// <param name="Health">Current health.</param>
/// <param name="Detail">Plain diagnostic detail.</param>
/// <param name="CycleGeneration">Lifecycle cycle owning this status.</param>
public sealed record GpuStatus(GpuHealth Health, string Detail, long CycleGeneration);

/// <summary>One diagnostic transition delivered to the consumer.</summary>
/// <param name="Level">Severity.</param>
/// <param name="Scope">Driver area.</param>
/// <param name="Message">Plain diagnostic text.</param>
/// <param name="Timestamp">UTC diagnostic time.</param>
public sealed record GpuLogEntry(GpuLogLevel Level, string Scope, string Message, DateTimeOffset Timestamp);

/// <summary>A complete desired set of native executable profiles.</summary>
/// <param name="Revision">Increasing consumer revision.</param>
/// <param name="CycleGeneration">Lifecycle cycle used to resolve values.</param>
/// <param name="Profiles">Complete desired native application profile set.</param>
public sealed record GpuProfileSync(long Revision, long CycleGeneration, IReadOnlyList<GpuApplicationProfile> Profiles);

/// <summary>One application's native overrides.</summary>
/// <param name="ProfileId">Consumer-owned stable identity.</param>
/// <param name="DisplayName">Plain application name.</param>
/// <param name="Executables">Executable file names as matched by the driver.</param>
/// <param name="Values">Desired native overrides.</param>
public sealed record GpuApplicationProfile(
    string ProfileId,
    string DisplayName,
    IReadOnlyList<string> Executables,
    IReadOnlyList<GpuApplicationValue> Values);

/// <summary>One native application override.</summary>
/// <param name="CapabilityId">Setting identity.</param>
/// <param name="InstanceId">Adapter or display discriminator.</param>
/// <param name="Value">Typed requested value.</param>
public sealed record GpuApplicationValue(string CapabilityId, string? InstanceId, GpuValue Value);

/// <summary>Outcome of a native profile synchronization.</summary>
/// <param name="Written">Entries written or confirmed.</param>
/// <param name="Removed">Previously owned entries removed.</param>
/// <param name="Failures">Entries refused or left uncertain.</param>
public sealed record GpuProfileSyncResult(int Written, int Removed, IReadOnlyList<GpuProfileFailure> Failures);

/// <summary>One refused or uncertain profile entry.</summary>
/// <param name="ProfileId">Consumer profile identity.</param>
/// <param name="Executable">Executable file name.</param>
/// <param name="CapabilityId">Setting identity.</param>
/// <param name="Detail">Plain diagnostic detail.</param>
public sealed record GpuProfileFailure(string ProfileId, string Executable, string CapabilityId, string Detail);
