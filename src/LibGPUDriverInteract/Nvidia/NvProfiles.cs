// SPDX-License-Identifier: MIT

using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Nvidia;

internal interface INvProfiles
{
    void Load();
    void Save(WriteAdmission admission);
    nint GlobalProfile();
    nint Application(string executable, bool create, string profileName, WriteAdmission admission);
    nint FindProfile(string name);
    string ProfileName(nint profile);
    IReadOnlyList<NvNamedProfile> Profiles();
    void DeleteProfile(nint profile, WriteAdmission admission);
    (uint Value, bool Explicit) Get(nint profile, uint setting);
    void Set(nint profile, uint setting, uint value, WriteAdmission admission);
    void Inherit(nint profile, uint setting, WriteAdmission admission);
}

internal sealed record NvNamedProfile(nint Handle, string Name);

internal sealed record NvOwnedSetting(
    string Profile,
    uint Setting,
    string Executable,
    string ProfileId,
    uint Original,
    bool OriginalExplicit,
    uint Written)
{
    internal string Key => Identity(Profile, Setting);

    internal static string Identity(string profile, uint setting)
    {
        return profile.ToUpperInvariant() + "/" + setting.ToString("X8");
    }
}

internal sealed record NvProfileJournal(IReadOnlyList<NvOwnedSetting> Entries, bool LegacyProfilesRemoved = false);

/// <summary>DRS owns application values. The journal owns only this library's individual changes.</summary>
/// <remarks>
///     A write is dispatched once SaveSettings returns and failed when SetSetting, DeleteProfileSetting or
///     SaveSettings throws; nothing waits for the driver to report a value. An unreadable journal leaves every
///     per-application value alone and fails each sync; the global controls keep working.
/// </remarks>
internal sealed class NvProfiles
{
    private readonly INvProfiles _api;
    private readonly Dictionary<string, NvOwnedSetting> _owned;
    private readonly string _path;
    private readonly string? _unreadable;
    private bool _legacyProfilesRemoved;

    internal NvProfiles(INvProfiles api, string stateDirectory, Action<string, string> report)
    {
        _api = api;
        _path = Path.Combine(stateDirectory, "nvidia-profiles.v1.json");
        try
        {
            var journal = DriverStateFile.Read(_path, new NvProfileJournal([]));
            if (journal.Entries is null || journal.Entries.Any(entry => entry is null
                                                                        || entry.Profile is null ||
                                                                        entry.Executable is null))
            {
                throw new DriverFailure("The NVIDIA per-application record has incomplete ownership entries.");
            }

            _owned = journal.Entries.ToDictionary(entry => entry.Key);
            _legacyProfilesRemoved = journal.LegacyProfilesRemoved;
        }
        catch (Exception failure) when (failure is DriverFailure or ArgumentException)
        {
            // The record is never replaced: without it no owned value could be restored. A journal that
            // names one setting twice is as corrupt as one that does not parse.
            _owned = [];
            _unreadable = failure.Message;
            report("profiles", failure.Message + " Per-application NVIDIA settings are left unchanged.");
        }
    }

    internal void Initialize(WriteAdmission admission, CancellationToken token)
    {
        // Initialization belongs to an explicitly opened native owner, independently of offered controls.
        // A corrupt ownership record is retained and reported by the constructor; global controls still work.
        if (_unreadable is not null)
        {
            return;
        }

        _api.Load();
        CleanupLegacyProfiles(admission, token);
    }

