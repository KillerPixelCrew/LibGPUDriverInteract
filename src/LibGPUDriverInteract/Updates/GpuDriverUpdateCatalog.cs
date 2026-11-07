using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LibGPUDriverInteract;

/// <summary>Adapter-specific updates from Intel DSA and the official AMD and NVIDIA driver selectors.</summary>
public sealed partial class GpuDriverUpdateCatalog : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>Creates an HTTP owner; no vendor application or device driver is started.</summary>
    public GpuDriverUpdateCatalog() : this(new HttpClientHandler { AllowAutoRedirect = false })
    {
    }

    internal GpuDriverUpdateCatalog(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LibGPUDriverInteract/1.0");
    }

    /// <summary>Finds the latest compatible package; throws a clear refusal when the catalog cannot identify it.</summary>
    public async Task<GpuDriverPackage> CheckAsync(GpuInstalledDriver adapter, CancellationToken token = default)
        => await CheckAsync(adapter, includeNonWhql: false, token).ConfigureAwait(false);

    /// <summary>Finds the latest compatible official release in the requested certification channel.</summary>
    public async Task<GpuDriverPackage> CheckAsync(GpuInstalledDriver adapter, bool includeNonWhql, CancellationToken token = default)
    {
        return adapter.Vendor switch
        {
            GpuVendor.Intel => await IntelAsync(adapter, includeNonWhql, token).ConfigureAwait(false),
            GpuVendor.Amd => await AmdAsync(adapter, includeNonWhql, token).ConfigureAwait(false),
            GpuVendor.Nvidia => await NvidiaAsync(adapter, includeNonWhql, token).ConfigureAwait(false),
            _ => throw new NotSupportedException("This GPU vendor has no update source.")
        };
    }

    /// <summary>Whether the selected package is newer than the adapter's installed driver.</summary>
    /// <returns>True for newer, false for current/newer installed, null for an ambiguous AMD branch without a driver date.</returns>
    public static bool? IsNewer(GpuDriverPackage package) =>
        GpuDriverUpdateProfile.For(package.Adapter.Vendor).IsNewer(package);

    private async Task<GpuDriverPackage> IntelAsync(GpuInstalledDriver adapter, bool includeNonWhql, CancellationToken token)
    {
        using var data = await BytesAsync("https://dsadata.intel.com/data/en", token).ConfigureAwait(false);
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
        var matches = catalog.RootElement.EnumerateArray().Where(item => !item.GetProperty("IntelSystemOnly").GetBoolean()
            && item.GetProperty("BoardNumbers").GetArrayLength() == 0
            && item.GetProperty("ModelNumbers").GetArrayLength() == 0
            && item.GetProperty("Components").EnumerateArray().Any(component =>
                component.GetProperty("Category").GetString() == "Graphics"
                && component.GetProperty("DetectionValues").EnumerateArray().Any(value =>
                    adapter.HardwareId.Equals(value.GetString(), StringComparison.OrdinalIgnoreCase)
                    || adapter.HardwareId.StartsWith(value.GetString() + "&", StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(item => item.GetProperty("DisplayReleaseDate").GetDateTime());
        var packages = new List<GpuDriverPackage>();
        foreach (var item in matches)
        {
            var file = item.GetProperty("Files").EnumerateArray().FirstOrDefault(file =>
                file.GetProperty("Url").GetString()!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && file.GetProperty("OperatingSystems").EnumerateArray().Any(value =>
                    value.GetString()!.StartsWith(os, StringComparison.Ordinal)
                    && value.GetString()!.EndsWith("-64", StringComparison.Ordinal)));
            if (file.ValueKind == JsonValueKind.Undefined || item.GetProperty("Url").GetString() is not { } address)
            {
                continue;
            }

            var details = CheckedUri(address);
            // DSA supplies PCI compatibility, but its release snapshot can lag Download Center.
            // Retain the product branch and drop only its pinned revision when asking for latest.
            var current = new Uri(Regex.Replace(details.AbsoluteUri, @"(/download/\d+)/\d+/", "$1/"));
            var page = await IntelPageAsync(current, token).ConfigureAwait(false);
            packages.Add(await IntelPagePackageAsync(adapter, page, current, token).ConfigureAwait(false));
            if (includeNonWhql)
            {
                var newerOption = Regex.Matches(page, @"<option\b[^>]*value=""([^""]+)""[^>]*>([^<]+)</option>",
                    RegexOptions.IgnoreCase).Cast<Match>().Where(option =>
                    Version.TryParse(NumericVersion(option.Groups[2].Value), out var version)
                    && version > Version.Parse(packages[0].Version))
                    .OrderByDescending(option => Version.Parse(NumericVersion(option.Groups[2].Value))).FirstOrDefault();
                if (newerOption is not null)
                {
                    var newer = new Uri(current, newerOption.Groups[1].Value);
                    var newerPage = await IntelPageAsync(newer, token).ConfigureAwait(false);
                    packages.Add(await IntelPagePackageAsync(adapter, newerPage, newer, token).ConfigureAwait(false));
                }
            }
            if (!packages[0].IsWhql && !includeNonWhql)
            {
                var certifiedOption = Regex.Matches(page, @"<option\b[^>]*value=""([^""]+)""[^>]*>([^<]+)</option>",
                    RegexOptions.IgnoreCase).Cast<Match>().FirstOrDefault(option =>
                    option.Groups[2].Value.Contains("WHQL", StringComparison.OrdinalIgnoreCase)
                    && !option.Groups[2].Value.Contains("Non-WHQL", StringComparison.OrdinalIgnoreCase));
                var stable = certifiedOption is null ? details : new Uri(current, certifiedOption.Groups[1].Value);
                var pinned = await IntelPageAsync(stable, token).ConfigureAwait(false);
                packages.Add(await IntelPagePackageAsync(adapter, pinned, stable, token).ConfigureAwait(false));
            }
            break;
        }

        return SelectRelease(packages, includeNonWhql, "Intel");
    }

    private async Task<string> IntelPageAsync(Uri url, CancellationToken token)
    {
        try
        {
            return await TextAsync(url.AbsoluteUri, token).ConfigureAwait(false);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Forbidden)
        {
            // Intel's official regional mirror publishes identical package metadata and SHA512.
            var mirror = new UriBuilder(url) { Host = "www.intel.cn",
                Path = url.AbsolutePath.Replace("/content/www/us/en/", "/content/www/cn/zh/", StringComparison.Ordinal) };
            return await TextAsync(mirror.Uri.AbsoluteUri, token).ConfigureAwait(false);
        }
    }

    private async Task<GpuDriverPackage> IntelPagePackageAsync(GpuInstalledDriver adapter, string page, Uri details,
        CancellationToken token)
    {
        var metadata = ParseIntelPage(page);
        return new GpuDriverPackage(adapter, metadata.Version, metadata.Date, metadata.Download, details,
            metadata.Notes, await SizeAsync(metadata.Download, token).ConfigureAwait(false), metadata.Checksum,
            metadata.IsWhql);
    }

    internal static IntelRelease ParseIntelPage(string page)
    {
        string Meta(string name)
        {
            var match = Regex.Match(page, "<meta\\s+name=\"" + name + "\"\\s+content=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value)
                : throw new InvalidDataException("Intel's download metadata is incomplete: " + name);
        }
        var version = NumericVersion(Meta("DownloadVersion"));
        var download = CheckedUri(Meta("RecommendedDownloadUrl"));
        var notes = Regex.Match(page, @"https://downloadmirror\.intel\.com/\d+/ReleaseNotes_[^""<>\s]+\.pdf",
            RegexOptions.IgnoreCase).Value;
        var nonWhql = notes.Contains("Non-WHQL", StringComparison.OrdinalIgnoreCase)
            || Meta("DownloadVersion").Contains("beta", StringComparison.OrdinalIgnoreCase);
        var whql = !nonWhql && (notes.Contains("_WHQL", StringComparison.OrdinalIgnoreCase)
            || Meta("DownloadVersion").Contains("WHQL", StringComparison.OrdinalIgnoreCase));
        var checksum = Regex.Match(Plain(page), @"SHA512\s*[:：]\s*([a-fA-F0-9]{128})");
        if (!checksum.Success || version.Length == 0 || (!whql && !nonWhql))
        {
            throw new InvalidDataException("Intel's release certification or SHA512 could not be established.");
        }
        return new IntelRelease(version, DateTime.ParseExact(Meta("LastUpdate"), "MM/dd/yyyy HH:mm:ss",
            CultureInfo.InvariantCulture), download, checksum.Groups[1].Value, whql, notes);
    }

    internal sealed record IntelRelease(string Version, DateTime Date, Uri Download, string Checksum, bool IsWhql, string Notes);

    internal static GpuDriverPackage SelectRelease(IEnumerable<GpuDriverPackage> packages, bool includeNonWhql, string vendor)
    {
        var all = packages.ToArray();
        var selected = all.Where(package => includeNonWhql || package.IsWhql)
            .OrderByDescending(package => package.Date).ThenByDescending(package => Version.Parse(package.Version))
            .ThenByDescending(package => package.IsWhql).FirstOrDefault()
            ?? throw new NotSupportedException(vendor + " lists no compatible " + (includeNonWhql ? "official" : "WHQL") + " release.");
        return selected with { ChannelDetail = includeNonWhql && all.All(package => package.IsWhql)
            ? vendor + " lists no newer official non-WHQL release for this adapter in its current catalog." : null };
    }

    private async Task<GpuDriverPackage> NvidiaAsync(GpuInstalledDriver adapter, bool includeNonWhql, CancellationToken token)
    {
        const string lookup = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=";
        var types = XDocument.Parse(await TextAsync(lookup + "1", token).ConfigureAwait(false));
        var adapterName = NormalizeName(adapter.Name);
        var family = NvidiaFamily().Match(adapter.Name);
        foreach (var type in types.Descendants("LookupValue").Where(type => NvidiaBrandMatches(adapterName,
                     NormalizeName(type.Element("Name")!.Value))))
        {
            var series = XDocument.Parse(await TextAsync(lookup + "2&ParentID=" + type.Element("Value")!.Value,
                token).ConfigureAwait(false));
            foreach (var group in series.Descendants("LookupValue"))
            {
                var groupName = group.Element("Name")!.Value;
                // Avoid downloading unrelated families. The exact product comparison below remains authoritative.
                if (family.Success && !groupName.Contains(family.Groups[1].Value, StringComparison.Ordinal)
                                   && !groupName.Contains("TITAN", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var products = XDocument.Parse(await TextAsync(lookup + "3&ParentID=" + group.Element("Value")!.Value,
                    token).ConfigureAwait(false));
                var product = products.Descendants("LookupValue").FirstOrDefault(value =>
                    NormalizeName(value.Element("Name")!.Value) == adapterName);
                if (product is null)
                {
                    continue;
                }

                var query = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php"
                            + "?func=DriverManualLookup&pfid=" + product.Element("Value")!.Value
                            + "&psid=" + group.Element("Value")!.Value
                            + "&osID=" + NvidiaWindowsOsId
                            + "&languageCode=1033&isWHQL=" + (includeNonWhql ? "0" : "1") + "&dch=1&sort1=1&numberOfResults=20";
                using var json = JsonDocument.Parse(await TextAsync(query, token).ConfigureAwait(false));
                if (!int.TryParse(json.RootElement.GetProperty("Success").GetString(), out var count) || count <= 0)
                {
                    throw new NotSupportedException("NVIDIA lists no compatible WHQL driver for this adapter and OS.");
                }

                var releases = new List<GpuDriverPackage>();
                foreach (var entry in json.RootElement.GetProperty("IDS").EnumerateArray())
                {
                    var info = entry.GetProperty("downloadInfo");
                    if (info.TryGetProperty("IsCRD", out var studio) && studio.GetString() == "1"
                        && info.GetProperty("IsBeta").GetString() != "1")
                    {
                        continue;
                    }
                    var url = CheckedUri(info.GetProperty("DownloadURL").GetString()!);
                    var whql = NvidiaWhql(info);
                    releases.Add(new GpuDriverPackage(adapter, info.GetProperty("Version").GetString()!,
                        ParseNvidiaDate(info.GetProperty("ReleaseDateTime").GetString()!), url,
                        CheckedUri(info.GetProperty("DetailsURL").GetString()!),
                        Plain(Uri.UnescapeDataString(info.GetProperty("ReleaseNotes").GetString()!)),
                        1, null, whql));
                }
                var selected = SelectRelease(releases, includeNonWhql, "NVIDIA");
                return new GpuDriverPackage(adapter, selected.Version, selected.Date, selected.Download, selected.Details,
                    selected.ReleaseNotes, await SizeAsync(selected.Download, token).ConfigureAwait(false), null, selected.IsWhql)
                    { ChannelDetail = selected.ChannelDetail };

            }
        }

        throw new NotSupportedException("NVIDIA's product selector could not identify this adapter unambiguously.");
    }

    private async Task<GpuDriverPackage> AmdAsync(GpuInstalledDriver adapter, bool includeNonWhql, CancellationToken token)
    {
        var referrer = new Uri("https://www.amd.com/");
        var adapterName = NormalizeName(adapter.Name);
        var selector = await TextAsync("https://www.amd.com/en/support/download/drivers.html", token, referrer)
            .ConfigureAwait(false);
        var match = Regex.Match(selector, "data-component=\"coveo-browse-documentation\"\\s+data-json=\"([^\"]+)\"");
        if (!match.Success)
        {
            throw new InvalidDataException("AMD's product selector format changed. No package was selected.");
        }

        using var catalog = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
        var candidates = Nodes(catalog.RootElement).Where(node => node.TryGetProperty("title", out var title)
            && NormalizeName(title.GetString()!) == adapterName
            && node.TryGetProperty("url", out _)).ToArray();
        var urls = candidates.Select(node => node.GetProperty("url").GetString()!).Distinct().ToArray();
        if (urls.Length != 1)
        {
            throw new NotSupportedException("AMD's product selector could not identify this adapter unambiguously. Use the OEM updater for this model.");
        }

        var details = CheckedUri(urls[0]);
        var page = await TextAsync(details.AbsoluteUri, token, referrer).ConfigureAwait(false);
        var packages = Regex.Matches(page, @"<article\b[^>]*class=""[^""]*driver-download-details[^""]*""[\s\S]*?</article>");
        foreach (var article in packages.Cast<Match>().OrderByDescending(article =>
                     Regex.Match(article.Value, @"\b20\d{2}-\d{2}-\d{2}\b").Value))
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

            var whql = AmdWhql(article.Value);
            if (!includeNonWhql && !whql)
            {
                continue;
            }
            var notes = await TextAsync("https://www.amd.com" + notesLink.Value, token, details).ConfigureAwait(false);
            var url = CheckedUri(WebUtility.HtmlDecode(download.Value));
            return new GpuDriverPackage(adapter, version.Groups[1].Value,
                DateTime.ParseExact(date.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture), url, details, Plain(notes),
                await SizeAsync(url, token, details).ConfigureAwait(false), null, whql)
                { ChannelDetail = includeNonWhql && packages.Cast<Match>().Where(item => item.Value.Contains("Adrenalin", StringComparison.Ordinal))
                    .All(item => AmdWhql(item.Value)) ? "AMD lists no newer official non-WHQL release for this adapter in its current catalog." : null };
        }

        throw new NotSupportedException("AMD lists no supported Windows installer for this adapter.");
    }

    internal static bool AmdWhql(string article) => Regex.IsMatch(Plain(article), @"\bWHQL\b", RegexOptions.IgnoreCase)
        && !Regex.IsMatch(Plain(article), @"non[- ]WHQL", RegexOptions.IgnoreCase);

    internal static bool NvidiaWhql(JsonElement info) => info.GetProperty("IsBeta").GetString() != "1"
        && (info.GetProperty("IsWHQL").GetString() == "1"
            || info.GetProperty("DownloadURL").GetString()!.EndsWith("-whql.exe", StringComparison.OrdinalIgnoreCase));

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

    private async Task<MemoryStream> BytesAsync(string url, CancellationToken token, Uri? referrer = null)
    {
        using var response = await SendAsync(CheckedUri(url), HttpMethod.Get, token, referrer).ConfigureAwait(false);
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var output = new MemoryStream();
        try
        {
            await CopyBoundedAsync(input, output, 8 * 1024 * 1024, token).ConfigureAwait(false);
            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private async Task<string> TextAsync(string url, CancellationToken token, Uri? referrer = null)
    {
        using var output = await BytesAsync(url, token, referrer).ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private async Task<long> SizeAsync(Uri url, CancellationToken token, Uri? referrer = null)
    {
        using var response = await SendAsync(url, HttpMethod.Head, token, referrer).ConfigureAwait(false);
        return response.Content.Headers.ContentLength is > 0 and < 4L * 1024 * 1024 * 1024
            ? response.Content.Headers.ContentLength.Value
            : throw new InvalidDataException("The official server did not supply a safe package size.");
    }

    internal async Task<HttpResponseMessage> SendAsync(Uri url, HttpMethod method, CancellationToken token, Uri? referrer = null)
    {
        for (var redirects = 0; redirects < 6; redirects++)
        {
            CheckedUri(url.AbsoluteUri);
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Referrer = referrer;
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
                throw new HttpRequestException($"The official driver server returned HTTP {(int)status}.", null, status);
            }

            return response;
        }

        throw new HttpRequestException("The official driver server redirected too many times.");
    }

    internal static Uri CheckedUri(string value)
    {
        var url = new Uri(value, UriKind.Absolute);
        string[] hosts = ["dsadata.intel.com", "www.intel.cn", "downloadmirror.intel.com", "www.intel.com", "www.amd.com",
            "drivers.amd.com", "www2.ati.com", "www.nvidia.com", "gfwsl.geforce.com", "us.download.nvidia.com",
            "international.download.nvidia.com"];
        if (url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.UserInfo.Length > 0
                                               || !hosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The driver source is not an allowed official HTTPS endpoint.");
        }

        return url;
    }

    internal static async Task CopyBoundedAsync(Stream input, Stream output, long limit, CancellationToken token,
        IProgress<int>? progress = null, System.Security.Cryptography.IncrementalHash? hash = null)
    {
        var buffer = new byte[81920];
        long total = 0;
        long nextReport = 0;
        var reported = -1;
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            total += count;
            if (total > limit)
            {
                throw new InvalidDataException("The vendor response exceeded its expected size.");
            }

            hash?.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            var percent = (int)(100 * total / limit);
            var now = Environment.TickCount64;
            if (progress is not null && percent != reported && (now >= nextReport || total == limit))
            {
                reported = percent;
                nextReport = now + 250;
                progress.Report(percent);
            }
        }
    }

    internal static string NormalizeChecksum(string? hash)
    {
        if (string.IsNullOrEmpty(hash) || hash.Length > 128 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Intel DSA's package checksum is missing or invalid.");
        }

        return hash.PadLeft(hash.Length <= 40 ? 40 : hash.Length <= 64 ? 64 : 128, '0');
    }

    internal static DateTime ParseNvidiaDate(string value) => DateTime.ParseExact(value,
        ["ddd MMM dd, yyyy", "ddd MMM d, yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static string NvidiaWindowsOsId => Environment.OSVersion.Version.Build >= 22000 ? "135" : "57";

    private static bool NvidiaBrandMatches(string adapterName, string productType)
    {
        if (adapterName.Contains("geforce", StringComparison.Ordinal))
        {
            return productType.Contains("geforce", StringComparison.Ordinal);
        }

        if (adapterName.Contains("titan", StringComparison.Ordinal))
        {
            return productType.Contains("titan", StringComparison.Ordinal);
        }

        if (adapterName.Contains("quadro", StringComparison.Ordinal) || adapterName.StartsWith("rtx", StringComparison.Ordinal))
        {
            return productType.Contains("quadro", StringComparison.Ordinal);
        }

        if (adapterName.Contains("tesla", StringComparison.Ordinal))
        {
            return productType.Contains("datacenter", StringComparison.Ordinal);
        }

        return true;
    }

    internal static string Plain(string html) => WebUtility.HtmlDecode(HtmlTags().Replace(ScriptStyle().Replace(html, ""), " "));
    private static string NumericVersion(string value) => VersionNumber().Match(value).Value;
    private static string NormalizeName(string value) => NameParts().Replace(value, "").ToLowerInvariant();

    [GeneratedRegex(@"<(script|style)\b[\s\S]*?</\1>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptStyle();
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTags();
    [GeneratedRegex(@"\d+\.\d+(?:\.\d+){0,2}")]
    private static partial Regex VersionNumber();
    [GeneratedRegex(@"\b(nvidia|amd)\b|\(tm\)|\(r\)|[^a-z0-9]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameParts();
    [GeneratedRegex(@"(?:RTX|GTX|GT)\s+(\d{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NvidiaFamily();

    /// <summary>Releases owned network resources.</summary>
    public void Dispose() => _http.Dispose();
}
