using LibGPUDriverInteract.Intel.Controls;
using LibGPUDriverInteract.Intel.Display;
using LibGPUDriverInteract.Intel.Graphics;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Intel.Profiles;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Intel;

/// <summary>
///     Everything one IGCL session offers: the controls, their sections and the per-application
///     writers, built when the session opens and again when the active displays change.
/// </summary>
internal sealed class IntelModel
{
    private const string FrameCategory = "frames";
    private const string QualityCategory = "quality";
    private const string SystemCategory = "system";
    private const string StatusCategory = "status";
    private const string RefreshCategory = "refresh";
    private const string PictureCategory = "picture";
    private const string ColorCategory = "color";
    private const string PowerCategory = "power";

    private static readonly GpuCategory[] AdapterCategories =
    [
        Category(FrameCategory, "Frame delivery", 0),
        Category(QualityCategory, "Image quality", 1),
        Category(SystemCategory, "Driver", 2),
        Category(StatusCategory, "Live status", 3)
    ];

    private static readonly GpuCategory[] DisplayCategories =
    [
        Category(RefreshCategory, "Refresh", 0),
        Category(PictureCategory, "Picture", 1),
        Category(ColorCategory, "Colour", 2),
        Category(PowerCategory, "Power savings", 3)
    ];

    private readonly Dictionary<nint, ArcSyncDisplay> _arcSync;
    private readonly Dictionary<string, INativeProfileTarget> _targets;

    private IntelModel(
        IReadOnlyList<IntelControl> controls,
        IReadOnlyList<GpuSection> sections,
        Dictionary<string, INativeProfileTarget> targets,
        Dictionary<nint, ArcSyncDisplay> arcSync)
    {
        Controls = controls;
        Sections = sections;
        _targets = targets;
        _arcSync = arcSync;
    }

    /// <summary>Every display with Arc Sync, the ones a refresh rate can be fixed on.</summary>
    public IReadOnlyCollection<ArcSyncDisplay> ArcSyncDisplays => _arcSync.Values;

    /// <summary>The Arc Sync state of one output.</summary>
    /// <param name="output">The output, from the session this model was built from.</param>
    /// <returns>The state, or null when the output has no Arc Sync.</returns>
    public ArcSyncDisplay? ArcSync(IgclOutput output)
    {
        return _arcSync.GetValueOrDefault(output.Handle);
    }

    /// <summary>Every published control, in publication order.</summary>
    public IReadOnlyList<IntelControl> Controls { get; }

    /// <summary>The declared sections.</summary>
    public IReadOnlyList<GpuSection> Sections { get; }

    public INativeProfileTarget? FindTarget(string capabilityId, string? instanceId)
    {
        return _targets.GetValueOrDefault(DriverIds.Key(capabilityId, instanceId));
    }

    /// <summary>Builds the model for an open session.</summary>
    /// <param name="session">The session.</param>
    /// <param name="memory">The shared-memory transport.</param>
    /// <param name="colors">The colour record.</param>
    /// <param name="adapterKeys">The display adapter class, enumerated once per session.</param>
    /// <param name="fixedRefresh">The session's fixed refresh-rate records.</param>
    /// <param name="log">Receives every decision.</param>
    /// <returns>The model; empty when the driver offers nothing.</returns>
    /// <remarks>
    ///     The adapters come first, then the built-in panel, then the other displays in order.
    /// </remarks>
    public static IntelModel Build(
        IgclSession session,
        IntelGraphicsMemoryTransport memory,
        ColorStore colors,
        IReadOnlyList<AdapterClassEntry> adapterKeys,
        FixedRefreshStore fixedRefresh,
        IDriverSink log)
    {
        List<IntelControl> controls = [];
        Dictionary<nint, ArcSyncDisplay> arcSync = [];
        List<GpuSection> sections = [];
        Dictionary<string, INativeProfileTarget> targets = new(StringComparer.Ordinal);
        var several = session.Adapters.Count > 1;
        var memoryAdapter = MemoryAdapter(session.Adapters, memory);
        foreach (var adapter in session.Adapters)
        {
            var instance = AdapterInstance(adapter);
            var sectionId = several ? $"graphics-{adapter.Index}" : "graphics";
            var title = several ? DriverDescriptors.Label(adapter.Name, "Graphics") : "Graphics";
            List<IntelControl> built = [];
            BuildAdapter(session, adapter, instance, sectionId, adapterKeys, built, targets, log);
            if (ReferenceEquals(adapter, memoryAdapter))
            {
                built.Add(new SharedMemoryControl(memory, instance, new Placement(sectionId, SystemCategory, 900)));
            }

            Section(sectionId, title, "Intel graphics driver settings", GpuSectionKind.Adapter, AdapterCategories,
                built, controls, sections);
        }

        var semanticVrrTaken = false;
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        // The built-in panel first, so the variable refresh role goes to it when it has variable refresh and
        // its section gets the lowest sort order among the displays.
        var topology = session.Outputs.Count == 0
            ? new DriverDisplayTargets([], [])
            : DisplayIdentityResolver.Capture(log);
        var ordered = session.Outputs
            .Select(output => (Output: output, Identity: DisplayIdentityResolver.Resolve(output, log, topology)))
            .OrderByDescending(entry => entry.Identity.Internal)
            .ToArray();
        foreach (var (output, identity) in ordered)
        {
            var name = identity.Name;
            for (var suffix = 2; !names.Add(name); suffix++)
            {
                name = DriverDescriptors.Label($"{identity.Name} {suffix}");
            }

            var sectionId = $"display-{ValueMapping.Fingerprint(identity.InstanceId)}";
            List<IntelControl> built = [];
            if (BuildDisplay(session, output, identity, sectionId, ref semanticVrrTaken, colors, fixedRefresh, built,
                    log) is { } display)
            {
                arcSync[output.Handle] = display;
            }

            Section(sectionId, name, "Intel display settings", GpuSectionKind.Display, DisplayCategories, built,
                controls, sections, identity.Target);
        }

        return new IntelModel(controls, sections, targets, arcSync);
    }