    internal GpuProfileSyncResult Sync(GpuProfileSync sync,
        IReadOnlyDictionary<string, NvSettingControl> controls, WriteAdmission admission, CancellationToken token)
    {
        if (_unreadable is not null)
        {
            var detail = $"The NVIDIA per-application record could not be read ({_unreadable}); nothing was changed.";
            var unreadableFailures = sync.Profiles.SelectMany(game => game.Executables
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(executable => new GpuProfileFailure(game.ProfileId, executable, "", detail))).ToArray();
            return new GpuProfileSyncResult(0, 0, unreadableFailures.Length > 0
                ? unreadableFailures
                : [new GpuProfileFailure("driver", "", "", detail)]);
        }

        var failures = new List<GpuProfileFailure>();
        var requests = new Dictionary<string, List<Request>>(StringComparer.Ordinal);
        _api.Load();
        foreach (var game in sync.Profiles)
        {
            foreach (var executable in game.Executables.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string? name = null;
                string? lookupError = null;
                foreach (var value in game.Values)
                {
                    token.ThrowIfCancellationRequested();
                    if (!controls.TryGetValue(value.CapabilityId, out var control) ||
                        value.InstanceId != control.Descriptor.InstanceId
                        || control.Descriptor.ProfileScope != GpuProfileScope.NativePerApplication ||
                        !control.Accepts(value.Value))
                    {
                        failures.Add(new GpuProfileFailure(game.ProfileId, executable, value.CapabilityId,
                            "This NVIDIA setting or value is not supported for application profiles."));
                        continue;
                    }

                    // The executable's profile is looked up once, at its first accepted value.
                    if (name is null && lookupError is null)
                    {
                        try
                        {
                            var owned = OwnedProfile(executable);
                            var handle = _api.Application(executable, false, owned, admission);
                            name = handle == 0 ? owned : _api.ProfileName(handle);
                        }
                        catch (Exception error) when (error is not OperationCanceledException &&
                                                      error is not DriverFailure { Lost: true })
                        {
                            lookupError = error.Message;
                        }
                    }

                    if (lookupError is not null)
                    {
                        failures.Add(new GpuProfileFailure(game.ProfileId, executable, value.CapabilityId,
                            lookupError));
                        continue;
                    }

                    var key = NvOwnedSetting.Identity(name!, control.Setting.Id);
                    if (!requests.TryGetValue(key, out var list))
                    {
                        requests[key] = list = [];
                    }

                    list.Add(new Request(game.ProfileId, executable, name!, control.Setting.Id,
                        value.CapabilityId, NvSettingControl.Decode(value.Value)));
                }
            }
        }

        var written = 0;
        var removed = 0;
        foreach (var (key, list) in requests)
        {
            token.ThrowIfCancellationRequested();
            if (list.Select(request => request.Value).Distinct().Skip(1).Any())
            {
                failures.AddRange(list.Select(request => Failure(request,
                    "These games share a native NVIDIA profile but request different values for the same setting.")));
                continue;
            }

            var request = list[0];
            try
            {
                Apply(key, request, admission);
                written += list.Count;
            }
            catch (Exception error) when (error is not OperationCanceledException &&
                                          error is not DriverFailure { Lost: true })
            {
                failures.AddRange(list.Select(item => Failure(item, error.Message)));
                // Discard changes that were buffered but never committed. Never retry a failed Save.
                _api.Load();
            }
        }

        foreach (var entry in _owned.Values.Where(entry => !requests.ContainsKey(entry.Key)).ToArray())
        {
            token.ThrowIfCancellationRequested();
            // An invalid/unavailable desired entry is not permission to remove its prior value.
            if (sync.Profiles.Any(game => game.Executables.Contains(entry.Executable, StringComparer.OrdinalIgnoreCase)
                                          && game.Values.Any(value =>
                                              controls.TryGetValue(value.CapabilityId, out var control)
                                              && control.Setting.Id == entry.Setting)))
            {
                continue;
            }

            try
            {
                _api.Load();
                var handle = _api.FindProfile(entry.Profile);
                if (handle != 0)
                {
                    var current = _api.Get(handle, entry.Setting);
                    var alreadyRestored = current.Explicit == entry.OriginalExplicit
                                          && (!entry.OriginalExplicit || current.Value == entry.Original);
                    if (current.Explicit && current.Value == entry.Written && !alreadyRestored)
                    {
                        if (entry.OriginalExplicit)
                        {
                            _api.Set(handle, entry.Setting, entry.Original, admission);
                        }
                        else
                        {
                            _api.Inherit(handle, entry.Setting, admission);
                        }

                        _api.Save(admission);
                    }
                    // A different current value belongs to an external editor and is preserved.
                }

                // The restore was dispatched, or no owned value was left to restore.
                _owned.Remove(entry.Key);
                try
                {
                    Persist();
                }
                catch
                {
                    // Keep ownership in memory as well as on disk if the record could not be updated.
                    // The next sync sees the restored value and only saves the record; it does not write again.
                    _owned[entry.Key] = entry;
                    throw;
                }

                removed++;
            }
            catch (Exception error) when (error is not OperationCanceledException &&
                                          error is not DriverFailure { Lost: true })
            {
                // The entry stays; the next sync compares the driver's value with it again.
                failures.Add(new GpuProfileFailure(entry.ProfileId, entry.Executable,
                    "driver." + entry.Setting.ToString("x8"), error.Message));
                _api.Load();
            }
        }

        return new GpuProfileSyncResult(written, removed, failures);
    }

