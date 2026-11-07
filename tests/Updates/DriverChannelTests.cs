using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace LibGPUDriverInteract.Tests.Updates;

public sealed class DriverChannelTests
{
    private static readonly GpuInstalledDriver Arc = new("arc", "VEN_8086&DEV_7D55", GpuVendor.Intel,
        "Intel Arc Graphics", "32.0.101.9150", null);

    [Theory]
    [InlineData(false, "VEN_8086&DEV_7D55")]
    [InlineData(true, "VEN_8086&DEV_7D55")]
    [InlineData(false, "VEN_8086&DEV_B083&SUBSYS_00001025&REV_00")]
    [InlineData(true, "VEN_8086&DEV_B083&SUBSYS_00001025&REV_00")]
    public async Task Intel9175ComesFromTheCurrentPageWhenDsaStillLists9150(bool beta, string hardwareId)
    {
        using var handler = new CatalogHandler("intel-9175.html");
        using var catalog = new GpuDriverUpdateCatalog(handler);
        var release = await catalog.CheckAsync(Arc with { HardwareId = hardwareId }, beta);
        Assert.Equal("32.0.101.9175", release.Version);
        Assert.True(release.IsWhql);
        Assert.Equal(128, release.Checksum!.Length);
        Assert.True(GpuDriverUpdateCatalog.IsNewer(release));
        Assert.Contains(handler.Requests, url => url.Host == "www.intel.cn");
    }

