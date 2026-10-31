using LibGPUDriverInteract.Runtime;
using LibGPUDriverInteract.Testing;
using Xunit;

namespace LibGPUDriverInteract.Nvidia.Tests;

public sealed class NvProfilesTests
{
    private const uint Setting = 0x1057eb71;
    private static readonly WriteAdmission Admitted = new(CancellationToken.None, () => true);
    private static readonly Action<string, string> Ignore = (_, _) => { };

    [Fact]
    public void SessionInitializationCleansOnlyExactLegacyProfilesWithoutPublishedControls()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs();
        var old = "WSGM 0123456789abcdefABCDEF01";
        api.NamedProfiles[10] = old;
        api.NamedProfiles[11] = "WSGM 0123456789abcdefABCDEF0";
        api.NamedProfiles[12] = "WSGM 0123456789abcdefABCDEF012";
        api.NamedProfiles[13] = "WSGM 0123456789abcdefABCDEFX1";
        api.NamedProfiles[14] = "wsgm 0123456789abcdefABCDEF01";
        api.NamedProfiles[15] = "WSGM GPUDriver 0123456789abcdefABCDEF01";
        api.NamedProfiles[16] = "User profile";
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        var session = new DriverTestSession { BeforeInitialize = profiles.Initialize };
        var runtime = new DriverRuntime("nvidia", (_, _) => session);
        try
        {
            Assert.Equal(GpuHealth.Unavailable,
                runtime.Start(new RecordingDriverSink(), directory.Root, () => true, CancellationToken.None));
            Assert.Equal(1, session.Initializations);
            Assert.Empty(session.Model.Controls);
        }
        finally
        {
            runtime.Close();
        }

