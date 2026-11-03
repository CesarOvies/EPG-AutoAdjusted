using System.Diagnostics;
using System.Net;
using EpgGenerator;
using EpgGenerator.Providers;

var options = ParseArgs(args);

if (options.SelfCheck)
{
    RunSelfCheck();
    return 0;
}

var aliasConfig = AliasConfig.Load(options.AliasesPath);

using var handler = new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
};
using var http = new HttpClient(handler)
{
    Timeout = TimeSpan.FromSeconds(30)
};
http.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");

// Providers in priority cascade: ClaroTV+ first, VivoPlay fills missing channels/schedules
IEpgProvider[] providers =
[
    new ClaroTvProvider(http),
    new VivoPlayProvider(http)
];

var nowUtc = DateTimeOffset.UtcNow;
var startUtc = nowUtc.AddHours(-2);
var endUtc = nowUtc.AddDays(options.Days);

Console.WriteLine($"[EPG] Window: {startUtc:u} -> {endUtc:u} ({options.Days} day(s))");

var validChannels = await EpgAggregator.CollectAndMergeAsync(providers, startUtc, endUtc);
int totalProgrammes = validChannels.Sum(c => c.Programmes.Count);

Console.WriteLine($"[EPG] Merged result: {validChannels.Count} active channels, {totalProgrammes} total programmes.");

// Safety gate: never overwrite existing EPG if all upstream APIs failed
if (!EpgAggregator.PassesSafetyGate(validChannels))
{
    Console.Error.WriteLine("[EPG] ERROR: Upstream returned insufficient data. Aborting overwrite to keep existing EPG safe.");
    return 1;
}

var xmlPath = Path.Combine(options.OutputDir, "epg.xml");
var gzPath = Path.Combine(options.OutputDir, "epg.xml.gz");

XmlTvWriter.Write(validChannels, aliasConfig, xmlPath, gzPath);

var xmlSizeMb = new FileInfo(xmlPath).Length / (1024.0 * 1024.0);
var gzSizeMb = new FileInfo(gzPath).Length / (1024.0 * 1024.0);
Console.WriteLine($"[EPG] Saved: {xmlPath} ({xmlSizeMb:F2} MB) | {gzPath} ({gzSizeMb:F2} MB)");
return 0;

static CliOptions ParseArgs(string[] args)
{
    int days = 2;
    string outputDir = "output";
    string aliasesPath = "aliases.json";
    bool selfCheck = false;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--days" when i + 1 < args.Length && int.TryParse(args[++i], out var d):
                days = Math.Clamp(d, 1, 7);
                break;
            case "--output" when i + 1 < args.Length:
                outputDir = args[++i];
                break;
            case "--aliases" when i + 1 < args.Length:
                aliasesPath = args[++i];
                break;
            case "--self-check":
                selfCheck = true;
                break;
        }
    }

    return new CliOptions(days, outputDir, aliasesPath, selfCheck);
}

static void RunSelfCheck()
{
    Debug.Assert(ChannelNormalizer.ExtractBaseName("AMC FHD H265") == "AMC");
    Debug.Assert(ChannelNormalizer.ExtractBaseName("BAND HD") == "BAND");
    Debug.Assert(ChannelNormalizer.ToCanonicalId("BAND HD") == "band.br");
    Debug.Assert(ChannelNormalizer.ToCanonicalId("TNT Séries FHD") == "tntseries.br");

    var cfg = new AliasConfig();
    var ch = new MergedChannel { Id = "amc.br", BaseName = "AMC" };
    ch.RawNames.Add("AMC HD");
    var names = ChannelNormalizer.BuildDisplayNames(ch, cfg);

    Debug.Assert(names.Contains("AMC"));
    Debug.Assert(names.Contains("AMC SD"));
    Debug.Assert(names.Contains("AMC HD"));
    Debug.Assert(names.Contains("AMC FHD"));
    Debug.Assert(names.Contains("AMC 4K"));
    Debug.Assert(names.Contains("AMC FHD H265"));

    Console.WriteLine($"[SELF-CHECK] OK ({names.Count} display-name variations generated for AMC)");
}

sealed record CliOptions(int Days, string OutputDir, string AliasesPath, bool SelfCheck);
