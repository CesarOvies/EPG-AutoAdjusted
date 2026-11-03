using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EpgGenerator;

public sealed record WorkflowStats(
    int TotalRuns,
    int SuccessfulRuns,
    int FailedRuns,
    IReadOnlyList<bool> RecentOutcomes // true = success, false = failure (oldest to newest)
);

public static class StatsSvgWriter
{
    private const string DefaultRepo = "CesarOvies/EPG-AutoAdjusted";

    public static async Task<WorkflowStats> FetchWorkflowStatsAsync(HttpClient http, CancellationToken ct = default)
    {
        try
        {
            var repo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
            if (string.IsNullOrWhiteSpace(repo))
                repo = DefaultRepo;

            var runsApiUrl = $"https://api.github.com/repos/{repo}/actions/workflows/update-epg.yml/runs?per_page=100";
            using var req = new HttpRequestMessage(HttpMethod.Get, runsApiUrl);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return new WorkflowStats(1, 1, 0, [true]);

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("workflow_runs", out var runsEl) ||
                runsEl.ValueKind != JsonValueKind.Array)
            {
                return new WorkflowStats(1, 1, 0, [true]);
            }

            int success = 0;
            int failed = 0;
            var recent = new List<bool>();

            foreach (var run in runsEl.EnumerateArray())
            {
                var status = run.TryGetProperty("status", out var sEl) ? sEl.GetString() : null;
                if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                    continue;

                var conclusion = run.TryGetProperty("conclusion", out var cEl) ? cEl.GetString() : null;
                if (string.Equals(conclusion, "success", StringComparison.OrdinalIgnoreCase))
                {
                    success++;
                    recent.Add(true);
                }
                else if (string.Equals(conclusion, "failure", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(conclusion, "timed_out", StringComparison.OrdinalIgnoreCase))
                {
                    failed++;
                    recent.Add(false);
                }
            }

            // If running inside GitHub Actions right now, count current successful run
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
            {
                success++;
                recent.Insert(0, true);
            }

            recent.Reverse(); // oldest to newest
            if (recent.Count > 30)
                recent = recent.Skip(recent.Count - 30).ToList();

            int total = success + failed;
            return total > 0
                ? new WorkflowStats(total, success, failed, recent)
                : new WorkflowStats(1, 1, 0, [true]);
        }
        catch
        {
            return new WorkflowStats(1, 1, 0, [true]);
        }
    }

