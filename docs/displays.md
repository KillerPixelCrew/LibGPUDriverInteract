# Display identity

Physical output sections can carry GpuSection.DisplayTarget. Adapter-wide policies have no physical
target. A driver output identifier is diagnostic metadata, not a Windows monitor route.

| Field                                       | Meaning                                     | Persist for remembered choices?     |
| ------------------------------------------- | ------------------------------------------- | ----------------------------------- |
| DevicePath                                  | Exact Windows monitor device-interface path | Yes, with current resolution checks |
| EdidIdentity                                | EDID manufacturer/product/serial identity   | Yes, as physical identity evidence  |
| SourceName                                  | Current GDI name such as `\\.\DISPLAY1`     | No                                  |
| AdapterLowPart / AdapterHighPart / TargetId | Current CCD route                           | No                                  |
| NativeOutputId                              | Vendor-native diagnostic output identity    | No                                  |

Hotplug, reboot and driver changes can alter routes and instance IDs. Persist physical identity,
signal RefreshTopologyAsync, then resolve remembered choices against the new descriptors and target.
Do not route by a friendly title, display number or duplicate EDID serial alone.

Discovery includes connected inactive monitors when the native API resolves them. Absence is not an
instruction to write to a guessed output. Match the latest capability and instance before dispatch.

Windows topology, resolution, generic HDR and layout policy belong to WindowsDeviceControl or the
host. This library exposes vendor output controls and read-only identity; it does not activate a
saved desktop layout. Display writes can retrain the link and must be explicit operations.
