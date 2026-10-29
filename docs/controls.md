# Controls and vendor support

Driver discovery supplies GpuDescriptorSet. Each GpuDescriptor contains a stable capability ID,
adapter/output InstanceId, label, role, value kind, section/category, bounds, choices, read/write
support, unit, persistence, profile scope and apply timing. Use that metadata to create UI controls.

GpuValue supports None, Boolean, Integer and Choice. A choice value is its machine identifier, not
the localized label. Integer values must fit declared bounds and step. An action can carry no value.
Unsupported or mismatched requests are rejected before a setter is dispatched.

## Vendor mechanisms

| Vendor | Interfaces                                                 | Supported areas, subject to runtime discovery                                                                                                                                     |
| ------ | ---------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Intel  | IGCL / ControlLib and the existing supported registry path | Driver-reported 3D settings, frame pacing, filtering, sharpening, scaling, shared GPU memory, Arc Sync, display colour/power and native executable profiles                       |
| AMD    | ADLX and ADL                                               | Anti-Lag, Boost, sharpening, Enhanced Sync, RSR, AFMF, supported FidelityFX upgrades, FreeSync, scaling, colour, Vari-Bright and dithering                                        |
| NVIDIA | NVAPI and DRS                                              | Performance/quality/synchronization settings, G-SYNC, supported DLSS overrides, frame generation, Smooth Motion, RTX HDR/Digital Vibrance, output format, dithering and HDR modes |

These describe supported paths, not a promise for every GPU or driver version. Native support,
adapter/output state and legal values determine publication. Intel can expose unknown scalar feature
IDs with declared value shapes; unknown custom structures are not guessed. NVIDIA private controls
are conditional. Do not write undocumented native IDs outside the published contract.

## Write outcomes

| Outcome           | Meaning                                                 |
| ----------------- | ------------------------------------------------------- |
| AppliedVerified   | Setter completed and independent readback matched       |
| AppliedUnverified | Setter completed without independent matching readback  |
| Rejected          | Refused before setter dispatch                          |
| Indeterminate     | A write may have reached the driver before interruption |

AppliedUnverified remains an accepted setter when readback is absent, fails or differs. Requested
values remain published until confirmation or a later independent change. Missing observations do
not hide otherwise writable controls. ReadbackValue is populated only for verified outcomes.

A setter that reports failure after it was dispatched is settled by readback, never by sending it
again: the control is read immediately and then over a short settle window of about a second and a
half. A readback matching the request is AppliedVerified, because the driver holds the value
whatever its result code said. A readback that still differs ends Indeterminate with the driver's
current value named in the reason and published as the observed state, so the requested value is not
shown as applied.

AMD's AFMF and RSR interfaces are service-wide: ADLX exposes them once for every GPU rather than per
adapter. Each AFMF control is still published per GPU instance, beside that GPU's Radeon Anti-Lag.
Handheld Companion turns Anti-Lag on whenever it turns AFMF on, and AMD recommends the pair; a
consumer offering AFMF should write `graphics.anti-lag` on the same instance first. The library
writes only the control it is commanded to.

GpuReason.Retryable describes whether a fresh user action might succeed; it never authorizes an
automatic retry. Identical display writes can retrain a link, so do not apply them as support probes
or refresh operations. Startup queries support without calling setters to test it.

State quality distinguishes measured, accepted and unavailable observations. ProfileScope and
ApplyTiming explain where a setting belongs and when it takes effect; writing does not override a
game-start or system-restart requirement.
