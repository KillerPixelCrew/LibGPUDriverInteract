// SPDX-License-Identifier: MIT

using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Intel.Display;

/// <summary>A refresh rate fixed through the Custom Arc Sync profile, and the profile to put back.</summary>
/// <param name="FixedHz">The rate the Custom profile holds as both its minimum and maximum.</param>
/// <param name="Profile">The profile in use before the first fixed write.</param>
/// <param name="MinimumHz">That profile's minimum refresh rate.</param>
/// <param name="MaximumHz">That profile's maximum refresh rate.</param>
/// <param name="MaxFrameTimeIncreaseUs">That profile's frame time increase limit.</param>
/// <param name="MaxFrameTimeDecreaseUs">That profile's frame time decrease limit.</param>
internal sealed record FixedRefreshRecord(
    float FixedHz,
    int Profile,
    float MinimumHz,
    float MaximumHz,
    uint MaxFrameTimeIncreaseUs,
    uint MaxFrameTimeDecreaseUs)
{
    /// <summary>How far a driver readback may sit from the written rate and still be the fixed profile.</summary>
    private const float Tolerance = 0.05f;

    /// <summary>The profile to write back.</summary>
    public CtlArcSyncProfileParams Before
    {
        get
        {
            CtlArcSyncProfileParams profile = default;
            profile.Profile = Profile;
            profile.MinimumHz = MinimumHz;
            profile.MaximumHz = MaximumHz;
            profile.MaxFrameTimeIncreaseUs = MaxFrameTimeIncreaseUs;
            profile.MaxFrameTimeDecreaseUs = MaxFrameTimeDecreaseUs;
            return profile;
        }
    }

    /// <summary>Records the profile in use before a rate is fixed.</summary>
    /// <param name="before">The profile in use.</param>
    /// <param name="fixedHz">The rate about to be fixed.</param>
    /// <returns>The record.</returns>
    public static FixedRefreshRecord Capture(CtlArcSyncProfileParams before, float fixedHz)
    {
        return new FixedRefreshRecord(fixedHz, before.Profile, before.MinimumHz, before.MaximumHz,
            before.MaxFrameTimeIncreaseUs, before.MaxFrameTimeDecreaseUs);
    }

    /// <summary>Whether the driver still runs the rate this record fixed.</summary>
    /// <param name="profile">The profile the driver reports.</param>
    /// <returns>True for the Custom profile with both bounds at the fixed rate.</returns>
    public bool Matches(CtlArcSyncProfileParams profile)
    {
        return profile.Profile == ArcSyncDisplay.ProfileCustom
               && Math.Abs(profile.MinimumHz - FixedHz) <= Tolerance
               && Math.Abs(profile.MaximumHz - FixedHz) <= Tolerance;
    }
}

/// <summary>The fixed refresh rates this library holds, per display, kept on disk until they are put back.</summary>
/// <remarks>
///     The Arc Sync profile is persistent driver state, so a fixed rate outlives the process that set it. A
///     record is written before the fixed profile is, and removed once the profile in use before it is back.
///     Records found at load belong to a previous process: each display restores its own once, when it is
///     discovered. A record file that cannot be read is left untouched and nothing is persisted that session.
/// </remarks>
internal sealed class FixedRefreshStore
{
    private const string Scope = "arcsync";
    private readonly Dictionary<string, CtlArcSyncProfileParams> _baselines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FixedRefreshRecord> _live = new(StringComparer.Ordinal);
    private readonly IDriverSink _log;
    private readonly Dictionary<string, FixedRefreshRecord> _recovered;
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);
    private string? _path;

    private FixedRefreshStore(string? path, Dictionary<string, FixedRefreshRecord> recovered, IDriverSink log)
    {
        _path = path;
        _recovered = recovered;
        _log = log;
    }

    /// <summary>The displays with a rate fixed this session.</summary>
    public IReadOnlyCollection<string> Live => _live.Keys;

    /// <summary>Loads the records a previous process left.</summary>
    /// <param name="directory">The driver state directory, or null to keep records in memory only.</param>
    /// <param name="log">Receives a record file that could not be read.</param>
    /// <returns>The store.</returns>
    public static FixedRefreshStore Load(string? directory, IDriverSink log)
    {
        var path = directory is null ? null : Path.Combine(directory, "arc-sync-fixed-refresh.v1.json");
        Dictionary<string, FixedRefreshRecord> recovered = new(StringComparer.Ordinal);
        if (path is null)
        {
            return new FixedRefreshStore(null, recovered, log);
        }

        try
        {
            foreach (var (display, record) in DriverStateFile.Read(path,
                         new Dictionary<string, FixedRefreshRecord>()))
            {
                recovered[display] = record;
            }
        }
        catch (DriverFailure failure)
        {
            log.Warn(Scope, $"{failure.Message} The fixed refresh record is left as it is and not written this session.");
            path = null;
        }

        return new FixedRefreshStore(path, recovered, log);
    }

    /// <summary>
    ///     The profile a released fixed rate returns to, when a policy that owns the Arc Sync profile beneath it
    ///     supplied one; otherwise the profile captured before the first fixed write.
    /// </summary>
    /// <param name="display">The display's instance id.</param>
    /// <returns>The supplied baseline, or null.</returns>
    public CtlArcSyncProfileParams? Baseline(string display)
    {
        return _baselines.TryGetValue(display, out var baseline) ? baseline : null;
    }

    /// <summary>Supplies or clears the profile a released fixed rate returns to on one display.</summary>
    /// <param name="display">The display's instance id.</param>
    /// <param name="baseline">The profile, or null to return to the captured one.</param>
    public void SetBaseline(string display, CtlArcSyncProfileParams? baseline)
    {
        if (baseline is { } profile)
        {
            _baselines[display] = profile;
        }
        else
        {
            _baselines.Remove(display);
        }
    }

    /// <summary>The rate fixed on a display this session.</summary>
    public FixedRefreshRecord? Get(string display)
    {
        return _live.GetValueOrDefault(display);
    }

    /// <summary>Records a fixed rate, on disk before the caller writes it.</summary>
    public void Set(string display, FixedRefreshRecord record)
    {
        _live[display] = record;
        Save();
    }

    /// <summary>Forgets a display's fixed rate once it is put back or the user took the profile over.</summary>
    public void Remove(string display)
    {
        if (_live.Remove(display))
        {
            Save();
        }
    }

    /// <summary>Hands out a previous process's record for a display, once per session.</summary>
    public FixedRefreshRecord? TakeRecovered(string display)
    {
        return _taken.Add(display) && !_live.ContainsKey(display) ? _recovered.GetValueOrDefault(display) : null;
    }

    /// <summary>Drops a previous process's record once it was put back or found superseded.</summary>
    public void ForgetRecovered(string display)
    {
        if (_recovered.Remove(display))
        {
            Save();
        }
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        Dictionary<string, FixedRefreshRecord> records = new(_recovered, StringComparer.Ordinal);
        foreach (var (display, record) in _live)
        {
            records[display] = record;
        }

        try
        {
            if (records.Count == 0)
            {
                File.Delete(_path);
            }
            else
            {
                DriverStateFile.Write(_path, records);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Error(Scope, $"The fixed refresh record could not be saved: {DriverLog.Describe(error)}");
        }
    }
}