    [Fact]
    public async Task TheLegacyUhdAdapterDoesNotReceiveTheArcBranch()
    {
        using var handler = new CatalogHandler("intel-9175.html");
        using var catalog = new GpuDriverUpdateCatalog(handler);
        var release = await catalog.CheckAsync(Arc with { HardwareId = "VEN_8086&DEV_4688", Version = "32.0.101.7092" }, true);
        Assert.Equal("32.0.101.7092", release.Version);
        Assert.False(GpuDriverUpdateCatalog.IsNewer(release));
        Assert.DoesNotContain(handler.Requests, url => url.AbsolutePath.Contains("785597", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, "32.0.101.9033", true)]
    [InlineData(true, "32.0.101.9034", false)]
    public async Task IntelNonWhqlRequiresOptIn(bool beta, string version, bool whql)
    {
        using var handler = new CatalogHandler("intel-9034-beta.html");
        using var catalog = new GpuDriverUpdateCatalog(handler);
        var release = await catalog.CheckAsync(Arc, beta);
        Assert.Equal(version, release.Version);
        Assert.Equal(whql, release.IsWhql);
    }

    [Theory]
    [InlineData(false, "26.1.1", true)]
    [InlineData(true, "26.2.1", false)]
    public async Task AmdOptionalNonWhqlRequiresOptIn(bool beta, string version, bool whql)
    {
        using var handler = new CatalogHandler("intel-9175.html");
        using var catalog = new GpuDriverUpdateCatalog(handler);
        var release = await catalog.CheckAsync(Arc with { Vendor = GpuVendor.Amd, Name = "AMD Radeon RX 7800 XT" }, beta);
        Assert.Equal(version, release.Version);
        Assert.Equal(whql, release.IsWhql);
    }

    [Fact]
    public void AmdOptionalCanAlsoBeWhqlCertified()
    {
        Assert.True(GpuDriverUpdateCatalog.AmdWhql(Fixture("amd-whql-optional.html")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NvidiaReportsNoBetaWhenTheOfficialCatalogHasNoneAndKeepsGameReady(bool beta)
    {
        using var handler = new CatalogHandler("intel-9175.html");
        using var catalog = new GpuDriverUpdateCatalog(handler);
        var release = await catalog.CheckAsync(Arc with { Vendor = GpuVendor.Nvidia, Name = "NVIDIA GeForce RTX 4070" }, beta);
        Assert.True(release.IsWhql);
        Assert.DoesNotContain("nsd", release.Download.AbsoluteUri);
        Assert.Equal(beta, release.ChannelDetail is not null);
        Assert.Contains(handler.Requests, url => url.Query.Contains("isWHQL=" + (beta ? "0" : "1"), StringComparison.Ordinal));
    }

    [Fact]
    public void NvidiaBetaFlagCannotBeHiddenByAWhqlNamedFile()
    {
        // Exercise a beta record explicitly: the recorded current catalog contains no Windows beta.
        var record = JsonNode();
        record["IsBeta"] = "1";
        using var json = JsonDocument.Parse(record.ToJsonString());
        Assert.False(GpuDriverUpdateCatalog.NvidiaWhql(json.RootElement));
    }

    [Theory]
    [InlineData(GpuVendor.Intel)]
    [InlineData(GpuVendor.Amd)]
    [InlineData(GpuVendor.Nvidia)]
    public void AllVendorsFilterBeforeSelectingTheNewest(GpuVendor vendor)
    {
        var adapter = Arc with { Vendor = vendor };
        var certified = new GpuDriverPackage(adapter, "1.0", new DateTime(2026, 1, 1),
            new Uri("https://www.intel.com/one"), new Uri("https://www.intel.com/notes"), "", 1, null, true);
        var beta = new GpuDriverPackage(adapter, "2.0", new DateTime(2026, 2, 1),
            new Uri("https://www.intel.com/two"), certified.Details, "", 1, null, false);
        Assert.Equal(certified.Identity, GpuDriverUpdateCatalog.SelectRelease([beta, certified], false, vendor.ToString()).Identity);
        Assert.Equal(beta.Identity, GpuDriverUpdateCatalog.SelectRelease([beta, certified], true, vendor.ToString()).Identity);
    }

    private static System.Text.Json.Nodes.JsonObject JsonNode() => System.Text.Json.Nodes.JsonNode.Parse(
        Fixture("nvidia-all.json"))!["IDS"]![0]!["downloadInfo"]!.AsObject();

    private static string Fixture(string name)
    {
        using var stream = typeof(DriverChannelTests).Assembly.GetManifestResourceStream(
            "LibGPUDriverInteract.Tests.Updates.Fixtures." + name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class CatalogHandler(string intelPage) : HttpMessageHandler
    {
        internal List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri!;
            Requests.Add(url);
            HttpContent content;
            if (request.Method == HttpMethod.Head)
            {
                content = new ByteArrayContent([]);
                content.Headers.ContentLength = 1024;
            }
            else if (url.Host == "dsadata.intel.com")
            {
                using var output = new MemoryStream();
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    using var writer = new StreamWriter(zip.CreateEntry("software-configurations.json").Open());
                    writer.Write(Fixture("intel-dsa.json"));
                }
                content = new ByteArrayContent(output.ToArray());
            }
            else if (url.Host == "www.intel.com")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            }
            else if (url.Host == "www.intel.cn")
            {
                content = new StringContent(Fixture(url.AbsolutePath.Contains("864990", StringComparison.Ordinal)
                    ? "intel-7092.html" : url.AbsolutePath.Contains("929557", StringComparison.Ordinal) ? "intel-9033.html" : intelPage));
            }
            else if (url.Host == "www.amd.com")
            {
                content = new StringContent(url.AbsolutePath.EndsWith("drivers.html", StringComparison.Ordinal)
                    ? Fixture("amd-selector.html") : url.AbsolutePath.Contains("release-notes", StringComparison.Ordinal)
                    ? "Driver Store Version 32.0.1.0" : Fixture("amd-releases.html"));
            }
            else if (url.Host == "www.nvidia.com")
            {
                var name = url.Query.Contains("TypeID=1", StringComparison.Ordinal) ? "GeForce"
                    : url.Query.Contains("TypeID=2", StringComparison.Ordinal) ? "GeForce RTX 40 Series" : "GeForce RTX 4070";
                content = new StringContent("<Values><LookupValue><Name>" + name + "</Name><Value>999</Value></LookupValue></Values>");
            }
            else
            {
                content = new StringContent(Fixture("nvidia-all.json"), Encoding.UTF8, "application/json");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
