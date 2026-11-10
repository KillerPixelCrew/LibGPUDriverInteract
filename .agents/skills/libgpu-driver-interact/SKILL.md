---
name: libgpu-driver-interact
description:
  Implement or review LibGPUDriverInteract vendor backends, driver capabilities, native sessions and
  profile ownership. Keep application profiles and UI in consumers.
---

# GPU driver contributions

Resolve this skill alias to its owning repository before using paths. Read CONTRIBUTING.md,
README.md, API.md and relevant PROVENANCE.md entries, then trace the public GpuDriver contract
through its vendor backend and tests. Vendor native headers and attributed source own ABI facts.

Use the existing serialized backend/session and adapter/output identity. Missing readback does not
remove writable support. Declare value shape, prerequisite, scope and apply timing in the owning
capability. Keep consumer profiles, UI and game detection outside the library. Preserve external
edits and explicit/inherited values during restore; never replay a possibly applied native write.

Follow repository formatter/settings. C# uses expanded braces, var locals with explicit construction
types where needed, named literals, no multiline trailing commas and complete public XML docs. Keep
validation/projection readable and avoid pass-through wrappers or duplicate driver owners. Review
support discovery, partial writes, cancellation after dispatch, disconnect and release. Report
accepted, observed and uncertain state separately. Tests do not prove installed-driver or
physical-output behavior. Publish dependency changes before consumers update their gitlinks.
