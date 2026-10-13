# Contributing

The public boundary is GpuDriver and library-owned records in namespace LibGPUDriverInteract. Driver
sessions, native bindings and publication sinks remain internal.

1. Identify the official native header/API or attributed open-source behavior and the affected
   driver, adapter and output. Preserve source references in PROVENANCE.md.
2. Add the native mechanism to the existing Intel, AMD or NVIDIA backend. Use its serialized lane,
   lifetime and support discovery rather than creating another driver owner.
3. Declare exact value shape, bounds, choices, instance, profile scope and apply timing. Query
   support without calling a setter to test it. Missing readback must not disable a writable
   control.
4. Preserve native handle ownership, explicit/inherited state, external edits and exact output
   identity. Report partial or uncertain results without automatic replay.
5. Add focused encoding, native-contract, lifecycle or profile-ownership regressions and update the
   public API/usage documentation. Report compilation and hardware observations separately.

Build with `dotnet build LibGPUDriverInteract.slnx -c Release` after initializing submodules. Vendor
DLLs come from the installed driver; do not redistribute them. Keep game detection, application
profiles/inheritance, UI and Windows layout policy in consumers.

WSGM pins this repository as a submodule. Push library changes before recording its updated gitlink.