    /// <summary>The stable instance id of an adapter.</summary>
    /// <param name="adapter">The adapter.</param>
    /// <returns>
    ///     <c>pci-8086-&lt;device&gt;-&lt;bus&gt;-&lt;device&gt;-&lt;function&gt;</c>, or with the enumeration
    ///     index when the driver does not report the bus address. The LUID is not used: it changes every
    ///     boot.
    /// </returns>
    internal static string AdapterInstance(IgclAdapter adapter)
    {
        return adapter.HasBusAddress
            ? $"pci-{adapter.PciVendorId:x4}-{adapter.PciDeviceId:x4}-{adapter.Bus:x2}-{adapter.Device:x2}-{adapter.Function:x}"
            : $"pci-{adapter.PciVendorId:x4}-{adapter.PciDeviceId:x4}-{adapter.Index}";
    }

    /// <summary>Adds a section and its controls when it has any.</summary>
    private static void Section(
        string sectionId,
        string title,
        string description,
        GpuSectionKind kind,
        IReadOnlyList<GpuCategory> categories,
        List<IntelControl> built,
        List<IntelControl> controls,
        List<GpuSection> sections,
        GpuDisplayTarget? displayTarget = null)
    {
        if (built.Count == 0)
        {
            return;
        }

        controls.AddRange(built);
        sections.Add(new GpuSection
        {
            SectionId = sectionId,
            Title = title,
            Description = description,
            Kind = kind,
            SortOrder = sections.Count,
            Categories = categories,
            DisplayTarget = displayTarget
        });
    }

    private static IgclAdapter? MemoryAdapter(IReadOnlyList<IgclAdapter> adapters, IntelGraphicsMemoryTransport memory)
    {
        if (!memory.IsAvailable)
        {
            return null;
        }

        foreach (var adapter in adapters)
        {
            if (AdapterClassKey.Matches(memory.MatchingDeviceId, adapter.PciDeviceId))
            {
                return adapter;
            }
        }

        // The split belongs to the integrated GPU; a lone adapter is that one.
        return adapters.FirstOrDefault(adapter => adapter.Integrated) ?? (adapters.Count == 1 ? adapters[0] : null);
    }

