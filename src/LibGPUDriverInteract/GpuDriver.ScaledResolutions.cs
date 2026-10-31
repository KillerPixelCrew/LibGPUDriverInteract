// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract;

public sealed partial class GpuDriver
{
    /// <summary>Reads source sizes on the existing serialized driver lane; never activates a display mode.</summary>
    public async ValueTask<GpuScaledResolutionCatalog?> ReadScaledResolutionsAsync(GpuDisplayTarget display,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        return await OnLaneAsync<GpuScaledResolutionCatalog?>(() => _runtime.ReadScaledResolutions(display),
            () => null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds or removes one source size, preserving all other driver data and the active display mode.</summary>
    public async ValueTask<GpuScaledResolutionResult> SetScaledResolutionAsync(GpuScaledResolutionCatalog observed,
        GpuScaledResolution mode, bool remove, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(mode);
        if (!mode.Valid)
        {
            return Refused();
        }

        return await OnLaneAsync(() => _runtime.SetScaledResolution(observed, mode, remove, cancellationToken),
            Refused, cancellationToken).ConfigureAwait(false);

        static GpuScaledResolutionResult Refused()
        {
            return new GpuScaledResolutionResult(false, false, GpuScaledResolutionActivation.Immediate,
                "Invalid source size or driver admission is closed.");
        }
    }

    /// <summary>Explicitly restarts only the selected display's still-matching driving adapter.</summary>
    /// <remarks>The display can go blank and graphics applications can lose their devices.</remarks>
    public async ValueTask<GpuScaledResolutionResult> RestartScaledResolutionDriverAsync(
        GpuScaledResolutionCatalog observed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observed);
        return await OnLaneAsync(() => _runtime.RestartScaledResolutionDriver(observed, cancellationToken),
            () => new GpuScaledResolutionResult(false, false, GpuScaledResolutionActivation.DriverRestart,
                "Driver admission is closed."), cancellationToken).ConfigureAwait(false);
    }
}
