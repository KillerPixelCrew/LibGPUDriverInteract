using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LibGPUDriverInteract;

/// <summary>A package selected from a vendor's official catalog for one exact adapter.</summary>
public sealed record GpuDriverPackage
{
    /// <summary>Creates immutable package metadata; installation independently verifies the file and expected vendor signer.</summary>
    public GpuDriverPackage(GpuInstalledDriver adapter, string version, DateTime date, Uri download,
        Uri details, string notes, long size, string? checksum)
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
        Identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0',
            adapter.InstanceId, version, download.AbsoluteUri, size.ToString(CultureInfo.InvariantCulture), checksum ?? ""))));
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
    /// <summary>Stable identity of the adapter, release, official file, size and checksum shown for confirmation.</summary>
    public string Identity { get; }
}