    private static void BuildAdapter(
        IgclSession session,
        IgclAdapter adapter,
        string instance,
        string sectionId,
        IReadOnlyList<AdapterClassEntry> adapterKeys,
        List<IntelControl> controls,
        Dictionary<string, INativeProfileTarget> targets,
        IDriverSink log)
    {
        var registryKeys = ThreeDKeysLocator.Find(adapterKeys, adapter.PciDeviceId);
        var table = session.Read3dCapabilities(adapter);

        // Intel's sample (3D_Feature_Sample_App.cpp, CtlTestFrameGeneration) notes that a per-application
        // value applies only once feature 15 is set to per-application for that executable, so every
        // per-application write of this adapter carries the switch.
        PerApplicationSwitch? perApplication = null;
        foreach (var details in table)
        {
            if (details.FeatureType == ThreeDFeatureCatalog.GlobalOrPerApp
                && details.ValueType == (int)IgclValueType.Enum
                && details.PerAppSupport != 0)
            {
                var info = ThreeDFeatureCatalog.Describe(details.FeatureType);
                perApplication = new PerApplicationSwitch(
                    new ThreeDFeature(session, adapter, details, info, FeatureShape.Scalar),
                    registryKeys,
                    $"{instance}|{details.FeatureType}",
                    ThreeDFeatureCatalog.CapabilityId(info));
            }
        }

        foreach (var details in table)
        {
            var info = ThreeDFeatureCatalog.Describe(details.FeatureType);
            log.Info(
                "graphics",
                $"Adapter {adapter.Index} feature {details.FeatureType} ({info.Label}): type {details.ValueType}, "
                + $"per-app {details.PerAppSupport}, misc 0x{details.FeatureMiscSupport:x}, "
                + $"mask 0x{details.Value.EnumSupportedTypes:x}.");
            if (ThreeDFeatureCatalog.SkipReason(details.FeatureType) is { } reason)
            {
                log.Info("graphics", $"{info.Label} is not published: {reason}.");
                continue;
            }

            if (Shape(details) is not { } shape)
            {
                log.Info("graphics", $"{info.Label} reports value type {details.ValueType}, which this package "
                                     + "cannot read without guessing; not published.");
                continue;
            }

            var category = details.FeatureType switch
            {
                ThreeDFeatureCatalog.FramePacing or ThreeDFeatureCatalog.EnduranceGaming
                    or ThreeDFeatureCatalog.FrameLimit or ThreeDFeatureCatalog.GamingFlipModes
                    or ThreeDFeatureCatalog.AdaptiveSyncPlus or ThreeDFeatureCatalog.LowLatency
                    or ThreeDFeatureCatalog.FrameGeneration or ThreeDFeatureCatalog.VrrWindowedBlt => FrameCategory,
                ThreeDFeatureCatalog.Anisotropic or ThreeDFeatureCatalog.Cmaa
                    or ThreeDFeatureCatalog.TextureFilteringQuality or ThreeDFeatureCatalog.AdaptiveTessellation
                    or ThreeDFeatureCatalog.SharpeningFilter or ThreeDFeatureCatalog.Msaa => QualityCategory,
                ThreeDFeatureCatalog.LiveState => StatusCategory,
                _ => SystemCategory
            };
            var placement = new Placement(sectionId, category, details.FeatureType * 10);
            foreach (var feature in Features(session, adapter, details, info, shape, log))
            {
                var built = ThreeDFeatureControl.Build(feature, instance, placement, log);
                controls.AddRange(built);
                placement = placement.Plus(1);
                var writable = built.Where(control => control.Descriptor.SupportsWrite).ToArray();
                if (!feature.PerApplication || writable.Length == 0)
                {
                    continue;
                }

                var group = shape == FeatureShape.AppProfile
                    ? $"{instance}|{details.FeatureType}|{feature.TierType}"
                    : $"{instance}|{details.FeatureType}";
                NativeFeatureTarget target = new(feature, writable, registryKeys, group, perApplication);
                foreach (var control in writable)
                {
                    targets[control.Key] = target;
                }
            }
        }

        if (RetroScalingControl.TryCreate(session, adapter, instance, new Placement(sectionId, SystemCategory, 800))
            is { } retro)
        {
            controls.Add(retro);
        }
    }

    /// <summary>The shape a feature's value travels in, or null when this package cannot carry it.</summary>
    /// <remarks>
    ///     The game profiles and the live state are defined only as structures; any other reported type is
    ///     not one this package can read without guessing, and a guessed type is what crashed the driver.
    /// </remarks>
    private static FeatureShape? Shape(Ctl3dFeatureDetails details)
    {
        if (details.ValueType == (int)IgclValueType.Custom)
        {
            return details.FeatureType switch
            {
                ThreeDFeatureCatalog.EnduranceGaming => FeatureShape.Endurance,
                ThreeDFeatureCatalog.AdaptiveSyncPlus => FeatureShape.AdaptiveSync,
                ThreeDFeatureCatalog.AppProfiles => FeatureShape.AppProfile,
                ThreeDFeatureCatalog.LiveState => FeatureShape.LiveState,
                _ => null
            };
        }

        return details.FeatureType is ThreeDFeatureCatalog.AppProfiles or ThreeDFeatureCatalog.LiveState
               || !ThreeDFeatureCatalog.IsScalar(details.ValueType)
            ? null
            : FeatureShape.Scalar;
    }

