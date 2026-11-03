using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;
using EpgGenerator.Providers;
using Xunit;

namespace EpgGenerator.Tests;

public class EpgGeneratorTests
{
    [Theory]
    [InlineData("AMC HD", "AMC")]
    [InlineData("AMC FHD H265", "AMC")]
    [InlineData("TNT Séries FHD", "TNT Séries")]
    [InlineData("DISCOVERY 4K UHD", "DISCOVERY")]
    [InlineData("GLOBO SP", "GLOBO SP")]
    public void ExtractBaseName_StripsTrailingResolutionAndCodec(string raw, string expected)
    {
        Assert.Equal(expected, ChannelNormalizer.ExtractBaseName(raw));
    }

    [Theory]
    [InlineData("BAND HD", "band.br")]
    [InlineData("TNT Séries FHD", "tntseries.br")]
    [InlineData("A&E HD", "ae.br")]
    [InlineData("History 2 H265", "history2.br")]
    public void ToCanonicalId_NormalizesAccentsAndSymbols(string raw, string expectedId)
    {
        Assert.Equal(expectedId, ChannelNormalizer.ToCanonicalId(raw));
    }

    [Fact]
    public void BuildDisplayNames_IncludesAllVariantsAndCustomAliases()
    {
        var cfg = new AliasConfig
        {
            Suffixes = ["SD", "HD", "FHD"],
            CustomAliases = new(StringComparer.OrdinalIgnoreCase)
            {
                ["GLOBO SP"] = ["Globo", "Rede Globo"]
            }
        };

        var ch = new MergedChannel { Id = "globosp.br", BaseName = "GLOBO SP" };
        ch.RawNames.Add("GLOBO SP HD");

        var names = ChannelNormalizer.BuildDisplayNames(ch, cfg);

        Assert.Contains("GLOBO SP", names);
        Assert.Contains("GLOBO SP FHD", names);
        Assert.Contains("GLOBO SP [HD]", names);
        Assert.Contains("GLOBO SP (HD)", names);
        Assert.Contains("Globo", names);
        Assert.Contains("Globo FHD", names);
        Assert.Contains("Rede Globo HD", names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void MergeProviderChannels_PrimaryWinsAndSecondaryFillsFallback()
    {
        var merged = new Dictionary<string, MergedChannel>(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        var claroChannels = new List<ChannelData>
        {
            new("1", "AMC HD", "https://claro/amc.png", "Filmes",
                [new ProgrammeData("Filme Claro", null, null, now, now.AddHours(1), null, null, null, null)]),
            new("2", "AXN HD", null, "Series", []) // Empty schedule on primary
        };

        var vivoChannels = new List<ChannelData>
        {
            new("LCH1", "AMC", "https://vivo/amc.png", null,
                [new ProgrammeData("Filme Vivo", null, null, now, now.AddHours(1), null, null, null, null)]),
            new("LCH2", "AXN", "https://vivo/axn.png", null,
                [new ProgrammeData("Serie Vivo Fallback", null, null, now, now.AddHours(1), null, null, null, null)]),
            new("LCH3", "SONY CHANNEL", "https://vivo/sony.png", null,
                [new ProgrammeData("Serie Sony", null, null, now, now.AddHours(1), null, null, null, null)])
        };

        EpgAggregator.MergeProviderChannels(merged, "ClaroTV+", claroChannels);
        EpgAggregator.MergeProviderChannels(merged, "VivoPlay", vivoChannels);

        // AMC keeps Claro schedule
        Assert.Equal("ClaroTV+", merged["amc.br"].PrimarySource);
        Assert.Equal("Filme Claro", merged["amc.br"].Programmes[0].Title);

        // AXN had empty schedule on Claro, so VivoPlay fallback takes over
        Assert.Equal("VivoPlay", merged["axn.br"].PrimarySource);
        Assert.Single(merged["axn.br"].Programmes);
        Assert.Equal("Serie Vivo Fallback", merged["axn.br"].Programmes[0].Title);
        Assert.Equal("https://vivo/axn.png", merged["axn.br"].LogoUrl);

        // SONY added exclusively from VivoPlay
        Assert.True(merged.ContainsKey("sonychannel.br"));
    }

    [Fact]
    public void SafetyGate_BlocksInsufficientData()
    {
        var now = DateTimeOffset.UtcNow;
        var oneChannel = new List<MergedChannel>
        {
            new()
            {
                Id = "amc.br",
                BaseName = "AMC",
                Programmes = [new ProgrammeData("P1", null, null, now, now.AddHours(1), null, null, null, null)]
            }
        };

        Assert.False(EpgAggregator.PassesSafetyGate(oneChannel, minChannels: 2, minProgrammes: 5));
        Assert.True(EpgAggregator.PassesSafetyGate(oneChannel, minChannels: 1, minProgrammes: 1));
    }

    [Fact]
    public async Task ClaroTvProvider_ParsesValidScheduleAndIgnoresSinglePlaceholder()
    {
        const string catalogJson = """
        {
          "response": {
            "liveChannels": [
              { "id": 10, "name": "AMC HD", "logo": "https://img/amc.png", "type": "FILMES" },
              { "id": 20, "name": "OFFLINE CH", "logo": "", "type": "VARIEDADES" }
            ]
          }
        }
        """;

        const string scheduleJson = """
        {
          "response": {
            "liveChannels": [
              {
                "id": 10,
                "schedules": [
                  {
                    "title": "Breaking Bad",
                    "episodeName": "Pilot",
                    "description": " Química.",
                    "startTime": 1700000000,
                    "endTime": 1700003600,
                    "image": "https://cdn/{{image-size-placeholder}}/1.jpg",
                    "seasonNumber": 1,
                    "episodeNumber": 2
                  },
                  {
                    "title": "Better Call Saul",
                    "startTime": 1700003600,
                    "endTime": 1700007200
                  }
                ]
              },
              {
                "id": 20,
                "schedules": [
                  {
                    "title": "Grade Indisponível",
                    "startTime": 1700000000,
                    "endTime": 1700086400
                  }
                ]
              }
            ]
          }
        }
        """;

        using var http = new HttpClient(new FakeHttpMessageHandler(req =>
        {
            var query = req.RequestUri?.Query ?? "";
            var body = query.Contains("channelIds=&") ? catalogJson : scheduleJson;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }));

        var provider = new ClaroTvProvider(http);
        var result = await provider.FetchAsync(
            DateTimeOffset.FromUnixTimeSeconds(1700000000),
            DateTimeOffset.FromUnixTimeSeconds(1700010000));

        Assert.Equal(2, result.Count);
        var amc = result.Single(c => c.SourceId == "10");
        Assert.Equal(2, amc.Programmes.Count);
        Assert.Equal("https://cdn/420_236/1.jpg", amc.Programmes[0].IconUrl);
        Assert.Equal(1, amc.Programmes[0].Season);
        Assert.Equal(2, amc.Programmes[0].Episode);

        // Channel 20 had only 1 schedule item (placeholder), so it must be cleared
        var offline = result.Single(c => c.SourceId == "20");
        Assert.Empty(offline.Programmes);
    }

    [Fact]
    public void XmlTvWriter_GeneratesValidXmlAndGzip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "epg-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var xmlPath = Path.Combine(tempDir, "epg.xml");
            var gzPath = Path.Combine(tempDir, "epg.xml.gz");

            var start = new DateTimeOffset(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);
            var ch = new MergedChannel
            {
                Id = "amc.br",
                BaseName = "AMC",
                LogoUrl = "https://logo/amc.png",
                Programmes =
                [
                    new ProgrammeData("Filme Teste", "Subtitulo", "Sinopse", start, start.AddHours(2), "Filmes", "https://icon.jpg", 2, 5)
                ]
            };

            XmlTvWriter.Write([ch], new AliasConfig { Suffixes = ["HD"] }, xmlPath, gzPath);

            Assert.True(File.Exists(xmlPath));
            Assert.True(File.Exists(gzPath));

            var doc = XDocument.Load(xmlPath);
            var channelEl = doc.Root!.Element("channel")!;
            Assert.Equal("amc.br", channelEl.Attribute("id")?.Value);
            Assert.Contains(channelEl.Elements("display-name").Select(e => e.Value), n => n == "AMC HD");

            var progEl = doc.Root.Element("programme")!;
            Assert.Equal("20261103120000 -0300", progEl.Attribute("start")?.Value);
            Assert.Equal("1.4.", progEl.Element("episode-num")?.Value);

            // Verify GZip integrity
            using var fs = File.OpenRead(gzPath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var sr = new StreamReader(gz, Encoding.UTF8);
            var decompressed = sr.ReadToEnd();
            Assert.Equal(File.ReadAllText(xmlPath), decompressed);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void RepositoryAliasesJson_IsValidAndNonEmpty()
    {
        var rootAliases = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "aliases.json"));
        Assert.True(File.Exists(rootAliases), $"Expected aliases.json at {rootAliases}");

        var cfg = AliasConfig.Load(rootAliases);
        Assert.NotEmpty(cfg.Suffixes);
        Assert.Contains("FHD", cfg.Suffixes);
        Assert.NotEmpty(cfg.CustomAliases);
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}