    private void Apply(string key, Request request, WriteAdmission admission)
    {
        _api.Load();
        var handle = _api.Application(request.Executable, false, request.Profile, admission);
        var applicationExists = handle != 0;
        if (handle == 0)
        {
            handle = _api.FindProfile(request.Profile);
        }

        if (handle != 0 &&
            !string.Equals(_api.ProfileName(handle), request.Profile, StringComparison.OrdinalIgnoreCase))
        {
            throw new DriverFailure("The executable's NVIDIA profile changed during synchronization.");
        }

        var current = handle != 0
            ? _api.Get(handle, request.Setting)
            : (_api.Get(_api.GlobalProfile(), request.Setting).Value, Explicit: false);
        _owned.TryGetValue(key, out var previous);
        var originalStillPresent = previous is not null && current.Explicit == previous.OriginalExplicit
                                                        && (!current.Explicit || current.Value == previous.Original);
        if (previous is not null && previous.Written == request.Value)
        {
            if (current.Explicit && current.Value == request.Value && applicationExists)
            {
                _owned[key] = previous with { ProfileId = request.ProfileId };
                Persist();
                return;
            }

            if (!originalStillPresent && !(current.Explicit && current.Value == request.Value))
            {
                throw new DriverFailure("An external editor changed this NVIDIA setting. Its value was preserved.");
            }
        }

        var retainOriginal = previous is not null &&
                             (originalStillPresent || (current.Explicit && current.Value == previous.Written));
        _owned[key] = new NvOwnedSetting(request.Profile, request.Setting, request.Executable, request.ProfileId,
            retainOriginal ? previous!.Original : current.Value,
            retainOriginal ? previous!.OriginalExplicit : current.Explicit, request.Value);
        Persist(); // Durable intent precedes the first native mutation and its commit.
        if (current.Explicit && current.Value == request.Value && applicationExists)
        {
            return;
        }

        try
        {
            handle = _api.Application(request.Executable, true, request.Profile, admission);
            if (!string.Equals(_api.ProfileName(handle), request.Profile, StringComparison.OrdinalIgnoreCase))
            {
                throw new DriverFailure("The executable's NVIDIA profile changed during synchronization.");
            }

            if (!current.Explicit || current.Value != request.Value)
            {
                _api.Set(handle, request.Setting, request.Value, admission);
            }

            _api.Save(admission);
        }
        catch (Exception failure)
        {
            // A dispatched mutation or uncertain commit retains its original intent. A later explicit
            // sync reads the driver and adopts a committed value without repeating its setter/save.
            if (failure is DriverFailure { Attempted: true } ||
                failure is not (DriverFailure or OperationCanceledException))
            {
                throw;
            }

            // No uncertain commit was dispatched. Buffered DRS changes are discarded by Sync's reload;
            // restore the prior ownership record rather than claiming a new applied override.
            if (previous is null)
            {
                _owned.Remove(key);
            }
            else
            {
                _owned[key] = previous;
            }

            Persist();
            throw;
        }
    }

    private void Persist()
    {
        DriverStateFile.Write(_path,
            new NvProfileJournal(_owned.Values.OrderBy(entry => entry.Key).ToArray(), _legacyProfilesRemoved));
    }

    private void CleanupLegacyProfiles(WriteAdmission admission, CancellationToken token)
    {
        if (_legacyProfilesRemoved)
        {
            return;
        }

        var profiles = _api.Profiles().Where(profile => IsLegacyProfile(profile.Name)).ToArray();
        foreach (var profile in profiles)
        {
            token.ThrowIfCancellationRequested();
            _api.DeleteProfile(profile.Handle, admission);
        }

        if (profiles.Length > 0)
        {
            _api.Save(admission);
            _api.Load();
        }

        // Do not restore values from deleted legacy profiles or carry their ownership into new profiles.
        var entries = _owned.Values.Where(entry => IsLegacyProfile(entry.Profile)).ToArray();
        foreach (var entry in entries)
        {
            _owned.Remove(entry.Key);
        }

        _legacyProfilesRemoved = true;
        try
        {
            Persist();
        }
        catch
        {
            _legacyProfilesRemoved = false;
            foreach (var entry in entries)
            {
                _owned.Add(entry.Key, entry);
            }

            throw;
        }
    }

    private static bool IsLegacyProfile(string name)
    {
        return name.Length == 29 && name.StartsWith("WSGM ", StringComparison.Ordinal)
                                 && name.Skip(5).All(Uri.IsHexDigit);
    }

    private static GpuProfileFailure Failure(Request request, string detail)
    {
        return new GpuProfileFailure(request.ProfileId, request.Executable, request.CapabilityId, detail);
    }

    internal static string OwnedProfile(string executable)
    {
        return "LibGPUDriverInteract " + DriverIds.Hash(executable.ToLowerInvariant()).ToUpperInvariant();
    }

    private sealed record Request(
        string ProfileId,
        string Executable,
        string Profile,
        uint Setting,
        string CapabilityId,
        uint Value);
}