    /// <summary>The driver values one reported feature stands for, with their capability structures read.</summary>
    /// <remarks>
    ///     Every feature is one value, except the game profiles (feature 11): each tier type in
    ///     <c>ctl_3d_app_profiles_caps_t.SupportedTierTypes</c> is its own value, asked for through the
    ///     structure's <c>TierType</c> input, so each becomes its own feature with its own supported tiers.
    /// </remarks>
    private static IEnumerable<ThreeDFeature> Features(
        IgclSession session,
        IgclAdapter adapter,
        Ctl3dFeatureDetails details,
        ThreeDFeatureInfo info,
        FeatureShape shape,
        IDriverSink log)
    {
        switch (shape)
        {
            case FeatureShape.Endurance:
            {
                ThreeDFeature feature = new(session, adapter, details, info, shape);
                if (session.TryReadCustomCaps<CtlEnduranceGamingCaps>(adapter, details, out var endurance))
                {
                    feature.EnduranceCaps = endurance;
                }

                yield return feature;
                yield break;
            }
            case FeatureShape.AdaptiveSync:
            {
                ThreeDFeature feature = new(session, adapter, details, info, shape);
                if (session.TryReadCustomCaps<CtlAdaptiveSyncCaps>(adapter, details, out var adaptive))
                {
                    feature.AdaptiveSyncCaps = adaptive;
                }

                yield return feature;
                yield break;
            }
            case FeatureShape.AppProfile:
            {
                var tierTypes = session.TryReadCustomCaps<Ctl3dAppProfilesCaps>(adapter, details, out var caps)
                    ? caps.SupportedTierTypes
                    : 0;
                log.Info("graphics", $"{info.Label}: tier types 0x{tierTypes:x}.");
                foreach (var type in ThreeDFeatureCatalog.TierTypes(tierTypes))
                {
                    ThreeDFeature feature = new(session, adapter, details, info, shape) { TierType = type.Value };
                    var result = feature.ReadAppProfile(null, out var profile);
                    if (result is not (IgclResult.Success or IgclResult.DataNotFound))
                    {
                        log.Info("graphics",
                            $"{type.Id} is not published: the driver answered {IgclResult.Describe(result)}.");
                        continue;
                    }

                    feature.SupportedTierProfiles = profile.SupportedTierProfiles;
                    feature.DefaultEnabledTierProfiles = profile.DefaultEnabledTierProfiles;
                    log.Info(
                        "graphics",
                        $"{type.Id}: supported 0x{profile.SupportedTierProfiles:x}, default "
                        + $"0x{profile.DefaultEnabledTierProfiles:x}, enabled 0x{profile.EnabledTierProfiles:x} "
                        + $"({IgclResult.Describe(result)}).");
                    yield return feature;
                }

                yield break;
            }
            default:
                yield return new ThreeDFeature(session, adapter, details, info, shape);
                yield break;
        }
    }

    private static ArcSyncDisplay? BuildDisplay(
        IgclSession session,
        IgclOutput output,
        DisplayIdentity identity,
        string sectionId,
        ref bool semanticVrrTaken,
        ColorStore colors,
        FixedRefreshStore fixedRefresh,
        List<IntelControl> controls,
        IDriverSink log)
    {
        var instance = identity.InstanceId;
        var arcSync = ArcSyncDisplay.TryCreate(session, output, log, identity.Name, fixedRefresh, instance);
        if (arcSync is not null)
        {
            // One display carries the variable refresh role, the one Steam's own toggle follows: the
            // built-in panel when it has variable refresh, otherwise the first display that does.
            var semantic = !semanticVrrTaken;
            semanticVrrTaken = true;
            var refresh = new Placement(sectionId, RefreshCategory, 0);
            controls.Add(new VariableRefreshControl(arcSync, instance, semantic, refresh));
            controls.Add(new ArcSyncProfileControl(arcSync, instance, refresh.Plus(1)));
            controls.AddRange(ArcSyncParameterControl.Build(arcSync, instance, refresh.Plus(2)));
        }

        var picture = new Placement(sectionId, PictureCategory, 0);
        controls.AddRange(ScalingControls.Build(session, output, instance, picture));
        controls.AddRange(SharpnessControls.Build(session, output, instance, picture.Plus(10)));
        if (WireFormatControl.TryCreate(session, output, instance, picture.Plus(20)) is { } wire)
        {
            controls.Add(wire);
        }

        controls.AddRange(DisplaySettingControls.Build(session, output, instance, picture.Plus(30)));
        if (ColorPipeline.TryCreate(session, output, instance, colors, log) is { } pipeline)
        {
            controls.AddRange(ColorControl.Build(pipeline, instance, new Placement(sectionId, ColorCategory, 0)));
        }

        var power = PowerSavingControls.SupportedFeatures(session, output);
        if (power == 0)
        {
            return arcSync;
        }

        var powerPlacement = new Placement(sectionId, PowerCategory, 0);
        controls.AddRange(PowerSavingControls.Build(session, output, power, instance, powerPlacement, log));
        if ((power & PowerSavingControls.FeatureLace) != 0)
        {
            controls.AddRange(LaceControls.Build(session, output, instance, powerPlacement.Plus(50)));
        }

        return arcSync;
    }

