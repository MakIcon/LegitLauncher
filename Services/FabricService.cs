using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace McLauncher.Services
{
    public class FabricService
    {
        private readonly HttpClient _http;

        public FabricService(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        // Список версий игры, которые поддерживает Fabric
        public async Task<List<string>> GetGameVersionsAsync(CancellationToken ct = default)
        {
            // https://meta.fabricmc.net/v2/versions/game
            var json = await _http.GetStringAsync("https://meta.fabricmc.net/v2/versions/game", ct);
            using var doc = JsonDocument.Parse(json);

            // Берем "stable" + нормальные номера (без "w" снапшотов)
            var list = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("version", out var v)) continue;
                var ver = v.GetString();
                if (string.IsNullOrWhiteSpace(ver)) continue;

                // Fabric meta отдает и снапшоты; нам в UI обычно нужны релизы.
                // Если хочешь показывать снапшоты Fabric — убери эту проверку.
                if (ver.Contains("w", StringComparison.OrdinalIgnoreCase)) continue;

                list.Add(ver);
            }

            // сортировка по числовым сегментам (примерно), новые сверху
            return list
                .Distinct()
                .OrderByDescending(SemverishKey)
                .ToList();
        }

        // Список loader-версий для конкретной версии игры
        public async Task<List<string>> GetLoaderVersionsAsync(string gameVersion, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(gameVersion)) return new List<string>();

            // https://meta.fabricmc.net/v2/versions/loader/{gameVersion}
            var json = await _http.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}", ct);
            using var doc = JsonDocument.Parse(json);

            var res = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.TryGetProperty("loader", out var loader) &&
                    loader.TryGetProperty("version", out var lv))
                {
                    var s = lv.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) res.Add(s);
                }
            }

            // meta обычно уже дает новые сверху, но на всякий:
            return res.Distinct().ToList();
        }

        // URL профиля Fabric (JSON) для gameVersion + loaderVersion
        public string GetProfileJsonUrl(string gameVersion, string loaderVersion)
        {
            // https://meta.fabricmc.net/v2/versions/loader/{game}/{loader}/profile/json
            return $"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}/{loaderVersion}/profile/json";
        }

        // Fabric "LaunchId" как ты просил: fabric-loader-0.16.5-1.21.1
        public string BuildLaunchId(string gameVersion, string loaderVersion)
            => $"fabric-loader-{loaderVersion}-{gameVersion}";

        private static object SemverishKey(string v)
        {
            // Примерная сортировка: 1.21.1 > 1.20.6 > 1.19.4
            // Не идеальный semver, но достаточно для списка.
            try
            {
                var parts = v.Split('.', StringSplitOptions.RemoveEmptyEntries);
                int a = parts.Length > 0 ? int.Parse(parts[0]) : 0;
                int b = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                int c = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                return (a, b, c);
            }
            catch
            {
                return (0, 0, 0);
            }
        }
    }
}