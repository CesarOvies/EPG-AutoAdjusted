using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EpgGenerator;

public static partial class ChannelNormalizer
{
    // Strips common resolution/codec tokens and backup superscripts (¹²³) at the end of channel names
    [GeneratedRegex(@"(?:\s+(?:FHD|UHD|QHD|HD|SD|4K|H\.?265|HEVC)[¹²³]*)+[¹²³]*\s*|[¹²³]+\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TrailingQualityRegex();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex NonAlphanumericRegex();

    public static string ExtractBaseName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return string.Empty;

        var cleaned = TrailingQualityRegex().Replace(rawName.Trim(), "").Trim();
        return string.IsNullOrEmpty(cleaned) ? rawName.Trim() : cleaned;
    }

    public static string ToCanonicalId(string rawName)
    {
        var baseName = ExtractBaseName(rawName);
        var normalized = RemoveDiacritics(baseName).ToLowerInvariant();
        var slug = NonAlphanumericRegex().Replace(normalized, "").Trim();
        return string.IsNullOrEmpty(slug) ? "unknown.br" : $"{slug}.br";
    }

    public static IReadOnlyList<string> BuildDisplayNames(MergedChannel channel, AliasConfig config)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;
            var trimmed = candidate.Trim();
            if (seen.Add(trimmed))
                result.Add(trimmed);
        }

        var seeds = new List<string> { channel.BaseName };
        foreach (var raw in channel.RawNames)
        {
            var rawBase = ExtractBaseName(raw);
            if (!string.IsNullOrWhiteSpace(rawBase))
                seeds.Add(rawBase);
        }

        if (config.CustomAliases.TryGetValue(channel.BaseName, out var customList))
        {
            seeds.AddRange(customList);
        }

        // Include ASCII (no-accent) and UPPERCASE variations for case-sensitive IPTV players
        var expandedSeeds = new List<string>();
        foreach (var s in seeds)
        {
            expandedSeeds.Add(s);
            expandedSeeds.Add(s.ToUpperInvariant());
            var ascii = RemoveDiacritics(s);
            expandedSeeds.Add(ascii);
            expandedSeeds.Add(ascii.ToUpperInvariant());
        }

        // First add base names and raw operator names
        foreach (var seed in expandedSeeds)
            Add(seed);
        foreach (var raw in channel.RawNames)
        {
            Add(raw);
            Add(raw.ToUpperInvariant());
        }

        // Expand each seed with every resolution/codec suffix (SD, HD, FHD, FHD¹, 4K, H265...)
        // Skip suffix expansion for dotted tvg-id aliases (e.g. "Adult.Swim.ca2") to keep XML lean
        foreach (var seed in expandedSeeds.Distinct(StringComparer.Ordinal))
        {
            if (seed.Contains('.'))
                continue;

            Add($"{seed}¹");
            Add($"{seed}²");
            Add($"{seed}³");
            foreach (var suffix in config.Suffixes)
            {
                Add($"{seed} {suffix}");
                Add($"{seed} [{suffix}]");
                Add($"{seed} ({suffix})");
            }
        }

        return result;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

