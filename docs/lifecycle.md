# Lifetime and events

Create constructs a stopped owner without opening the vendor driver. Start opens a native session,
discovers supported controls and begins observations. One vendor owner can manage multiple adapters
and outputs; use one state directory per vendor and prevent competing owners writing the same
journals.

| Operation                    | Contract                                                                              |
| ---------------------------- | ------------------------------------------------------------------------------------- |
| DetectVendors                | Enumerates present vendor identities without opening native sessions                  |
| StartAsync                   | Starts one owned initialization; cancelling the wait does not cancel initialization   |
| ExecuteAsync                 | Validates and serializes one explicit control request                                 |
| SyncApplicationProfilesAsync | Submits the complete desired native executable-profile set                            |
| RefreshTopologyAsync         | Rediscovers adapters/outputs after an explicit topology signal                        |
| StopAsync                    | Closes admission and waits for native cleanup; false means the caller stopped waiting |
| DisposeAsync                 | Awaits terminal owned cleanup before native libraries can unload                      |

Native operations share one serialized lane. A cancelled wait does not mean native execution ended
or a setter was undone. Stop closes admission immediately; owned calls and loaded DLLs remain alive
until safe retirement. A later Start waits for previous cleanup. Dispose is terminal.

## Events

DescriptorsChanged supplies a complete detached replacement. StateChanged supplies changed
observations, and StatusChanged reports current health. Read-only snapshots are safe to retain, but
their availability can change. Replace descriptor sets rather than appending to them.

Events run synchronously on the publishing thread. Subscriber exceptions are logged without changing
the driver operation. Keep handlers short, marshal UI work and do not dispatch a command
synchronously from inside a callback while the native lane is held.

## Sleep, topology and driver loss

There is no public Suspend/Resume pair. The consumer decides whether to pause commands/publication
or Stop and later Start around sleep. Normal polling observes known controls; it does not repeatedly
rediscover or reopen a lost driver. Signal topology changes with RefreshTopologyAsync. A lost
session can reopen on that explicit signal or a user command, not an automatic setter retry.

Driver health can be Ready, Unavailable or Failed. Ready requires at least one supported control. Do
not confuse present vendor detection with a usable installed native driver.
