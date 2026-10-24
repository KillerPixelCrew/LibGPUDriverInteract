// SPDX-License-Identifier: MIT

using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Amd;

internal sealed partial class AdlxSession
{
    private void AddGraphics(nint gpu, string instance, string section,
        List<DriverControl> controls)
    {
        string? service = null;
        Feature(service, 3, "anti-lag", feature =>
        {
            Toggle(feature, "anti-lag", "Radeon Anti-Lag", 4, 5);
            if (feature.HasInterface("IADLX3DAntiLag1"))
            {
                var next = feature with { FeatureInterface = "IADLX3DAntiLag1" };
                Choice(next, "anti-lag-level", "Anti-Lag level", 6, 7,
                    [(0, "Anti-Lag"), (1, "Anti-Lag Next")]);
            }
        });
        Feature(service, 5, "boost", feature =>
        {
            Toggle(feature, "boost", "Radeon Boost", 4, 7);
            Range(feature, "boost-resolution", "Boost minimum resolution", 5, 6, 8);
        });
        Feature(service, 6, "image-sharpening", feature =>
        {
            Toggle(feature, "image-sharpening", "Radeon Image Sharpening", 4, 7);
            Range(feature, "sharpness", "Image sharpening strength", 5, 6, 8);
        });
        Feature(service, 7, "enhanced-sync", feature => Toggle(feature, "enhanced-sync", "Enhanced Sync", 4, 5));
        Feature(service, 8, "vertical-sync", feature => Choice(feature, "vertical-sync", "Wait for vertical refresh", 5,
            6,
            [
                (0, "Always off"), (1, "Off unless application specifies"), (2, "On unless application specifies"),
                (3, "Always on")
            ]));
        Feature(service, 10, "anti-aliasing", feature =>
        {
            Choice(feature, "anti-aliasing-mode", "Anti-aliasing mode", 4, 7,
            [
                (0, "Application settings"), (1, "Enhance application settings"), (2, "Override application settings")
            ]);
            Choice(feature, "anti-aliasing-level", "Anti-aliasing level", 5, 8,
                [(2, "2x"), (3, "2x EQ"), (4, "4x"), (5, "4x EQ"), (8, "8x"), (9, "8x EQ")]);
            Choice(feature, "anti-aliasing-method", "Anti-aliasing method", 6, 9,
                [(0, "Multisampling"), (1, "Adaptive multisampling"), (2, "Supersampling")]);
        });
        Feature(service, 11, "morphological-aa",
            feature => Toggle(feature, "morphological-aa", "Morphological anti-aliasing", 4, 5));
        Feature(service, 12, "anisotropic-filtering", feature =>
        {
            Toggle(feature, "anisotropic-filtering", "Anisotropic filtering", 4, 6);
            Choice(feature, "anisotropic-level", "Anisotropic filtering level", 5, 7,
                [(2, "2x"), (4, "4x"), (8, "8x"), (16, "16x")]);
        });
        Feature(service, 13, "tessellation", feature =>
        {
            Choice(feature, "tessellation-mode", "Tessellation mode", 4, 6,
                [(0, "AMD optimized"), (1, "Application settings"), (2, "Override application settings")]);
            Choice(feature, "tessellation-level", "Tessellation maximum level", 5, 7,
                [(1, "Off"), (2, "2x"), (4, "4x"), (6, "6x"), (8, "8x"), (16, "16x"), (32, "32x"), (64, "64x")]);
        });
        Feature(service, 14, "rsr", feature =>
        {
            Toggle(feature, "rsr", "Radeon Super Resolution", 4, 5);
            Range(feature, "rsr-sharpness", "RSR sharpness", 6, 7, 8);
        });
        Feature(service, 15, "reset-shader-cache", feature =>
        {
            var descriptor = DriverDescriptors.Toggle("graphics.reset-shader-cache", instance, "Reset shader cache",
                    section,
                    GpuProfileScope.GlobalOnly) with
                {
                    Role = GpuRole.Action, ValueKind = GpuValueKind.None, SupportsRead = false,
                    SupportsWrite = false, Persistence = GpuPersistence.Volatile
                };
            controls.Add(new AdlxControl(descriptor, GpuValue.None, (_, admission) =>
            {
                feature.Use(target =>
                {
                    RequireSupported(target);
                    AdlxNative.Call(target.Pointer, 4, admission);
                    return true;
                });
            }));
        });
        Feature("IADLX3DSettingsServices1", 17, "afmf", feature =>
        {
            Toggle(feature, "afmf", "AMD Fluid Motion Frames", 4, 5);
            if (feature.HasInterface("IADLX3DAMDFluidMotionFrames1"))
            {
                var advanced = feature with { FeatureInterface = "IADLX3DAMDFluidMotionFrames1" };
                if (advanced.Use(target => AdlxNative.Boolean(target.Pointer, 6)))
                {
                    Choice(advanced, "afmf-algorithm", "AFMF algorithm", 7, 8,
                        [(0, "Auto"), (1, "Enhanced (AFMF 2.1)"), (2, "Standard")]);
                    Choice(advanced, "afmf-search-mode", "AFMF search mode", 9, 10,
                        [(0, "Auto"), (1, "Standard"), (2, "High")]);
                    Choice(advanced, "afmf-performance-mode", "AFMF performance mode", 11, 12,
                        [(0, "Auto"), (1, "Quality"), (2, "Performance")]);
                    Choice(advanced, "afmf-fast-motion", "AFMF fast motion response", 13, 14,
                        [(0, "Repeat frames"), (1, "Blend frames")]);
                }
            }
        });

        Feature("IADLX3DSettingsServices2", 18, "desktop-sharpening", feature =>
            Toggle(feature, "desktop-sharpening", "Desktop image sharpening", 4, 5,
                scope: GpuProfileScope.GlobalOnly));

        Feature("IADLX3DSettingsServices3", 19, "fsr-upgrade", feature =>
            Toggle(feature, "fsr-upgrade", "FidelityFX upscaling upgrade", 4, 5));
        Feature("IADLX3DSettingsServices3", 20, "fsr-framegen-upgrade", feature =>
        {
            Toggle(feature, "fsr-framegen-upgrade", "FidelityFX frame generation upgrade", 6, 7, 5);
            var values = feature.Use(target =>
            {
                using var list = AdlxNative.Interface(target.Pointer, 3);
                var items = AdlxNative.Items(list.Pointer);
                try
                {
                    return items.Select(item => AdlxNative.Integer(item.Pointer, 3)).Distinct()
                        .Where(ratio => ratio == 1).Select(ratio => (ratio, "2x")).ToArray();
                }
                finally
                {
                    Release(items);
                }
            });
            if (values.Length > 0)
            {
                Choice(feature, "fsr-framegen-ratio", "FidelityFX frame generation ratio", 4, 8,
                    values, 5);
            }
        }, 5);

        // Chill and FRTC are deliberately absent: the consumer remains the frame-limit owner.
        return;

        void Feature(string? owner, int slot, string id, Action<AdlxGraphicsFeature> build, int supported = 3)
        {
            Try(instance + "/" + id, () =>
            {
                var feature = new AdlxGraphicsFeature(_system, gpu, slot, owner);
                feature.TryUse(target =>
                {
                    if (AdlxNative.Boolean(target.Pointer, supported))
                    {
                        build(feature);
                    }

                    return true;
                }, out _);
            });
        }

        void Toggle(AdlxGraphicsFeature feature, string id, string label, int read, int write, int supported = 3,
            GpuProfileScope scope = GpuProfileScope.Switched)
        {
            controls.Add(new AdlxControl(DriverDescriptors.Toggle("graphics." + id, instance, label, section, scope),
                () => feature.Use(target =>
                {
                    RequireSupported(target, supported);
                    return GpuValue.Boolean(AdlxNative.Boolean(target.Pointer, read));
                }),
                (value, admission) => feature.Use(target =>
                {
                    RequireSupported(target, supported);
                    AdlxNative.SetBoolean(target.Pointer, write, value.BooleanValue == true, admission);
                    return true;
                })));
        }

        void Choice(AdlxGraphicsFeature feature, string id, string label, int read, int write,
            IReadOnlyList<(int Value, string Label)> values, int supported = 3)
        {
            controls.Add(new AdlxControl(DriverDescriptors.Choice("graphics." + id, instance, label, section,
                    GpuProfileScope.Switched,
                    values.Select(value => (AmdValue.Encode(value.Value), value.Label))),
                () => feature.Use(target =>
                {
                    RequireSupported(target, supported);
                    return GpuValue.Choice(AmdValue.Encode(AdlxNative.Integer(target.Pointer, read)));
                }),
                (value, admission) => feature.Use(target =>
                {
                    RequireSupported(target, supported);
                    AdlxNative.SetInteger(target.Pointer, write, AmdValue.Decode(value), admission);
                    return true;
                })));
        }

        void Range(AdlxGraphicsFeature feature, string id, string label, int range, int read, int write)
        {
            var bounds = feature.Use(target => AdlxNative.Range(target.Pointer, range));
            controls.Add(new AdlxControl(DriverDescriptors.Range("graphics." + id, instance, label, section,
                        GpuProfileScope.Switched, bounds.Minimum, bounds.Maximum, bounds.Step) with
                    {
                        Unit = GpuUnit.Percent
                    },
                () => feature.Use(target =>
                {
                    RequireSupported(target);
                    return GpuValue.Integer(AdlxNative.Integer(target.Pointer, read));
                }),
                (value, admission) => feature.Use(target =>
                {
                    RequireSupported(target);
                    var current = AdlxNative.Range(target.Pointer, range);
                    if (!Within(value.IntegerValue!.Value, current))
                    {
                        throw new DriverFailure("This value is outside the AMD driver's current range.");
                    }

                    AdlxNative.SetInteger(target.Pointer, write, value.IntegerValue.Value, admission);
                    return true;
                })));
        }
    }

    private static void RequireSupported(AdlxObject feature, int supported = 3)
    {
        if (!AdlxNative.Boolean(feature.Pointer, supported))
        {
            throw new DriverFailure("This AMD feature is no longer supported on the selected target.");
        }
    }

    internal static bool Within(int value, AdlxRange range)
    {
        return value >= range.Minimum && value <= range.Maximum
                                      && ((long)value - range.Minimum) % range.Step == 0;
    }
}
