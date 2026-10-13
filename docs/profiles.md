# Application profiles

The application owns game identity, desired values, inheritance and active-game selection. The
library owns native executable-profile encoding and its exact driver-setting ownership.

| Scope                | Consumer behavior                                                                       |
| -------------------- | --------------------------------------------------------------------------------------- |
| NativePerApplication | Submit the complete desired executable-profile set through SyncApplicationProfilesAsync |
| Switched             | Apply resolved values explicitly when the active application changes                    |
| GlobalOnly           | Keep a global choice; there is no application-specific native mechanism                 |

Intel uses native executable profiles where the installed driver declares support. NVIDIA uses DRS
global/native executable profiles. AMD's graphics controls are GPU-wide; the consumer switches them
when selecting a game. AMD native profile synchronization is intentionally a no-op.

## Complete desired sets

GpuProfileSync carries an increasing Revision and the complete desired Profiles collection. Each
GpuApplicationProfile contains a stable consumer ProfileId, DisplayName, executable file names and
GpuApplicationValue entries. Use published capability and instance IDs. Omitted formerly owned
native entries may be removed; do not submit only the most recently edited profile.

```csharp
var sync = new GpuProfileSync(1,
[
    new GpuApplicationProfile("my-game", "My game", ["MyGame.exe"],
    [
        new GpuApplicationValue(descriptor.CapabilityId,
            descriptor.InstanceId, GpuValue.Choice(descriptor.Choices[0].Value))
    ])
]);
var result = await driver.SyncApplicationProfilesAsync(sync);
// Inspect Written, Removed and every Failures entry.
```

The example assumes a current writable Choice descriptor with native application support and at
least one option. Resolve real user choices before dispatch. Executable names are matched by the
driver; the library does not discover which store game owns a process.

## Ownership and restoration

The caller supplies durable state storage. Intel retains application ownership in
`application-profiles.v1.json` and colour ownership in `color.v1.json`. NVIDIA uses
`nvidia-profiles.v1.json` for individual native setting ownership. Preserve these files across
normal application restarts; do not share their directory with an independent writer.

Original explicit/inherited state and external edits are preserved. Cleanup targets owned entries,
not arbitrary whole driver profiles. Corrupt/unreadable journals can refuse profile sync while
independent global controls remain usable. Inspect failure records and retain evidence; deleting a
journal is not a safe way to resolve uncertain ownership.
