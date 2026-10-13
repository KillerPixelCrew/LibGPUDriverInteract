# Troubleshooting

| Symptom               | Inspect and act                                                                           |
| --------------------- | ----------------------------------------------------------------------------------------- |
| No detected vendor    | Confirm present Windows display adapters; detection does not read installer metadata      |
| Unavailable startup   | Check x64 architecture, installed driver, loading diagnostics and supported controls      |
| Feature absent        | Inspect current native support and descriptors; do not force another driver version's IDs |
| Rejected command      | Check capability/instance, value kind, bounds, choices and active admission               |
| AppliedUnverified     | Retain accepted value and confirmation limits; absent readback is not setter rejection    |
| Indeterminate write   | Do not retry automatically; require resolution or a fresh explicit action                 |
| Display route changed | Refresh topology and rematch physical identity, not display numbering                     |
| Driver lost           | Signal topology or a user operation; polling does not reopen or repeat setters            |
| Profile sync fails    | Inspect every failure, executable identity, native scope and journal readability          |
| Stop returns false    | Cleanup continues; keep the owner/DLL alive and await Dispose                             |

Supply Action<GpuLogEntry> diagnostics. Record StatusChanged, command results and profile failures.
Reports should identify vendor, driver version, adapter/output, capability/instance and the exact
outcome. Do not confuse vendor presence, successful compilation or a support declaration with
working hardware behavior.
