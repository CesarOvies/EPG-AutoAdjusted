using System.Text.Json;

namespace EpgGenerator;

public sealed record ChannelData(
    string SourceId,
    string Name,
    string? LogoUrl,
    string? Category,
    List<ProgrammeData> Programmes
);

public sealed record ProgrammeData(
    string Title,
    string? SubTitle,
    string? Description,
    DateTimeOffset Start,
    DateTimeOffset Stop,
    string? Category,
    string? IconUrl,
    int? Season,
    int? Episode
);

public sealed class MergedChannel
{
    public required string Id { get; init; }
    public required string BaseName { get; init; }
    public string? LogoUrl { get; set; }
    public HashSet<string> RawNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ProgrammeData> Programmes { get; set; } = [];
    public string PrimarySource { get; set; } = "";
}

public sealed class AliasConfig
{
    public List<string> Suffixes { get; set; } =
    [
        "SD", "HD", "FHD", "4K", "UHD", "QHD", "H265", "HEVC",
        "SD¹", "SD²", "HD¹", "HD²", "FHD¹", "FHD²", "UHD¹", "UHD²",
        "FHD H265", "HD H265", "FHD HEVC", "4K UHD"
    ];

    public Dictionary<string, List<string>> CustomAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static AliasConfig Load(string path)
    {
        if (!File.Exists(path))
            return new AliasConfig();

        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<AliasConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AliasConfig();

        cfg.CustomAliases = new Dictionary<string, List<string>>(cfg.CustomAliases, StringComparer.OrdinalIgnoreCase);
        return cfg;
    }
}

