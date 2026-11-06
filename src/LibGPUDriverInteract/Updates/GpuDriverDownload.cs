namespace LibGPUDriverInteract;

/// <summary>A verified package held against replacement from download until installation completes.</summary>
public sealed class GpuDriverDownload : IAsyncDisposable
{
    private FileStream? _stream;

    internal GpuDriverDownload(GpuDriverPackage package, string path, FileStream stream)
    {
        Package = package;
        Path = path;
        _stream = stream;
    }

    /// <summary>The exact confirmed package whose bytes this owner holds.</summary>
    public GpuDriverPackage Package { get; }
    /// <summary>The protected temporary file; delete it after disposing this owner.</summary>
    public string Path { get; }
    internal FileStream Stream => _stream ?? throw new ObjectDisposedException(nameof(GpuDriverDownload));

    /// <summary>Releases the held file. A completed or cancelled download is never installed automatically.</summary>
    public async ValueTask DisposeAsync()
    {
        var stream = Interlocked.Exchange(ref _stream, null);
        if (stream is not null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
