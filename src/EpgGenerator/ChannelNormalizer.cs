using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EpgGenerator;

public static partial class ChannelNormalizer
{
    // Strips common resolution/codec tokens at the end of operator channel names
    [GeneratedRegex(@"\s+(?:FHD|UHD|HD|SD|4K|H\.?265|HEVC)(?:\s+(?:FHD|UHD|HD|SD|4K|H\.?265|HEVC))*\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        // First add base names and raw operator names
        foreach (var seed in seeds)
            Add(seed);
        foreach (var raw in channel.RawNames)
            Add(raw);

        // Expand each seed with every resolution/codec suffix (SD, HD, FHD, 4K, H265...)
        foreach (var seed in seeds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
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

