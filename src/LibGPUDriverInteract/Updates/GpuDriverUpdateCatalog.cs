using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LibGPUDriverInteract;

/// <summary>Adapter-specific updates from Intel DSA and the official AMD and NVIDIA driver selectors.</summary>
public sealed class GpuDriverUpdateCatalog : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    /// <summary>Creates an HTTP owner; no vendor application or device driver is started.</summary>
    public GpuDriverUpdateCatalog()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 WSGM-DriverUpdates/1.0");
        _http.DefaultRequestHeaders.Referrer = new Uri("https://www.amd.com/");
    }

    /// <summary>Finds the latest compatible package; throws a clear refusal when the catalog cannot identify it.</summary>
    public async Task<GpuDriverPackage> CheckAsync(GpuInstalledDriver adapter, CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        token = deadline.Token;
        return adapter.Vendor switch
        {
            GpuVendor.Intel => await IntelAsync(adapter, token).ConfigureAwait(false),
            GpuVendor.Amd => await AmdAsync(adapter, token).ConfigureAwait(false),
            GpuVendor.Nvidia => await NvidiaAsync(adapter, token).ConfigureAwait(false),
            _ => throw new NotSupportedException("This GPU vendor has no update source.")
        };
    }

    /// <summary>Whether the selected package is newer than the adapter's installed driver.</summary>
    public static bool IsNewer(GpuDriverPackage package)
    {
        var installed = package.Adapter.Version;
        if (package.Adapter.Vendor == GpuVendor.Nvidia && System.Version.TryParse(installed, out var windows))
        {
            var digits = (windows.Build % 10 * 10000 + windows.Revision).ToString(CultureInfo.InvariantCulture);
            installed = digits.Insert(digits.Length - 2, ".");
        }

        if (package.Adapter.Vendor == GpuVendor.Amd)
        {
            // AMD publishes several Windows versions in one release, for different architectures.
            var versions = Regex.Matches(package.ReleaseNotes, @"Windows Driver Store Version\s+(\d+\.\d+\.\d+\.\d+)")
                .Select(match => System.Version.Parse(match.Groups[1].Value)).Order().ToArray();
            if (!System.Version.TryParse(installed, out var currentAmd) || versions.Length == 0)
            {
                throw new InvalidDataException("AMD's driver-store versions could not be compared with the installed driver.");
            }

            if (versions.Contains(currentAmd) || currentAmd > versions[^1])
            {
                return false;
            }

            if (currentAmd < versions[0])
            {
                return true;
            }

            return package.Adapter.Date is { } installedDate
                ? installedDate.Date < package.Date.Date
                : throw new InvalidDataException("AMD's multi-architecture release needs an installed driver date to compare.");
        }

        if (!System.Version.TryParse(NumericVersion(package.Version), out var available)
            || !System.Version.TryParse(installed, out var current))
        {
            throw new InvalidDataException("The installed and available driver versions could not be compared.");
        }

        return available > current;
    }

    private async Task<GpuDriverPackage> IntelAsync(GpuInstalledDriver adapter, CancellationToken token)
    {
        using var response = await SendAsync(new Uri("https://dsadata.intel.com/data/en"), HttpMethod.Get, token)
            .ConfigureAwait(false);
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var data = new MemoryStream();
        await CopyBoundedAsync(input, data, 8 * 1024 * 1024, token).ConfigureAwait(false);
        data.Position = 0;
        using var archive = new ZipArchive(data);
        var entry = archive.GetEntry("software-configurations.json")
                    ?? throw new InvalidDataException("Intel DSA did not supply its software catalog.");
        if (entry.Length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException("Intel DSA's catalog is unexpectedly large.");
        }

        await using var json = entry.Open();
        using var catalog = await JsonDocument.ParseAsync(json, cancellationToken: token).ConfigureAwait(false);
        var os = Environment.OSVersion.Version.Build >= 22000 ? "windows-11-" : "windows-10-";
        var matches = catalog.RootElement.EnumerateArray().Where(item => !item.GetProperty("IsBeta").GetBoolean()
            && !item.GetProperty("IntelSystemOnly").GetBoolean()
            && item.GetProperty("BoardNumbers").GetArrayLength() == 0
            && item.GetProperty("ModelNumbers").GetArrayLength() == 0
            && item.GetProperty("Components").EnumerateArray().Any(component =>
                component.GetProperty("Category").GetString() == "Graphics"
                && component.GetProperty("DetectionValues").EnumerateArray().Any(value =>
                    adapter.HardwareId.Equals(value.GetString(), StringComparison.OrdinalIgnoreCase)
                    || adapter.HardwareId.StartsWith(value.GetString() + "&", StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(item => item.GetProperty("DisplayReleaseDate").GetDateTime());
        foreach (var item in matches)
        {
            var file = item.GetProperty("Files").EnumerateArray().FirstOrDefault(file =>
                file.GetProperty("Url").GetString()!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && file.GetProperty("OperatingSystems").EnumerateArray().Any(value =>
                    value.GetString()!.StartsWith(os, StringComparison.Ordinal)
                    && value.GetString()!.EndsWith("-64", StringComparison.Ordinal)));
            if (file.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }

            var hash = file.GetProperty("Hash").GetString();
            if (string.IsNullOrEmpty(hash) || hash.Length > 128 || !hash.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException("Intel DSA's package checksum is missing or invalid.");
            }

            // DSA's SHA1 is serialized as a number: leading zeroes can be omitted in this feed.
            hash = hash.Length <= 40 ? hash.PadLeft(40, '0') : hash;
            var details = new Uri(item.GetProperty("Url").GetString()!);
            return new GpuDriverPackage(adapter, NumericVersion(item.GetProperty("Version").GetString()!),
                item.GetProperty("DisplayReleaseDate").GetDateTime(),
                CheckedUri(file.GetProperty("Url").GetString()!), details,
                Plain(item.GetProperty("ExtendedDetails").GetString()!), file.GetProperty("Size").GetInt64(),
                hash, "-s");
        }

        throw new NotSupportedException("Intel DSA lists no compatible Windows x64 graphics package for this adapter.");
    }

    private async Task<GpuDriverPackage> NvidiaAsync(GpuInstalledDriver adapter, CancellationToken token)
    {
        const string lookup = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=";
        var types = XDocument.Parse(await TextAsync(lookup + "1", token).ConfigureAwait(false));
        foreach (var type in types.Descendants("LookupValue"))
        {
            var series = XDocument.Parse(await TextAsync(lookup + "2&ParentID=" + type.Element("Value")!.Value,
                token).ConfigureAwait(false));
            foreach (var group in series.Descendants("LookupValue"))
            {
                var groupName = group.Element("Name")!.Value;
                // Avoid downloading unrelated families. The exact product comparison below remains authoritative.
                var family = Regex.Match(adapter.Name, @"(?:RTX|GTX|GT)\s+(\d{2})", RegexOptions.IgnoreCase);
                if (family.Success && !groupName.Contains(family.Groups[1].Value, StringComparison.Ordinal)
                                   && !groupName.Contains("TITAN", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var products = XDocument.Parse(await TextAsync(lookup + "3&ParentID=" + group.Element("Value")!.Value,
                    token).ConfigureAwait(false));
                var product = products.Descendants("LookupValue").FirstOrDefault(value =>
                    NormalizeName(value.Element("Name")!.Value) == NormalizeName(adapter.Name));
                if (product is null)
                {
                    continue;
                }

                var query = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php"
                            + "?func=DriverManualLookup&pfid=" + product.Element("Value")!.Value
                            + "&psid=" + group.Element("Value")!.Value
                            + "&osID=" + (Environment.OSVersion.Version.Build >= 22000 ? "135" : "57")
                            + "&languageCode=1033&isWHQL=1&dch=1&sort1=1&numberOfResults=1";
                using var json = JsonDocument.Parse(await TextAsync(query, token).ConfigureAwait(false));
                if (json.RootElement.GetProperty("Success").GetString() != "1")
                {
                    throw new NotSupportedException("NVIDIA lists no compatible WHQL driver for this adapter and OS.");
                }

                var info = json.RootElement.GetProperty("IDS")[0].GetProperty("downloadInfo");
                var url = CheckedUri(info.GetProperty("DownloadURL").GetString()!);
                return new GpuDriverPackage(adapter, info.GetProperty("Version").GetString()!,
                    DateTime.Parse(info.GetProperty("ReleaseDateTime").GetString()!, CultureInfo.InvariantCulture),
                    url, CheckedUri(info.GetProperty("DetailsURL").GetString()!),
                    Plain(Uri.UnescapeDataString(info.GetProperty("ReleaseNotes").GetString()!)),
                    await SizeAsync(url, token).ConfigureAwait(false), null, "-s -n Display.Driver");
            }
        }

        throw new NotSupportedException("NVIDIA's product selector could not identify this adapter unambiguously.");
    }

    private async Task<GpuDriverPackage> AmdAsync(GpuInstalledDriver adapter, CancellationToken token)
    {
        var selector = await TextAsync("https://www.amd.com/en/support/download/drivers.html", token)
            .ConfigureAwait(false);
        var match = Regex.Match(selector, "data-component=\"coveo-browse-documentation\"\\s+data-json=\"([^\"]+)\"");
        if (!match.Success)
        {
            throw new InvalidDataException("AMD's product selector format changed. No package was selected.");
        }

        using var catalog = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
        var candidates = Nodes(catalog.RootElement).Where(node => node.TryGetProperty("title", out var title)
            && NormalizeName(title.GetString()!) == NormalizeName(adapter.Name)
            && node.TryGetProperty("url", out _)).ToArray();
        var urls = candidates.Select(node => node.GetProperty("url").GetString()!).Distinct().ToArray();
        if (urls.Length != 1)
        {
            throw new NotSupportedException("AMD's product selector could not identify this adapter unambiguously. Use the OEM updater for this model.");
        }

        var details = CheckedUri(urls[0]);
        var page = await TextAsync(details.AbsoluteUri, token).ConfigureAwait(false);
        var packages = Regex.Matches(page, @"<article\b[^>]*class=""[^""]*driver-download-details[^""]*""[\s\S]*?</article>");
        foreach (Match article in packages)
        {
            var download = Regex.Match(article.Value, @"https://drivers\.amd\.com/[^""\s<>]+\.exe");
            var version = Regex.Match(Plain(article.Value), @"Adrenalin\s+(\d+\.\d+\.\d+)");
            var notesLink = Regex.Match(article.Value, @"/en/resources/support-articles/release-notes/RN-RAD-WIN-[^""<>]+\.html");
            var date = Regex.Match(article.Value, @"\b20\d{2}-\d{2}-\d{2}\b");
            if (!download.Success || !version.Success || !notesLink.Success || !date.Success
                || download.Value.Contains("minimalsetup", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var notes = await TextAsync("https://www.amd.com" + notesLink.Value, token).ConfigureAwait(false);
            var url = CheckedUri(WebUtility.HtmlDecode(download.Value));
            return new GpuDriverPackage(adapter, version.Groups[1].Value,
                DateTime.Parse(date.Value, CultureInfo.InvariantCulture), url, details, Plain(notes),
                await SizeAsync(url, token).ConfigureAwait(false), null, "-INSTALL");
        }

        throw new NotSupportedException("AMD lists no supported Windows installer for this adapter.");
    }

    private static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            yield return node;
            foreach (var property in node.EnumerateObject())
            {
                foreach (var child in Nodes(property.Value))
                {
                    yield return child;
                }
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            {
                foreach (var child in Nodes(item))
                {
                    yield return child;
                }
            }
        }
    }

    private async Task<string> TextAsync(string url, CancellationToken token)
    {
        using var response = await SendAsync(CheckedUri(url), HttpMethod.Get, token).ConfigureAwait(false);
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        await CopyBoundedAsync(input, output, 8 * 1024 * 1024, token).ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    private async Task<long> SizeAsync(Uri url, CancellationToken token)
    {
        using var response = await SendAsync(url, HttpMethod.Head, token).ConfigureAwait(false);
        return response.Content.Headers.ContentLength is > 0 and < 4L * 1024 * 1024 * 1024
            ? response.Content.Headers.ContentLength.Value
            : throw new InvalidDataException("The official server did not supply a safe package size.");
    }

    internal async Task<HttpResponseMessage> SendAsync(Uri url, HttpMethod method, CancellationToken token)
    {
        for (var redirects = 0; redirects < 6; redirects++)
        {
            CheckedUri(url.AbsoluteUri);
            using var request = new HttpRequestMessage(method, url);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
            {
                url = new Uri(url, next);
                response.Dispose();
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"The official driver server returned HTTP {(int)status}.");
            }

            return response;
        }

        throw new HttpRequestException("The official driver server redirected too many times.");
    }

    internal static Uri CheckedUri(string value)
    {
        var url = new Uri(value, UriKind.Absolute);
        string[] hosts = ["dsadata.intel.com", "downloadmirror.intel.com", "www.intel.com", "www.amd.com",
            "drivers.amd.com", "www2.ati.com", "www.nvidia.com", "gfwsl.geforce.com", "us.download.nvidia.com",
            "international.download.nvidia.com"];
        if (url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.UserInfo.Length > 0
                                               || !hosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The driver source is not an allowed official HTTPS endpoint.");
        }

        return url;
    }

    internal static async Task CopyBoundedAsync(Stream input, Stream output, long limit, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            total += count;
            if (total > limit)
            {
                throw new InvalidDataException("The vendor response exceeded its expected size.");
            }

            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
    }

    internal static string Plain(string html) => WebUtility.HtmlDecode(Regex.Replace(
        Regex.Replace(html, @"<(script|style)\b[\s\S]*?</\1>", "", RegexOptions.IgnoreCase), "<[^>]+>", " "));
    private static string NumericVersion(string value) => Regex.Match(value, @"\d+\.\d+(?:\.\d+){0,2}").Value;
    private static string NormalizeName(string value) => Regex.Replace(value.ToLowerInvariant(),
        @"\b(nvidia|amd)\b|\(tm\)|\(r\)|[^a-z0-9]", "");

    /// <summary>Releases owned network resources.</summary>
    public void Dispose() => _http.Dispose();
}
