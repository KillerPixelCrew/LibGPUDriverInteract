// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Runtime.InteropServices;
using LibGPUDriverInteract.Runtime;
using WindowsDeviceControl;

namespace LibGPUDriverInteract.Amd;

internal sealed unsafe partial class AdlxSession : IDriverSession
{
    private readonly AdlxObject? _displays;
    private readonly AdlxObject? _displays3;
    private readonly long _epoch;
    private readonly Action<string, string> _report;
    private readonly List<AdlxObject> _services = [];
    private AdlDitherApi? _dither;
    private bool _initialized;
    private nint _library;
    private nint _mapping;
    private List<AdlxObject> _objects = [];
    private nint _system;

    internal AdlxSession(Action<string, string> report, Action<string>? info = null)
    {
        _report = report;
        _library = SystemLibrary.Load("amdadlx64.dll");
        try
        {
            ulong runtimeVersion = 0;
            AdlxNative.Check(
                ((delegate* unmanaged[Cdecl]<ulong*, int>)NativeLibrary.GetExport(_library, "ADLXQueryFullVersion"))
                (&runtimeVersion), "QueryFullVersion");
            var version = Math.Min(AdlxNative.HeaderVersion, runtimeVersion);
            nint system = 0;
            nint mapping = 0;
            int status;
            string entry;
            if (NativeLibrary.TryGetExport(_library, "ADLXInitialize2", out var initialize))
            {
                entry = "ADLXInitialize2";
                status = ((delegate* unmanaged[Cdecl]<ulong, nint*, nint*, int>)initialize)(version, &system, &mapping);
            }
            else
            {
                entry = "ADLXInitialize";
                status = ((delegate* unmanaged[Cdecl]<ulong, nint*, int>)NativeLibrary.GetExport(_library, entry))
                    (version, &system);
            }

            // An already-initialized ADLX owner must not be terminated by this package.
            if (status != 0)
            {
                throw new DriverFailure($"ADLX initialization returned {status}.");
            }

            // A pasted log then names the driver's ADLX build next to any result it returns.
            info?.Invoke(
                $"ADLX runtime {AdlxNative.VersionText(runtimeVersion)} initialized as {AdlxNative.VersionText(version)} via {entry}.");
            _initialized = true;
            _epoch = AdlxNative.Epoch;
            _system = system != 0 ? system : throw new DriverFailure("ADLX supplied no system service.");
            _mapping = mapping;
            _displays = Service(() => AdlxNative.Interface(_system, 3), "displays");

            if (_displays is not null)
            {
                _displays3 = Service(() => AdlxNative.Query(_displays.Pointer, "IADLXDisplayServices3"), "displays3");
            }

            if (_mapping != 0)
            {
                Try("dithering", () => _dither = new AdlDitherApi());
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>ADLX reads each control live; there is nothing to load per pass.</summary>
    public void BeginPass()
    {
    }

    /// <inheritdoc />
    public DriverModel Discover()
    {
        var objects = new List<AdlxObject>();
        var sections = new List<GpuSection>();
        var controls = new List<DriverControl>();
        try
        {
            using var gpuList = AdlxNative.Interface(_system, 1);
            var gpus = AdlxNative.Items(gpuList.Pointer);
            objects.AddRange(gpus);
            if (gpus.Count == 0)
            {
                throw new DriverFailure("No AMD GPU is available.", lost: true);
            }

            foreach (var gpu in gpus)
            {
                Try("gpu", () =>
                {
                    var pnp = AdlxNative.Text(gpu.Pointer, 9);
                    if (!pnp.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    var instance = "pci-" + DriverIds.Hash(pnp);
                    var section = "amd." + instance;
                    var before = controls.Count;
                        AddGraphics(gpu.Pointer, instance, section, controls);
                    if (controls.Count > before)
                    {
                        sections.Add(DriverDescriptors.Section(section, AdlxNative.Text(gpu.Pointer, 7)));
                    }
                });
            }

            if (_displays is not null)
            {
                var routes = DriverDisplayTargets.Capture();
                using var list = AdlxNative.Interface(_displays.Pointer, 4);
                var displays = AdlxNative.Items(list.Pointer);
                objects.AddRange(displays);
                foreach (var display in displays)
                {
                    Try("display", () =>
                    {
                        using var gpu = AdlxNative.Interface(display.Pointer, 12);
                        var pnp = AdlxNative.Text(gpu.Pointer, 9);
                        if (!pnp.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        var id = AdlxNative.Size(display.Pointer, 13);
                        var edid = AdlxNative.Text(display.Pointer, 7);
                        var instance = (AdlxNative.Integer(display.Pointer, 5) == 15 ? "internal-" : "output-")
                                       + DriverIds.Hash(pnp + "/" + id + "/" + edid);
                        var section = "amd." + instance;
                        var before = controls.Count;
                        AddDisplay(display.Pointer, instance, section, objects, controls);
                        if (controls.Count > before)
                        {
                            string? sourceName = null;
                            Try(instance + "/windows-route", () =>
                            {
                                if (_mapping != 0 && _dither is not null)
                                {
                                    var mapped = AdlIds(display.Pointer);
                                    sourceName = _dither.SourceName(mapped.Adapter, mapped.Display);
                                }
                            });
                            var serialIdentity = EdidIdentity(edid);
                            var target = routes.BySource(sourceName) ?? routes.ByEdid(serialIdentity);
                            sections.Add(DriverDescriptors.Section(section,
                                "AMD " + AdlxNative.Text(display.Pointer, 6), true) with
                            {
                                DisplayTarget = routes.Describe(target, $"{pnp}/{id}", sourceName, serialIdentity)
                            });
                        }
                    });
                }
            }

            var previous = _objects;
            _objects = objects;
            Release(previous);
            return new DriverModel(sections, controls);
        }
        catch
        {
            Release(objects);
            throw;
        }
    }

    internal static string? EdidIdentity(string edid)
    {
        var hex = new string(edid.Where(character => !char.IsWhiteSpace(character)).ToArray());
        try
        {
            return DisplayEdid.ParseIdentity(Convert.FromHexString(hex));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public GpuProfileSyncResult Sync(GpuProfileSync sync, WriteAdmission admission,
        CancellationToken token)
    {
        return SyncProfiles(sync, token);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_library == 0)
        {
            return;
        }

        try
        {
            Release(_objects);
            _dither?.Dispose();
            _dither = null;
            Release(_services);
            if (_initialized && _epoch == AdlxNative.Epoch)
            {
                ((delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(_library, "ADLXTerminate"))();
                _initialized = false;
            }
        }
        finally
        {
            NativeLibrary.Free(_library);
            _library = 0;
            _system = _mapping = 0;
        }
    }

    internal static GpuProfileSyncResult SyncProfiles(GpuProfileSync sync, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // ADLX controls are GPU-wide. The consumer applies/restores switched values for the active game.
        return sync.Profiles.Any(profile => profile.Values.Count > 0)
            ? DriverRuntime.SyncUnavailable(sync,
                "ADLX controls are GPU-wide; native per-application profiles are not supported.")
            : new GpuProfileSyncResult(0, 0, []);
    }

    private AdlxObject? Service(Func<AdlxObject?> get, string key)
    {
        AdlxObject? result = null;
        Try(key, () =>
        {
            result = get();
            if (result is not null)
            {
                _services.Add(result);
            }
        });
        return result;
    }

    private void Try(string key, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is not DriverFailure { Lost: true })
        {
            _report(key, error.Message);
        }
    }

    private static void Release(List<AdlxObject> objects)
    {
        for (var index = objects.Count - 1; index >= 0; index--)
        {
            objects[index].Dispose();
        }

        objects.Clear();
    }
}

internal sealed class AdlxControl(
    GpuDescriptor descriptor,
    Func<GpuValue> read,
    Action<GpuValue, WriteAdmission> write)
    : DriverControl(descriptor)
{
    internal override GpuValue Read()
    {
        return read();
    }

    internal override void Write(GpuValue value, WriteAdmission admission)
    {
        write(value, admission);
    }
}

internal static class AmdValue
{
    internal static string Encode(int value)
    {
        return "v" + value.ToString(CultureInfo.InvariantCulture);
    }

    internal static int Decode(GpuValue value)
    {
        return int.Parse(value.ChoiceValue!.AsSpan(1), CultureInfo.InvariantCulture);
    }
}
