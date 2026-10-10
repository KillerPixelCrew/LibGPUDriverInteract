// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract;

/// <summary>One supported driver control, containing plain grouping and value metadata.</summary>
public sealed record GpuDescriptor
{
    /// <summary>Stable setting identity.</summary>
    public required string CapabilityId { get; init; }

    /// <summary>Adapter or display discriminator, or null for a global setting.</summary>
    public string? InstanceId { get; init; }

    /// <summary>Plain display label.</summary>
    public required string Label { get; init; }

    /// <summary>Control meaning.</summary>
    public required GpuRole Role { get; init; }

    /// <summary>Accepted value shape.</summary>
    public required GpuValueKind ValueKind { get; init; }

    /// <summary>Declared section containing the control.</summary>
    public string? SectionId { get; init; }

    /// <summary>Declared category within its section.</summary>
    public string? CategoryId { get; init; }

    /// <summary>Relative order within its group.</summary>
    public int SortOrder { get; init; }

    /// <summary>Whether the driver exposes a read.</summary>
    public bool SupportsRead { get; init; }

    /// <summary>Whether the driver exposes a setter.</summary>
    public bool SupportsWrite { get; init; }

    /// <summary>Inclusive integer minimum.</summary>
    public int? Minimum { get; init; }

    /// <summary>Inclusive integer maximum.</summary>
    public int? Maximum { get; init; }

    /// <summary>Legal integer step.</summary>
    public int? Step { get; init; }

    /// <summary>Numeric unit.</summary>
    public GpuUnit Unit { get; init; }

    /// <summary>Setting lifetime.</summary>
    public GpuPersistence Persistence { get; init; } = GpuPersistence.DevicePersistent;

    /// <summary>Available application-profile mechanism.</summary>
    public GpuProfileScope ProfileScope { get; init; } = GpuProfileScope.Switched;

    /// <summary>When an accepted setter becomes effective.</summary>
    public GpuApplyTiming ApplyTiming { get; init; }

    /// <summary>Legal named options.</summary>
    public IReadOnlyList<GpuChoice> Choices { get; init; } = [];
}

/// <summary>One legal named option.</summary>
/// <param name="Value">Machine value.</param>
/// <param name="Label">Plain display label.</param>
public sealed record GpuChoice(string Value, string Label);

/// <summary>A plain group of related driver controls.</summary>
public sealed record GpuSection
{
    /// <summary>Stable group identity.</summary>
    public required string SectionId { get; init; }

    /// <summary>Plain group title.</summary>
    public required string Title { get; init; }

    /// <summary>Optional plain description.</summary>
    public string? Description { get; init; }

    /// <summary>Adapter, display or other group hint.</summary>
    public GpuSectionKind Kind { get; init; } = GpuSectionKind.Adapter;

    /// <summary>Relative group order.</summary>
    public int SortOrder { get; init; }

    /// <summary>Declared categories within the group.</summary>
    public IReadOnlyList<GpuCategory> Categories { get; init; } = [];

    /// <summary>Read-only Windows monitor identity and current route for a physical output section.</summary>
    /// <remarks>Null for adapter-wide controls. Friendly titles and capability instance IDs are not monitor identities.</remarks>
    public GpuDisplayTarget? DisplayTarget { get; init; }
}

/// <summary>A physical driver output's observed Windows monitor identity and session route.</summary>
/// <remarks>Routes are refreshed on topology discovery. Persist the monitor identity, never GDI numbering or native output IDs.</remarks>
public sealed record GpuDisplayTarget
{
    /// <summary>Exact Windows monitor device-interface path, when resolved.</summary>
    public string? DevicePath { get; init; }

    /// <summary>EDID manufacturer, product and serial identity, when available.</summary>
    public string? EdidIdentity { get; init; }

    /// <summary>Current GDI source name, such as <c>\\.\DISPLAY1</c>, when the output is active.</summary>
    public string? SourceName { get; init; }

    /// <summary>Current CCD adapter LUID low part.</summary>
    public uint? AdapterLowPart { get; init; }

    /// <summary>Current CCD adapter LUID high part.</summary>
    public int? AdapterHighPart { get; init; }

    /// <summary>Current CCD target identifier.</summary>
    public uint? TargetId { get; init; }

    /// <summary>Vendor-native output identity for diagnostics; never a cross-vendor Windows route.</summary>
    public string? NativeOutputId { get; init; }
}

/// <summary>A plain subgroup within a driver section.</summary>
public sealed record GpuCategory
{
    /// <summary>Stable subgroup identity.</summary>
    public required string CategoryId { get; init; }

    /// <summary>Plain subgroup title.</summary>
    public required string Title { get; init; }

    /// <summary>Relative subgroup order.</summary>
    public int SortOrder { get; init; }
}

/// <summary>A complete replacement set of current driver controls.</summary>
public sealed record GpuDescriptorSet
{
    /// <summary>Complete plain grouping metadata.</summary>
    public IReadOnlyList<GpuSection> Sections { get; init; } = [];

    /// <summary>All currently supported controls.</summary>
    public IReadOnlyList<GpuDescriptor> Descriptors { get; init; } = [];
}
