using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Intel.Controls;

/// <summary>What one read returned.</summary>
/// <param name="Value">The value in the descriptor's shape, or null when it is unknown or unreadable.</param>
/// <param name="Reason">Why the control cannot be used right now, when it cannot.</param>
/// <param name="Failure">Why it could not be read, or null when the read succeeded.</param>
internal readonly record struct ControlRead(GpuValue? Value, GpuReason? Reason = null, string? Failure = null)
{
    /// <summary>A successful read; a null value is one the descriptor has no equivalent for.</summary>
    /// <param name="value">The value, or null when unknown.</param>
    /// <returns>The read.</returns>
    public static ControlRead Of(GpuValue? value)
    {
        return new ControlRead(value);
    }

    /// <summary>A read the driver answered with anything but success.</summary>
    /// <param name="result">The IGCL result.</param>
    /// <returns>The read.</returns>
    public static ControlRead Driver(int result)
    {
        return new ControlRead(null, Failure: $"the driver answered {IgclResult.Describe(result)}");
    }

    /// <summary>A read that failed another way.</summary>
    /// <param name="failure">Why.</param>
    /// <returns>The read.</returns>
    public static ControlRead Failed(string failure)
    {
        return new ControlRead(null, Failure: failure);
    }

    /// <summary>A control that cannot be used while something else is set, with what the driver holds.</summary>
    /// <param name="reason">Why.</param>
    /// <param name="value">What the driver applies now, when it is worth showing.</param>
    /// <returns>The read.</returns>
    public static ControlRead Unavailable(GpuReason reason, GpuValue? value = null)
    {
        return new ControlRead(value, reason);
    }
}

/// <summary>How a write ended at the driver.</summary>
internal enum WriteStatus
{
    /// <summary>The driver accepted it.</summary>
    Applied,

    /// <summary>The driver or the plugin validated and refused it; nothing was changed.</summary>
    Refused,

    /// <summary>The write failed mid-call; whether anything changed is unknown.</summary>
    Uncertain
}

/// <summary>What one write returned.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Detail">Why it did not apply, or null when it did.</param>
internal readonly record struct ControlWrite(WriteStatus Status, string? Detail = null)
{
    public static ControlWrite Applied { get; } = new(WriteStatus.Applied);

    /// <summary>Classifies a set call's result.</summary>
    /// <param name="result">The driver result.</param>
    /// <param name="what">What was being written, for the detail.</param>
    /// <returns>Applied, refused or uncertain.</returns>
    public static ControlWrite From(int result, string what)
    {
        if (result == IgclResult.Success)
        {
            return Applied;
        }

        return new ControlWrite(
            IgclResult.IsRefusal(result) ? WriteStatus.Refused : WriteStatus.Uncertain,
            $"The driver answered {IgclResult.Describe(result)} to {what}.");
    }

    /// <summary>A write refused before anything changed.</summary>
    /// <param name="detail">Why.</param>
    /// <returns>The write.</returns>
    public static ControlWrite Refuse(string detail)
    {
        return new ControlWrite(WriteStatus.Refused, detail);
    }
}

/// <summary>
///     One published capability instance and the driver path behind it.
/// </summary>
/// <remarks>
///     A control is built from what the driver reported at the start of a session and lives until the
///     session closes. Reads and writes run on the plugin's lane; nothing here is thread-safe on its own.
///     A write never reads first to decide whether to write, and a read never gates a write: the plugin
///     writes, reads back to grade the outcome, and publishes the written value.
///     <para>
///         The rules every control shares live here: a read-only descriptor refuses writes, a value is
///         validated against the descriptor (range and step included) before it reaches the driver, and a
///         choice must name one of the offered members.
///     </para>
/// </remarks>
internal abstract class IntelControl
{
    protected IntelControl(
        GpuDescriptor descriptor,
        IReadOnlyList<EnumMember>? members = null,
        IntegerRange? range = null)
    {
        Descriptor = descriptor;
        Members = members ?? [];
        Range = range
                ?? (descriptor.ValueKind == GpuValueKind.Integer
                    ? IntegerRange.Linear(descriptor.Minimum ?? 0, descriptor.Maximum ?? 0, descriptor.Step ?? 1)
                    : null);
        Key = DriverIds.Key(descriptor.CapabilityId, descriptor.InstanceId);
    }

    /// <summary>The descriptor as published.</summary>
    public GpuDescriptor Descriptor { get; }

    /// <summary>The capability id.</summary>
    public string CapabilityId => Descriptor.CapabilityId;

    /// <summary>The routing key, built once.</summary>
    public string Key { get; }

    /// <summary>The offered members of a choice, in offer order; empty for other kinds.</summary>
    public IReadOnlyList<EnumMember> Members { get; }

    /// <summary>The integer range of a ranged control, or null for other kinds.</summary>
    public IntegerRange? Range { get; }

    /// <summary>Reads the current value.</summary>
    /// <returns>The value, or a failed read.</returns>
    public abstract ControlRead Read();

    /// <summary>Validates and writes a value.</summary>
    /// <param name="value">The value.</param>
    /// <param name="admission">Rechecked immediately before each native setter.</param>
    /// <returns>How the driver answered, or a refusal when the descriptor does not allow it.</returns>
    public ControlWrite Write(GpuValue value, WriteAdmission admission)
    {
        if (!Descriptor.SupportsWrite)
        {
            return ControlWrite.Refuse($"{CapabilityId} is a status the driver reports; it cannot be set.");
        }

        return Validate(value, out var error)
            ? WriteValidated(value, admission)
            : ControlWrite.Refuse(error ?? "The value is not accepted.");
    }

    /// <summary>Validates a requested value against the descriptor.</summary>
    /// <param name="value">The request.</param>
    /// <param name="error">Why it is not acceptable.</param>
    /// <returns><see langword="true" /> when it may be written.</returns>
    public virtual bool Validate(GpuValue? value, out string? error)
    {
        var valid = DriverDescriptors.Accepts(Descriptor, value, Range);
        error = valid ? null : "The value is not offered by this driver control.";
        return valid;
    }

    /// <summary>The published choice for a driver value.</summary>
    /// <param name="value">The driver value.</param>
    /// <returns>The member's choice, or null when no offered member has that value.</returns>
    public GpuValue? MemberOf(uint value)
    {
        return EnumMembers.ByValue(Members, value)?.Choice;
    }

    /// <summary>The driver value of a validated choice.</summary>
    /// <param name="value">A choice <see cref="Validate" /> accepted.</param>
    /// <returns>The member's driver value.</returns>
    public uint ValueOf(GpuValue value)
    {
        return EnumMembers.ById(Members, value.ChoiceValue)!.Value;
    }

    /// <summary>Writes a value <see cref="Validate" /> accepted.</summary>
    /// <param name="value">The value.</param>
    /// <param name="admission">Rechecked immediately before the setter.</param>
    /// <returns>How the driver answered.</returns>
    protected abstract ControlWrite WriteValidated(GpuValue value, WriteAdmission admission);

    /// <summary>The native flag byte of a validated boolean.</summary>
    /// <param name="value">The boolean value.</param>
    /// <returns>1 when on, 0 otherwise.</returns>
    public static byte Flag(GpuValue value)
    {
        return value.BooleanValue == true ? (byte)1 : (byte)0;
    }
}
