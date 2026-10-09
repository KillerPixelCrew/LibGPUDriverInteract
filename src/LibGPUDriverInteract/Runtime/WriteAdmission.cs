// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract.Runtime;

/// <summary>Checks caller cancellation and the owner's active flag immediately before a native setter.</summary>
internal readonly struct WriteAdmission(CancellationToken token, Func<bool> admitted)
{
    internal bool Admitted => !token.IsCancellationRequested && admitted();

    internal void Check()
    {
        if (!Admitted)
        {
            throw new DriverFailure("The GPU write is no longer admitted (cancelled or stopped).");
        }
    }
}
