using System.Text.Json;

namespace EpgGenerator.Providers;

public sealed class ClaroTvProvider(HttpClient http) : IEpgProvider
{
    public string Name => "ClaroTV+";

    private const string BaseUrl = "https://www.clarotvmais.com.br/avsclient/1.2/epg/livechannels";
    private const string DefaultLocation = "SAO%20PAULO,AMAZONAS";
    private const int BatchSize = 40;

    public async Task<List<ChannelData>> FetchAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct = default)
    {
        var catalogUrl = $"{BaseUrl}?types=&channelIds=&startTime=&endTime=&location={DefaultLocation}&channel=PCTV";
        using var catalogDoc = await GetJsonAsync(catalogUrl, ct);
        if (catalogDoc is null)
            return [];

        if (!catalogDoc.RootElement.TryGetProperty("response", out var respEl) ||
            !respEl.TryGetProperty("liveChannels", out var channelsEl) ||
            channelsEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var channelMeta = new Dictionary<string, (string Name, string? Logo, string? Category)>();
        foreach (var ch in channelsEl.EnumerateArray())
        {
            var id = GetIdString(ch);
            var name = ch.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                continue;

            var logo = ch.TryGetProperty("logo", out var l) ? l.GetString() : null;
            var type = ch.TryGetProperty("type", out var t) ? t.GetString() : null;
            channelMeta[id] = (name.Trim(), logo, type);
        }

        var allIds = channelMeta.Keys.ToList();
        var results = new Dictionary<string, ChannelData>();
        long startUnix = startUtc.ToUnixTimeSeconds();
        long endUnix = endUtc.ToUnixTimeSeconds();

        for (int i = 0; i < allIds.Count; i += BatchSize)
        {
            var batchIds = string.Join(",", allIds.Skip(i).Take(BatchSize));
            var scheduleUrl = $"{BaseUrl}?types=&channelIds={batchIds}&startTime={startUnix}&endTime={endUnix}&location={DefaultLocation}&channel=PCTV";

            using var scheduleDoc = await GetJsonAsync(scheduleUrl, ct);
            if (scheduleDoc is null)
                continue;

            if (!scheduleDoc.RootElement.TryGetProperty("response", out var sResp) ||
                !sResp.TryGetProperty("liveChannels", out var sChannels) ||
                sChannels.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var ch in sChannels.EnumerateArray())
            {
                var id = GetIdString(ch);
                if (string.IsNullOrEmpty(id) || !channelMeta.TryGetValue(id, out var meta))
                    continue;

                var programmes = new List<ProgrammeData>();
                if (ch.TryGetProperty("schedules", out var schedArray) && schedArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in schedArray.EnumerateArray())
                    {
                        var title = item.TryGetProperty("title", out var tEl) ? tEl.GetString() : null;
                        if (string.IsNullOrWhiteSpace(title))
                            continue;

                        long st = item.TryGetProperty("startTime", out var stEl) && stEl.TryGetInt64(out var v1) ? v1 : 0;
                        long et = item.TryGetProperty("endTime", out var etEl) && etEl.TryGetInt64(out var v2) ? v2 : 0;
                        if (st <= 0 || et <= st)
                            continue;

                        var desc = item.TryGetProperty("description", out var dEl) ? dEl.GetString() : null;
                        var epName = item.TryGetProperty("episodeName", out var epEl) ? epEl.GetString() : null;
                        var img = item.TryGetProperty("image", out var imEl) ? imEl.GetString() : null;
                        if (!string.IsNullOrEmpty(img))
                            img = img.Replace("{{image-size-placeholder}}", "420_236");

                        int? season = item.TryGetProperty("seasonNumber", out var snEl) && snEl.TryGetInt32(out var sVal) && sVal > 0 ? sVal : null;
                        int? episode = item.TryGetProperty("episodeNumber", out var enEl) && enEl.TryGetInt32(out var eVal) && eVal > 0 ? eVal : null;

                        programmes.Add(new ProgrammeData(
                            Title: title.Trim(),
                            SubTitle: string.IsNullOrWhiteSpace(epName) ? null : epName.Trim(),
                            Description: string.IsNullOrWhiteSpace(desc) ? null : desc.Trim(),
                            Start: DateTimeOffset.FromUnixTimeSeconds(st),
                            Stop: DateTimeOffset.FromUnixTimeSeconds(et),
                            Category: meta.Category,
                            IconUrl: img,
                            Season: season,
                            Episode: episode
                        ));
                    }
                }

                // Claro returns a single placeholder schedule when guide is unavailable
                if (programmes.Count <= 1)
                    programmes.Clear();

                results[id] = new ChannelData(id, meta.Name, meta.Logo, meta.Category, programmes);
            }
        }

        return results.Values.ToList();
    }

    private static string? GetIdString(JsonElement el)
    {
        if (!el.TryGetProperty("id", out var idEl))
            return null;
        return idEl.ValueKind switch
        {
            JsonValueKind.Number => idEl.GetInt64().ToString(),
            JsonValueKind.String => idEl.GetString(),
            _ => null
        };
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
                return null;
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch
        {
            return null;
        }
    }
}