    private static GpuCategory Category(string id, string title, int order)
    {
        return new GpuCategory
        {
            CategoryId = id,
            Title = title,
            SortOrder = order
        };
    }

    /// <summary>The per-application writer of one 3D feature.</summary>
    private sealed class NativeFeatureTarget : INativeProfileTarget
    {
        private readonly Dictionary<string, ThreeDFeatureControl> _controls;
        private readonly ThreeDFeature _feature;

        public NativeFeatureTarget(
            ThreeDFeature feature,
            IReadOnlyList<ThreeDFeatureControl> controls,
            IReadOnlyList<string> registryKeys,
            string groupKey,
            INativeApplicationSwitch? applicationSwitch)
        {
            _feature = feature;
            _controls = controls.ToDictionary(control => control.CapabilityId, StringComparer.Ordinal);
            RegistryKeys = registryKeys;
            GroupKey = groupKey;
            ApplicationSwitch = applicationSwitch;
        }

        public string GroupKey { get; }

        public IReadOnlyList<string> RegistryKeys { get; }

        public INativeApplicationSwitch? ApplicationSwitch { get; }

        public string? WriteForApplication(
            string executable,
            IReadOnlyList<(string CapabilityId, GpuValue Value)> values, WriteAdmission admission)
        {
            // Fields no override names keep the global value, which is what the game would get anyway.
            var raw = _feature.Read(null, out var global) == IgclResult.Success ? global : _feature.DefaultValue();
            foreach (var (capabilityId, value) in values)
            {
                if (!_controls.TryGetValue(capabilityId, out var control) || !control.Validate(value, out _))
                {
                    return $"The value for {capabilityId} is not one the driver offers.";
                }

                raw = control.Encode(raw, value);
            }

            var result = _feature.Write(executable, raw, admission);
            return result == IgclResult.Success
                ? null
                : $"The driver answered {IgclResult.Describe(result)} to {_feature.Info.Label} for {executable}.";
        }
    }

    /// <summary>
    ///     Feature 15, <c>CTL_3D_FEATURE_GLOBAL_OR_PER_APP</c>, of one adapter: turns a game's own values
    ///     on in the driver.
    /// </summary>
    /// <remarks>
    ///     <c>igcl_api.h</c> lines 1838-1845 document the values and Intel's sample writes
    ///     <c>CTL_3D_GLOBAL_OR_PER_APP_TYPES_PER_APP</c> with the executable's name, as an enum. Its note
    ///     on the frame generation test says a per-application value applies only once that is set.
    /// </remarks>
    private sealed class PerApplicationSwitch : INativeApplicationSwitch
    {
        private readonly ThreeDFeature _feature;

        public PerApplicationSwitch(
            ThreeDFeature feature,
            IReadOnlyList<string> registryKeys,
            string groupKey,
            string recordId)
        {
            _feature = feature;
            RegistryKeys = registryKeys;
            GroupKey = groupKey;
            RecordId = recordId;
        }

        public string RecordId { get; }

        public string GroupKey { get; }

        public IReadOnlyList<string> RegistryKeys { get; }

        public string? EnableFor(string executable, WriteAdmission admission)
        {
            RawFeatureValue raw = default;
            raw.Scalar.EnumValue = ThreeDFeatureCatalog.PerApplicationSettings;
            var result = _feature.Write(executable, raw, admission);
            return result == IgclResult.Success
                ? null
                : $"The driver answered {IgclResult.Describe(result)} to per-application settings for {executable}.";
        }
    }
}
