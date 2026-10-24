// SPDX-License-Identifier: MIT

using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Amd;

/// <summary>Resolves a complete 3D service/feature chain for one native operation.</summary>
/// <remarks>The session owns the system and GPU targets. Services and features are scoped to each operation.</remarks>
internal sealed record AdlxGraphicsFeature(
    nint System,
    nint Gpu,
    int Slot,
    string? ServiceInterface = null,
    string? FeatureInterface = null)
{
    private readonly long _epoch = AdlxNative.Epoch;

    internal T Use<T>(Func<AdlxObject, T> operation)
    {
        return TryUse(operation, out var result) ? result
            : throw new DriverFailure("The AMD driver no longer offers this graphics interface.");
    }

    internal bool TryUse<T>(Func<AdlxObject, T> operation, out T result)
    {
        result = default!;
        if (_epoch != AdlxNative.Epoch)
        {
            throw new DriverFailure("The AMD graphics session has retired.");
        }

        using var service = AdlxNative.Interface(System, 7);
        using var versioned = ServiceInterface is null ? null : AdlxNative.Query(service.Pointer, ServiceInterface);
        if (ServiceInterface is not null && versioned is null)
        {
            return false;
        }

        using var feature = AdlxNative.GraphicsFeature((versioned ?? service).Pointer, Slot, Gpu);
        using var extended = FeatureInterface is null ? null : AdlxNative.Query(feature.Pointer, FeatureInterface);
        if (FeatureInterface is not null && extended is null)
        {
            return false;
        }

        result = operation(extended ?? feature);
        return true;
    }

    internal bool HasInterface(string name)
    {
        return Use(feature =>
        {
            using var queried = AdlxNative.Query(feature.Pointer, name);
            return queried is not null;
        });
    }
}