        Assert.Equal(new nint[] { 10 }, api.DeletedProfiles);
        Assert.Equal(6, api.NamedProfiles.Count);
        Assert.Equal(1, api.Saves);
        Assert.True(DriverStateFile.Read(JournalPath(directory),
            new NvProfileJournal([])).LegacyProfilesRemoved);
        // No profile sync or NvSettingControl was created: cleanup is independent of descriptor discovery.
    }

    [Fact]
    public void SuccessfulLegacyCleanupIsPersistedAndNotRepeatedOnAnotherEnabledOpen()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs();
        api.NamedProfiles[10] = "WSGM 0123456789abcdefABCDEF01";
        new NvProfiles(api, directory.Root, Ignore).Initialize(Admitted, CancellationToken.None);
        api.NamedProfiles[11] = "WSGM ABCDEF0123456789abcdef01";
        new NvProfiles(api, directory.Root, Ignore).Initialize(Admitted, CancellationToken.None);
        Assert.Equal(new nint[] { 10 }, api.DeletedProfiles);
        Assert.Equal(1, api.Saves);
        Assert.Equal(1, api.Enumerations);
        Assert.True(api.NamedProfiles.ContainsKey(11));
    }

    [Fact]
    public void LegacyCleanupRemovesOnlyDeletedProfilesFromTheExistingOwnershipJournal()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs();
        var old = "WSGM 0123456789abcdefABCDEF01";
        api.NamedProfiles[10] = old;
        var unrelated = new NvOwnedSetting("Native game", Setting, "game.exe", "game", 7, true, 9);
        var path = JournalPath(directory);
        DriverStateFile.Write(path, new NvProfileJournal([
            unrelated,
            new NvOwnedSetting(old, Setting, "old.exe", "old", 7, true, 9)
        ]));
        new NvProfiles(api, directory.Root, Ignore).Initialize(Admitted, CancellationToken.None);
        var journal = DriverStateFile.Read(path, new NvProfileJournal([]));
        Assert.Equal(unrelated, Assert.Single(journal.Entries));
        Assert.True(journal.LegacyProfilesRemoved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCleanupSaveIsReconciledOnTheNextExplicitOpenWithoutResavingCommittedDeletions(bool committed)
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs { FailSave = !committed, FailAfterSave = committed };
        api.NamedProfiles[10] = "WSGM 0123456789abcdefABCDEF01";
        Assert.Throws<DriverFailure>(() =>
            new NvProfiles(api, directory.Root, Ignore).Initialize(Admitted, CancellationToken.None));
        Assert.False(File.Exists(JournalPath(directory)));
        api.FailSave = false;
        api.FailAfterSave = false;
        new NvProfiles(api, directory.Root, Ignore).Initialize(Admitted, CancellationToken.None);
        Assert.Empty(api.NamedProfiles);
        Assert.Equal(committed ? 1 : 2, api.Saves);
        Assert.True(DriverStateFile
            .Read(JournalPath(directory), new NvProfileJournal([]))
            .LegacyProfilesRemoved);
    }

    [Fact]
    public void CorruptOwnershipJournalSuppressesCleanupAndPreservesAllNativeProfilesAndRecordBytes()
    {
        using var directory = new TemporaryDirectory();
        var path = JournalPath(directory);
        File.WriteAllText(path, "corrupt ownership");
        var api = new Drs();
        api.NamedProfiles[10] = "WSGM 0123456789abcdefABCDEF01";
        new NvProfiles(api, directory.Root, Ignore).Initialize(Admitted, CancellationToken.None);
        Assert.Empty(api.DeletedProfiles);
        Assert.Equal(0, api.Enumerations);
        Assert.Equal(0, api.Saves);
        Assert.Equal("corrupt ownership", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedOrCancelledAdmissionNeverDeletesOrSavesLegacyProfiles(bool cancelled)
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs();
        api.NamedProfiles[10] = "WSGM 0123456789abcdefABCDEF01";
        var token = new CancellationToken(cancelled);
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        if (cancelled)
        {
            Assert.Throws<OperationCanceledException>(() =>
                profiles.Initialize(new WriteAdmission(token, () => false), token));
        }
        else
        {
            Assert.Throws<DriverFailure>(() => profiles.Initialize(new WriteAdmission(token, () => false), token));
        }

        Assert.Empty(api.DeletedProfiles);
        Assert.Equal(0, api.Saves);
        Assert.True(api.NamedProfiles.ContainsKey(10));
    }

    [Fact]
    public void ReadingAnInheritedDrsSettingDoesNotMaterializeAnOverride()
    {
        var api = new Drs();
        api.Values[123] = 99;
        var control = Controls(api).Values.Single();
        control.Read();
        Assert.Equal(0, api.Saves);
        Assert.False(api.Values.ContainsKey(Setting));
        Assert.Equal(99u, api.Values[123]);
    }

    [Fact]
    public void ReadingAnExplicitDrsSettingDoesNotSaveTheDatabase()
    {
        var api = new Drs { GlobalExplicit = true };
        api.Values[Setting] = 9;
        api.Values[123] = 99;
        var control = Controls(api).Values.Single();
        control.Read();
        Assert.Equal(0, api.Saves);
        Assert.Equal(9u, api.Values[Setting]);
        Assert.Equal(99u, api.Values[123]);
    }

    [Fact]
    public void InheritRestoresPriorExplicitValueAndPreservesUnrelatedSettings()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs();
        api.Values[Setting] = 7;
        api.Values[123] = 99;
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        Assert.Empty(profiles.Sync(Sync(1, 9), Controls(api), Admitted, CancellationToken.None).Failures);
        Assert.Equal(9u, api.Values[Setting]);
        Assert.Equal(1, profiles.Sync(Empty(2), Controls(api), Admitted, CancellationToken.None).Removed);
        Assert.Equal(7u, api.Values[Setting]);
        Assert.Equal(99u, api.Values[123]);
    }

    [Fact]
    public void InheritRemovesOnlyOwnedOverrideAndReadsTheCurrentGlobalValue()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs();
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        profiles.Sync(Sync(1, 9), Controls(api), Admitted, CancellationToken.None);
        api.Global = 12;
        profiles.Sync(Empty(2), Controls(api), Admitted, CancellationToken.None);
        Assert.False(api.Values.ContainsKey(Setting));
        Assert.Equal((12u, false), api.Get(2, Setting));
    }

    [Fact]
    public void ExternalEditIsPreservedDuringSyncAndRemoval()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs();
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        profiles.Sync(Sync(1, 9), Controls(api), Admitted, CancellationToken.None);
        api.Values[Setting] = 12;
        Assert.Single(profiles.Sync(Sync(2, 9), Controls(api), Admitted, CancellationToken.None).Failures);
        profiles.Sync(Empty(3), Controls(api), Admitted, CancellationToken.None);
        Assert.Equal(12u, api.Values[Setting]);
        Assert.Equal(1, api.Saves);
    }

    [Fact]
    public void FailedSaveRetainsOriginalIntentAndALaterExplicitSyncReconcilesTheDriver()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs { FailSave = true };
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        Assert.Single(profiles.Sync(Sync(1, 9), Controls(api), Admitted, CancellationToken.None).Failures);
        Assert.False(Assert.Single(DriverStateFile.Read(JournalPath(directory),
            new NvProfileJournal([])).Entries).OriginalExplicit);
        api.FailSave = false;
        Assert.Empty(profiles.Sync(Sync(2, 9), Controls(api), Admitted, CancellationToken.None).Failures);
        Assert.Equal(2, api.Saves);
        Assert.Equal(9u, api.Values[Setting]);
    }

    [Fact]
    public void SaveThatCommittedDespiteAFailureIsAdoptedWithoutAnotherWrite()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs { FailAfterSave = true };
        api.Values[Setting] = 7;
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        Assert.Single(profiles.Sync(Sync(1, 9), Controls(api), Admitted, CancellationToken.None).Failures);
        api.FailAfterSave = false;
        Assert.Empty(profiles.Sync(Sync(2, 9), Controls(api), Admitted, CancellationToken.None).Failures);
        Assert.Equal(1, api.Saves);
        Assert.Equal(9u, api.Values[Setting]);
        Assert.Equal(7u, Assert.Single(DriverStateFile.Read(JournalPath(directory),
            new NvProfileJournal([])).Entries).Original);
        profiles.Sync(Empty(3), Controls(api), Admitted, CancellationToken.None);
        Assert.Equal(7u, api.Values[Setting]);
        Assert.Equal(2, api.Saves);
    }

    [Fact]
    public void FailedRestorationKeepsTheEntryAndTheNextSyncRestoresAgain()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs();
        api.Values[Setting] = 7;
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        profiles.Sync(Sync(1, 9), Controls(api), Admitted, CancellationToken.None);
        api.FailSave = true;
        Assert.Single(profiles.Sync(Empty(2), Controls(api), Admitted, CancellationToken.None).Failures);
        api.FailSave = false;
        Assert.Equal(1, profiles.Sync(Empty(3), Controls(api), Admitted, CancellationToken.None).Removed);
        Assert.Equal(3, api.Saves);
        Assert.Equal(7u, api.Values[Setting]);
    }

    [Fact]
    public void FailedRecordSaveAfterRestorationKeepsOwnershipUntilOnlyTheRecordCanBeSaved()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs();
        api.Values[Setting] = 7;
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        profiles.Sync(Sync(1, 9), Controls(api), Admitted, CancellationToken.None);
        var path = JournalPath(directory);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Single(profiles.Sync(Empty(2), Controls(api), Admitted, CancellationToken.None).Failures);
            Assert.Equal(7u, api.Values[Setting]);
            Assert.Equal(2, api.Saves);
        }

        Assert.Single(DriverStateFile.Read(path, new NvProfileJournal([])).Entries);
        Assert.Equal(1, profiles.Sync(Empty(3), Controls(api), Admitted, CancellationToken.None).Removed);
        Assert.Equal(2, api.Saves);
        Assert.Empty(DriverStateFile.Read(path, new NvProfileJournal([])).Entries);
    }

    [Fact]
    public void GamesSharingOneNativeProfileCannotRequestConflictingValues()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs();
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        var sync = new GpuProfileSync(1, [Game("one", "one.exe", 9), Game("two", "two.exe", 10)]);
        Assert.Equal(2, profiles.Sync(sync, Controls(api), Admitted, CancellationToken.None).Failures.Count);
        Assert.Equal(0, api.Saves);
    }

    [Fact]
    public void SameValueInSharedNativeProfileIsWrittenOnce()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs();
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        var sync = new GpuProfileSync(1, [Game("one", "one.exe", 9), Game("two", "two.exe", 9)]);
        Assert.Equal(2, profiles.Sync(sync, Controls(api), Admitted, CancellationToken.None).Written);
        Assert.Equal(1, api.Saves);
    }

    [Fact]
    public void RevisionOrderingBelongsToTheCallerAndTheLatestDesiredSetApplies()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs();
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        profiles.Sync(Sync(2, 9), Controls(api), Admitted, CancellationToken.None);
        profiles.Sync(Empty(1), Controls(api), Admitted, CancellationToken.None);
        Assert.DoesNotContain(Setting, api.Values.Keys);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("{}")]
    [InlineData("{\"Entries\":null}")]
    [InlineData("{\"Entries\":[null]}")]
    public void CorruptJournalFailsEverySyncWithoutReplacingIt(string content)
    {
        using var directory = new TemporaryDirectory();
        var path = JournalPath(directory);
        File.WriteAllText(path, content);
        var api = new Drs();
        var reports = new List<string>();
        var profiles = new NvProfiles(api, directory.Root, (key, detail) => reports.Add(key + ": " + detail));
        var failure = Assert.Single(profiles.Sync(Sync(1, 9), Controls(api), Admitted, CancellationToken.None)
            .Failures);
        Assert.Equal("game.exe", failure.Executable);
        Assert.Equal(0, api.Saves);
        Assert.Single(reports);
        Assert.Equal(content, File.ReadAllText(path));
    }

    private static GpuProfileSync Empty(long revision)
    {
        return new GpuProfileSync(revision, []);
    }

    [Fact]
    public void EmptyDesiredSetWithAnUnreadableJournalCannotClaimSuccessfulRemoval()
    {
        using var directory = new TemporaryDirectory();
        var path = JournalPath(directory);
        File.WriteAllText(path, "broken");
        var api = new Drs();
        var failure = Assert.Single(new NvProfiles(api, directory.Root, Ignore)
            .Sync(Empty(1), Controls(api), Admitted, CancellationToken.None).Failures);
        Assert.Equal("driver", failure.ProfileId);
        Assert.Contains("could not be read", failure.Detail);
        Assert.Equal("broken", File.ReadAllText(path));
        Assert.Equal(0, api.Saves);
    }

    [Fact]
    public void NewApplicationIntentIsDurableBeforeEitherNativeCreationAndRestoresInheritedState()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs { ApplicationExists = false, Global = 7 };
        var path = JournalPath(directory);

        void CheckIntent()
        {
            var intent = Assert.Single(DriverStateFile.Read(path, new NvProfileJournal([])).Entries);
            Assert.Equal(7u, intent.Original);
            Assert.False(intent.OriginalExplicit);
            Assert.Equal(9u, intent.Written);
            Assert.Equal(NvProfiles.OwnedProfile("game.exe"), intent.Profile);
        }

        api.BeforeCreateProfile = CheckIntent;
        api.BeforeCreateApplication = CheckIntent;
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        Assert.Empty(profiles.Sync(Sync(1, 9), Controls(api), Admitted, CancellationToken.None).Failures);
        Assert.Equal(1, api.ProfileCreations);
        Assert.Equal(1, api.ApplicationCreations);
        Assert.Equal(1, api.Saves);
        profiles.Sync(Empty(2), Controls(api), Admitted, CancellationToken.None);
        Assert.DoesNotContain(Setting, api.Values.Keys);
        Assert.Equal((7u, false), api.Get(2, Setting));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdmissionIsRecheckedBeforeEachNativeCreationAfterPreparatoryReads(bool beforeApplication)
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var api = new Drs { ApplicationExists = false };
        if (beforeApplication)
        {
            api.BeforeCreateApplication = cancellation.Cancel;
        }
        else
        {
            api.BeforeCreateProfile = cancellation.Cancel;
        }

        var profiles = new NvProfiles(api, directory.Root, Ignore);
        var result = profiles.Sync(Sync(1, 9), Controls(api), new WriteAdmission(cancellation.Token, () => true),
            cancellation.Token);
        Assert.Single(result.Failures);
        Assert.Equal(beforeApplication ? 1 : 0, api.ProfileCreations);
        Assert.Equal(0, api.ApplicationCreations);
        Assert.Equal(0, api.Saves);
        Assert.Empty(api.NamedProfiles);
        Assert.Empty(api.Values);
    }

    [Fact]
    public void FailedIntentPersistenceNeverCreatesANativeProfileOrApplication()
    {
        using var directory = new TemporaryDirectory();
        var api = new Drs { ApplicationExists = false };
        var profiles = new NvProfiles(api, directory.Root, Ignore);
        var path = JournalPath(directory);
        DriverStateFile.Write(path, new NvProfileJournal([]));
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Single(profiles.Sync(Sync(1, 9), Controls(api), Admitted, CancellationToken.None).Failures);
        }

        Assert.Equal(0, api.ProfileCreations);
        Assert.Equal(0, api.ApplicationCreations);
        Assert.Equal(0, api.Saves);
        Assert.Empty(DriverStateFile.Read(path, new NvProfileJournal([])).Entries);
    }

    private static string JournalPath(TemporaryDirectory directory)
    {
        return directory.GetPath("nvidia-profiles.v1.json");
    }

    private static GpuProfileSync Sync(long revision, uint value)
    {
        return new GpuProfileSync(revision, [Game("game", "game.exe", value)]);
    }

    private static GpuApplicationProfile Game(string id, string executable, uint value)
    {
        return new GpuApplicationProfile(id, id, [executable],
        [
            new GpuApplicationValue("driver.1057eb71", "driver",
                GpuValue.Choice(NvSettingControl.Encode(value)))
        ]);
    }

    private static Dictionary<string, NvSettingControl> Controls(Drs api)
    {
        return new Dictionary<string, NvSettingControl>
        {
            ["driver.1057eb71"] = new(api,
                new NvSettingDefinition(Setting, "Power", "graphics", true, true, []),
                "graphics", [7, 9, 10, 12])
        };
    }

    private sealed class Drs : INvProfiles
    {
        private Dictionary<uint, uint> _pending = [];
        private Dictionary<nint, string> _pendingProfiles = [];
        internal Dictionary<nint, string> NamedProfiles { get; } = [];
        internal List<nint> DeletedProfiles { get; } = [];
        internal Dictionary<uint, uint> Values { get; } = [];
        internal uint Global { get; set; } = 5;
        internal bool GlobalExplicit { get; set; }
        internal int Saves { get; private set; }
        internal int Enumerations { get; private set; }
        internal bool FailSave { get; set; }
        internal bool FailAfterSave { get; set; }
        internal bool ApplicationExists { get; set; } = true;
        internal string ApplicationName { get; private set; } = "Native game";
        internal int ProfileCreations { get; private set; }
        internal int ApplicationCreations { get; private set; }
        internal Action? BeforeCreateProfile { get; set; }
        internal Action? BeforeCreateApplication { get; set; }

        public void Load()
        {
            _pending = new Dictionary<uint, uint>(Values);
            _pendingProfiles = new Dictionary<nint, string>(NamedProfiles);
        }

        public void Save(WriteAdmission admission)
        {
            admission.Check();
            Saves++;
            if (FailSave)
            {
                throw new DriverFailure("Save failed before commit.", true);
            }

            Values.Clear();
            NamedProfiles.Clear();
            foreach (var profile in _pendingProfiles)
            {
                NamedProfiles.Add(profile.Key, profile.Value);
            }

            foreach (var pair in _pending)
            {
                Values.Add(pair.Key, pair.Value);
            }

            if (FailAfterSave)
            {
                throw new DriverFailure("Save failed after commit.", true);
            }
        }

        public nint GlobalProfile()
        {
            return 1;
        }

        public nint Application(string executable, bool create, string profileName, WriteAdmission admission)
        {
            if (!ApplicationExists)
            {
                if (!create)
                {
                    return 0;
                }

                if (!_pendingProfiles.Values.Contains(profileName))
                {
                    BeforeCreateProfile?.Invoke();
                    admission.Check();
                    ProfileCreations++;
                    _pendingProfiles[2] = profileName;
                }

                BeforeCreateApplication?.Invoke();
                admission.Check();
                ApplicationCreations++;
                ApplicationName = profileName;
                ApplicationExists = true;
            }

            return 2;
        }

        public nint FindProfile(string name)
        {
            return name == "Native game" || _pendingProfiles.Values.Contains(name) ? 2 : 0;
        }

        public string ProfileName(nint profile)
        {
            return _pendingProfiles.GetValueOrDefault(profile, ApplicationName);
        }

        public IReadOnlyList<NvNamedProfile> Profiles()
        {
            Enumerations++;
            return _pendingProfiles.Select(profile => new NvNamedProfile(profile.Key, profile.Value)).ToArray();
        }

        public void DeleteProfile(nint profile, WriteAdmission admission)
        {
            admission.Check();
            DeletedProfiles.Add(profile);
            _pendingProfiles.Remove(profile);
        }

        public (uint Value, bool Explicit) Get(nint profile, uint setting)
        {
            return (profile != 1 || GlobalExplicit) && _pending.TryGetValue(setting, out var value)
                ? (value, true)
                : (Global, false);
        }

        public void Set(nint profile, uint setting, uint value, WriteAdmission admission)
        {
            admission.Check();
            _pending[setting] = value;
        }

        public void Inherit(nint profile, uint setting, WriteAdmission admission)
        {
            admission.Check();
            _pending.Remove(setting);
        }
    }
}
