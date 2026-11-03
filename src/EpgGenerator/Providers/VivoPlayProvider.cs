using System.Text.Json;

namespace EpgGenerator.Providers;

public sealed class VivoPlayProvider(HttpClient http) : IEpgProvider
{
    public string Name => "VivoPlay";

    private const string CatalogUrl =
        "https://contentapi-br.cdn.telefonica.com/25/default/pt-BR/contents/all?ca_deviceTypes=401&contentTypes=LCH&ca_active=true&ca_requiresPin=false&fields=Pid,Name,ChannelNumber,CallLetter,images.logo&orderBy=contentOrder&offset=0&limit=1000";

    private const int BatchSize = 20;

    public async Task<List<ChannelData>> FetchAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct = default)
    {
        using var catalogDoc = await GetJsonAsync(CatalogUrl, ct);
        if (catalogDoc is null)
            return [];

        if (!catalogDoc.RootElement.TryGetProperty("Content", out var contentEl) ||
            !contentEl.TryGetProperty("List", out var listEl) ||
            listEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var channels = new Dictionary<string, ChannelData>(StringComparer.OrdinalIgnoreCase);
        foreach (var ch in listEl.EnumerateArray())
        {
            var pid = ch.TryGetProperty("Pid", out var pEl) ? pEl.GetString() : null;
            var name = ch.TryGetProperty("Name", out var nEl) ? nEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(pid) || string.IsNullOrWhiteSpace(name))
                continue;

            string? logo = null;
            if (ch.TryGetProperty("Images", out var imgEl) &&
                imgEl.TryGetProperty("Logo", out var logoArr) &&
                logoArr.ValueKind == JsonValueKind.Array &&
                logoArr.GetArrayLength() > 0 &&
                logoArr[0].TryGetProperty("Url", out var urlEl))
            {
                logo = urlEl.GetString();
            }

            channels[pid] = new ChannelData(pid, name.Trim(), logo, null, []);
        }

        var allPids = channels.Keys.ToList();

        // Query in 24-hour slices so limit=1000 covers the whole batch of 20 channels
        for (var windowStart = startUtc; windowStart < endUtc; windowStart = windowStart.AddDays(1))
        {
            var windowEnd = windowStart.AddDays(1);
            if (windowEnd > endUtc)
                windowEnd = endUtc;

            long stUnix = windowStart.ToUnixTimeSeconds();
            long etUnix = windowEnd.ToUnixTimeSeconds();

            for (int i = 0; i < allPids.Count; i += BatchSize)
            {
                var batchPids = string.Join(",", allPids.Skip(i).Take(BatchSize));
                var scheduleUrl =
                    $"https://contentapi-br.cdn.telefonica.com/25/default/pt-BR/schedules?ca_deviceTypes=null%7C401&ca_channelmaps=779%7Cnull&fields=Pid,Title,Description,ChannelName,Start,End,LiveChannelPid,SeasonNumber,EpisodeNumber,images.videoFrame&orderBy=START_TIME%3Aa&filteravailability=false&starttime={stUnix}&endtime={etUnix}&livechannelpids={batchPids}&offset=0&limit=1000";

                using var schedDoc = await GetJsonAsync(scheduleUrl, ct);
                if (schedDoc is null)
                    continue;

                if (!schedDoc.RootElement.TryGetProperty("Content", out var schedArr) ||
                    schedArr.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in schedArr.EnumerateArray())
                {
                    var pid = item.TryGetProperty("LiveChannelPid", out var lpEl) ? lpEl.GetString() : null;
                    var title = item.TryGetProperty("Title", out var tEl) ? tEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(pid) || string.IsNullOrWhiteSpace(title))
                        continue;

                    if (!channels.TryGetValue(pid, out var chData))
                        continue;

                    long st = item.TryGetProperty("Start", out var sEl) && sEl.TryGetInt64(out var v1) ? v1 : 0;
                    long et = item.TryGetProperty("End", out var eEl) && eEl.TryGetInt64(out var v2) ? v2 : 0;
                    if (st <= 0 || et <= st)
                        continue;

                    var desc = item.TryGetProperty("Description", out var dEl) ? dEl.GetString() : null;
                    int? season = item.TryGetProperty("SeasonNumber", out var snEl) && snEl.TryGetInt32(out var sVal) && sVal > 0 ? sVal : null;
                    int? episode = item.TryGetProperty("EpisodeNumber", out var enEl) && enEl.TryGetInt32(out var eVal) && eVal > 0 ? eVal : null;

                    string? icon = null;
                    if (item.TryGetProperty("Images", out var imgEl) &&
                        imgEl.TryGetProperty("VideoFrame", out var vfArr) &&
                        vfArr.ValueKind == JsonValueKind.Array &&
                        vfArr.GetArrayLength() > 0 &&
                        vfArr[0].TryGetProperty("Url", out var uEl))
                    {
                        icon = uEl.GetString();
                    }

                    chData.Programmes.Add(new ProgrammeData(
                        Title: title.Trim(),
                        SubTitle: null,
                        Description: string.IsNullOrWhiteSpace(desc) ? null : desc.Trim(),
                        Start: DateTimeOffset.FromUnixTimeSeconds(st),
                        Stop: DateTimeOffset.FromUnixTimeSeconds(et),
                        Category: null,
                        IconUrl: icon,
                        Season: season,
                        Episode: episode
                    ));
                }
            }
        }

        return channels.Values.ToList();
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

