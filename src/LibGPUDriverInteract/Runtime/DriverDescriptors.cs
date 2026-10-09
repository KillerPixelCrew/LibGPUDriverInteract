// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract.Runtime;

internal static class DriverDescriptors
{
    internal static GpuSection Section(string id, string title, bool display = false)
    {
        return new GpuSection
        {
            SectionId = id, Title = Label(title),
            Kind = display ? GpuSectionKind.Display : GpuSectionKind.Adapter
        };
    }

    internal static GpuDescriptor Toggle(string id, string instance, string label, string section,
        GpuProfileScope scope)
    {
        return Base(id, instance, label, section, scope, GpuValueKind.Boolean, GpuRole.Toggle);
    }

    internal static GpuDescriptor Choice(string id, string? instance, string label, string section,
        GpuProfileScope scope, IEnumerable<(string Id, string Label)> options)
    {
        return Base(id, instance, label, section, scope, GpuValueKind.Choice, GpuRole.Choice) with
        {
            Choices = options.Select(option => new GpuChoice(option.Id, Label(option.Label))).ToArray()
        };
    }

    internal static GpuDescriptor Range(string id, string instance, string label, string section,
        GpuProfileScope scope, int minimum, int maximum, int step)
    {
        return Base(id, instance, label, section, scope, GpuValueKind.Integer, GpuRole.Range) with
        {
            Minimum = minimum, Maximum = maximum, Step = step, Unit = GpuUnit.None
        };
    }

    internal static GpuDescriptor ReadOnly(string id, string instance, string label, string section,
        IEnumerable<(string Id, string Label)> options)
    {
        return Choice(id, instance, label, section, GpuProfileScope.GlobalOnly, options) with
        {
            Role = GpuRole.ReadOnly, SupportsWrite = false
        };
    }

    private static GpuDescriptor Base(string id, string? instance, string label, string section,
        GpuProfileScope scope, GpuValueKind kind, GpuRole role)
    {
        return new GpuDescriptor
        {
            CapabilityId = id, InstanceId = instance, SectionId = section, Role = role, ValueKind = kind,
            Label = Label(label), SupportsRead = true, SupportsWrite = true, ProfileScope = scope,
            Persistence = GpuPersistence.DevicePersistent,
            ApplyTiming = scope == GpuProfileScope.NativePerApplication
                ? GpuApplyTiming.NextApplicationStart
                : GpuApplyTiming.Immediate
        };
    }

    private static string Label(string text)
    {
        return new string(text.Where(character => !PlainText.IsUnsafe(character)).ToArray());
    }
}
