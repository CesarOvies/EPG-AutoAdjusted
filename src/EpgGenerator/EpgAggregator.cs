using System.Diagnostics;
using EpgGenerator.Providers;

namespace EpgGenerator;

public static class EpgAggregator
{
    public const int DefaultMinChannels = 10;
    public const int DefaultMinProgrammes = 100;

    public static async Task<List<MergedChannel>> CollectAndMergeAsync(
        IEnumerable<IEpgProvider> providers,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken ct = default)
    {
        var merged = new Dictionary<string, MergedChannel>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            var sw = Stopwatch.StartNew();
            Console.WriteLine($"[EPG] Fetching from {provider.Name}...");
            var channels = await provider.FetchAsync(startUtc, endUtc, ct);
            int withProgs = channels.Count(c => c.Programmes.Count > 0);
            Console.WriteLine($"[EPG] {provider.Name}: {channels.Count} channels ({withProgs} with active schedule) in {sw.Elapsed.TotalSeconds:F1}s");

            MergeProviderChannels(merged, provider.Name, channels);
        }

        return merged.Values.Where(c => c.Programmes.Count > 0).ToList();
    }

    public static void MergeProviderChannels(
        IDictionary<string, MergedChannel> merged,
        string providerName,
        IEnumerable<ChannelData> channels)
    {
        foreach (var ch in channels)
        {
            var canonicalId = ChannelNormalizer.ToCanonicalId(ch.Name);
            var baseName = ChannelNormalizer.ExtractBaseName(ch.Name);

            if (!merged.TryGetValue(canonicalId, out var target))
            {
                target = new MergedChannel
                {
                    Id = canonicalId,
                    BaseName = baseName,
                    LogoUrl = ch.LogoUrl,
                    PrimarySource = providerName
                };
                merged[canonicalId] = target;
            }

            target.RawNames.Add(ch.Name);
            if (string.IsNullOrWhiteSpace(target.LogoUrl) && !string.IsNullOrWhiteSpace(ch.LogoUrl))
                target.LogoUrl = ch.LogoUrl;

            // Primary provider wins; secondary provider acts as fallback when schedule is empty
            if (target.Programmes.Count == 0 && ch.Programmes.Count > 0)
            {
                target.Programmes = ch.Programmes;
                target.PrimarySource = providerName;
            }
        }
    }

    public static bool PassesSafetyGate(
        IReadOnlyCollection<MergedChannel> validChannels,
        int minChannels = DefaultMinChannels,
        int minProgrammes = DefaultMinProgrammes)
    {
        if (validChannels.Count < minChannels)
            return false;

        int totalProgrammes = validChannels.Sum(c => c.Programmes.Count);
        return totalProgrammes >= minProgrammes;
    }

    public static int AppendStaticChannels(
        List<MergedChannel> channels,
        AliasConfig config,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        if (config.StaticChannels.Count == 0)
            return 0;

        var existingIds = new HashSet<string>(channels.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        var existingAliases = new HashSet<string>(
            channels.SelectMany(c => ChannelNormalizer.BuildDisplayNames(c, config)),
            StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (var rawStatic in config.StaticChannels)
        {
            if (string.IsNullOrWhiteSpace(rawStatic))
                continue;

            var baseName = ChannelNormalizer.ExtractBaseName(rawStatic);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = rawStatic.Trim();

            var canonicalId = ChannelNormalizer.ToCanonicalId(baseName);
            if (existingIds.Contains(canonicalId) || existingAliases.Contains(baseName))
                continue;

            var programmes = new List<ProgrammeData>();
            for (var cursor = startUtc; cursor < endUtc; cursor = cursor.AddHours(6))
            {
                var next = cursor.AddHours(6);
                if (next > endUtc)
                    next = endUtc;

                programmes.Add(new ProgrammeData(
                    Title: baseName,
                    SubTitle: null,
                    Description: baseName,
                    Start: cursor,
                    Stop: next,
                    Category: "24H / Especial",
                    IconUrl: null,
                    Season: null,
                    Episode: null));
            }

            var staticChannel = new MergedChannel
            {
                Id = canonicalId,
                BaseName = baseName,
                PrimarySource = "Static",
                Programmes = programmes
            };
            staticChannel.RawNames.Add(rawStatic.Trim());

            channels.Add(staticChannel);
            existingIds.Add(canonicalId);
            existingAliases.Add(baseName);
            added++;
        }

        return added;
    }
}