    public static void WriteSvg(
        string svgPath,
        WorkflowStats stats,
        int activeChannels,
        int totalProgrammes,
        DateTimeOffset updatedAtUtc)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(svgPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        int total = Math.Max(1, stats.TotalRuns);
        double pct = Math.Clamp(stats.SuccessfulRuns * 100.0 / total, 0.0, 100.0);
        string pctText = pct.ToString("0.#", CultureInfo.InvariantCulture) + "%";

        // Donut chart math (r = 44, circumference = 2 * pi * 44 = 276.46)
        const double circumference = 276.46;
        double dash = circumference * (pct / 100.0);
        string dashStr = dash.ToString("0.00", CultureInfo.InvariantCulture);
        string gapStr = (circumference - dash).ToString("0.00", CultureInfo.InvariantCulture);
        string ringColor = pct >= 95 ? "#10b981" : pct >= 80 ? "#f59e0b" : "#ef4444";

        var brt = updatedAtUtc.ToOffset(TimeSpan.FromHours(-3));
        string updatedText = $"{brt:dd/MM/yyyy HH:mm} BRT";

        // Build uptime history pills (up to 30 recent runs)
        var barsSb = new StringBuilder();
        var outcomes = stats.RecentOutcomes.Count > 0 ? stats.RecentOutcomes : [true];
        int barX = 230;
        foreach (var ok in outcomes.TakeLast(30))
        {
            string color = ok ? "#10b981" : "#ef4444";
            barsSb.AppendLine($"  <rect x=\"{barX}\" y=\"158\" width=\"12\" height=\"18\" rx=\"3\" fill=\"{color}\" />");
            barX += 16;
        }

        string svg = $$"""
        <svg xmlns="http://www.w3.org/2000/svg" width="760" height="205" viewBox="0 0 760 205" role="img" aria-label="EPG Workflow Status">
          <defs>
            <linearGradient id="bg" x1="0%" y1="0%" x2="100%" y2="100%">
              <stop offset="0%" stop-color="#0f172a"/>
              <stop offset="100%" stop-color="#1e293b"/>
            </linearGradient>
          </defs>
          <rect width="760" height="205" rx="14" fill="url(#bg)" stroke="#334155" stroke-width="1.5"/>

          <!-- Donut Chart: Taxa de Sucesso -->
          <g transform="translate(110, 102)">
            <circle r="44" fill="none" stroke="#334155" stroke-width="10"/>
            <circle r="44" fill="none" stroke="{{ringColor}}" stroke-width="10"
                    stroke-dasharray="{{dashStr}} {{gapStr}}"
                    stroke-linecap="round" transform="rotate(-90)"/>
            <text x="0" y="4" text-anchor="middle" fill="#f8fafc" font-family="Segoe UI, Ubuntu, sans-serif" font-size="20" font-weight="700">{{pctText}}</text>
            <text x="0" y="22" text-anchor="middle" fill="#94a3b8" font-family="Segoe UI, Ubuntu, sans-serif" font-size="10" font-weight="600">SUCESSO</text>
          </g>

          <!-- Header -->
          <text x="230" y="36" fill="#f8fafc" font-family="Segoe UI, Ubuntu, sans-serif" font-size="17" font-weight="700">EPG-AutoAdjusted — Painel de Execuções (GitHub Actions)</text>
          <text x="230" y="55" fill="#94a3b8" font-family="Segoe UI, Ubuntu, sans-serif" font-size="12">Última atualização: {{updatedText}}</text>

          <!-- Metric Cards -->
          <rect x="230" y="70" width="155" height="62" rx="8" fill="#1e293b" stroke="#334155"/>
          <text x="244" y="92" fill="#94a3b8" font-family="Segoe UI, Ubuntu, sans-serif" font-size="11" font-weight="600">WORKFLOWS RODADOS</text>
          <text x="244" y="118" fill="#10b981" font-family="Segoe UI, Ubuntu, sans-serif" font-size="22" font-weight="700">{{stats.SuccessfulRuns}} <tspan fill="#64748b" font-size="14" font-weight="400">/ {{stats.TotalRuns}} ok</tspan></text>

          <rect x="398" y="70" width="155" height="62" rx="8" fill="#1e293b" stroke="#334155"/>
          <text x="412" y="92" fill="#94a3b8" font-family="Segoe UI, Ubuntu, sans-serif" font-size="11" font-weight="600">CANAIS ATIVOS</text>
          <text x="412" y="118" fill="#38bdf8" font-family="Segoe UI, Ubuntu, sans-serif" font-size="22" font-weight="700">{{activeChannels}}</text>

          <rect x="566" y="70" width="164" height="62" rx="8" fill="#1e293b" stroke="#334155"/>
          <text x="580" y="92" fill="#94a3b8" font-family="Segoe UI, Ubuntu, sans-serif" font-size="11" font-weight="600">PROGRAMAS NA GRADE</text>
          <text x="580" y="118" fill="#a855f7" font-family="Segoe UI, Ubuntu, sans-serif" font-size="22" font-weight="700">{{totalProgrammes.ToString("N0", new CultureInfo("pt-BR"))}}</text>

          <!-- Recent Runs Uptime Bar -->
          <text x="230" y="150" fill="#94a3b8" font-family="Segoe UI, Ubuntu, sans-serif" font-size="11" font-weight="600">HISTÓRICO DAS ÚLTIMAS EXECUÇÕES</text>
        {{barsSb}}
        </svg>
        """;

        var tmpPath = svgPath + ".tmp";
        File.WriteAllText(tmpPath, svg, new UTF8Encoding(false));
        File.Move(tmpPath, svgPath, overwrite: true);
    }
}
