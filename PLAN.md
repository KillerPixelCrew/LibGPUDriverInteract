# GPU library extraction plan

1. **Inventory the existing implementations.** Account for all Intel, AMD and NVIDIA controls,
   native entry points, profile operations, shared runtime helpers and regression tests. Preserve
   source identities and distinguish driver-discovered controls from static catalog entries.
2. **Extract native backends and lifecycle.** Move driver bindings, adapter/output enumeration,
   serialization, cancellation admission and resource ownership without changing native behavior.
3. **Remove WSGM SDK coupling.** Define library-owned values and results. Leave UI placement,
   application profile selection and WSGM publications in the application; retain native profile
   mechanics and explicit restoration storage at the library boundary.
4. **Integrate WSGM directly.** Adapt its existing GPU coordinator, profiles and both UI surfaces to
   the library. Remove the three first-party GPU plugin packages and their installer offers after
   the direct path preserves the existing functionality.
5. **Verify and publish.** Preserve the existing backend tests, verify profile inheritance,
   external edits, hotplug, driver loss and output state, and publish a documented Windows .NET API.

Completion requires all existing vendor capabilities and profile semantics to work through the
library and WSGM. Creating an API or changing package placement alone is not the completed
extraction. Generic Windows controls remain in WindowsDeviceControl; handheld-specific controls
remain in LibHandheld. The common Plugin SDK stays available for independent plugins.
