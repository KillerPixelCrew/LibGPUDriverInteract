// SPDX-License-Identifier: MIT

using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract;

public sealed partial class GpuDriver
{
    /// <summary>Reads source sizes on the existing serialized driver lane; never activates a display mode.</summary>
    public async ValueTask<GpuScaledResolutionCatalog?> ReadScaledResolutionsAsync(GpuDisplayTarget display,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (!await EnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            return await Task.Run(() => _runtime.ReadScaledResolutions(display), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _lane.Release();
        }
    }

    /// <summary>Adds or removes one source size, preserving all other driver data and the active display mode.</summary>
    public async ValueTask<GpuScaledResolutionResult> SetScaledResolutionAsync(GpuScaledResolutionCatalog observed,
        GpuScaledResolution mode, bool remove, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(mode);
        if (!mode.Valid || !await EnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return new GpuScaledResolutionResult(false, false, GpuScaledResolutionActivation.Immediate,
                "Invalid source size or driver admission is closed.");
        }

        try
        {
            return await Task.Run(() => _runtime.SetScaledResolution(observed, mode, remove,
                new WriteAdmission(cancellationToken, () => _active && !_disposed)), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _lane.Release();
        }
    }

    /// <summary>Explicitly restarts only the selected display's still-matching driving adapter.</summary>
    /// <remarks>The display can go blank and graphics applications can lose their devices.</remarks>
    public async ValueTask<GpuScaledResolutionResult> RestartScaledResolutionDriverAsync(
        GpuScaledResolutionCatalog observed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observed);
        if (!await EnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return new GpuScaledResolutionResult(false, false, GpuScaledResolutionActivation.DriverRestart,
                "Driver admission is closed.");
        }

        try
        {
            return await Task.Run(() => _runtime.RestartScaledResolutionDriver(observed,
                new WriteAdmission(cancellationToken, () => _active && !_disposed)), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _lane.Release();
        }
    }
}
