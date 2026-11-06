namespace LibGPUDriverInteract;

/// <summary>A package selected from a vendor's official catalog for one exact adapter.</summary>
public sealed class GpuDriverPackage
{
    internal GpuDriverPackage(GpuInstalledDriver adapter, string version, DateTime date, Uri download,
        Uri details, string notes, long size, string? checksum, string arguments)
    {
        if (size is <= 0 or >= 4L * 1024 * 1024 * 1024)
        {
            throw new InvalidDataException("The official driver package size is missing or unsafe.");
        }

        Adapter = adapter;
        Version = version;
        Date = date;
        Download = download;
        Details = details;
        ReleaseNotes = notes;
        Size = size;
        Checksum = checksum;
        Arguments = arguments;
    }

    /// <summary>The installed adapter this package was selected for.</summary>
    public GpuInstalledDriver Adapter { get; }
    /// <summary>The vendor's published driver version.</summary>
    public string Version { get; }
    /// <summary>The vendor's release date.</summary>
    public DateTime Date { get; }
    /// <summary>The official download URL.</summary>
    public Uri Download { get; }
    /// <summary>The official details and license page.</summary>
    public Uri Details { get; }
    /// <summary>Plain text release notes, without executable markup.</summary>
    public string ReleaseNotes { get; }
    /// <summary>Expected byte length from the catalog or official download server.</summary>
    public long Size { get; }
    /// <summary>Published hexadecimal checksum, or null when the source supplies none.</summary>
    public string? Checksum { get; }
    internal string Arguments { get; }
}
