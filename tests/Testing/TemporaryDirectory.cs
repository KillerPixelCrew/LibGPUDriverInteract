// SPDX-License-Identifier: MIT

namespace LibGPUDriverInteract.Testing;

/// <summary>A uniquely named directory under the system temp folder, deleted on dispose.</summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "libgpu-driver-interact-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        Directory.Delete(Root, true);
    }

    public string GetPath(params string[] segments)
    {
        return segments.Aggregate(Root, Path.Combine);
    }
}
