using McLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace McLauncher.Services
{
    public class ModrinthService
    {
        private readonly HttpClient _http;

        public ModrinthService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "LegitLauncher/1.0 (contact@example.com)");
        }

        private static string BuildFacets(string gameVersion, string loader)
        {
            // versions + categories(loader) + project_type:mod
            return $"[[\"versions:{gameVersion}\"],[\"categories:{loader}\"],[\"project_type:mod\"]]";
        }

        public async Task<List<ModItem>> GetPopularModsAsync(string gameVersion, string loader, int limit = 24)
        {
            try
            {
                string facets = BuildFacets(gameVersion, loader);
                string url =
                    $"https://api.modrinth.com/v2/search?query=&index=downloads&facets={Uri.EscapeDataString(facets)}&limit={limit}";

                var result = await _http.GetFromJsonAsync<ModrinthSearchResult>(url);
                return result?.hits ?? new List<ModItem>();
            }
            catch
            {
                return new List<ModItem>();
            }
        }

        public async Task<List<ModItem>> SearchModsAsync(string query, string gameVersion, string loader, int limit = 24)
        {
            try
            {
                string facets = BuildFacets(gameVersion, loader);
                string url =
                    $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&index=downloads&facets={Uri.EscapeDataString(facets)}&limit={limit}";

                var result = await _http.GetFromJsonAsync<ModrinthSearchResult>(url);
                return result?.hits ?? new List<ModItem>();
            }
            catch
            {
                return new List<ModItem>();
            }
        }

        public Task<(bool ok, string message, string filename)> DownloadLatestAsync(
            ModItem mod,
            string gameVersion,
            string loader,
            string modsFolder)
        {
            // Оставил совместимость с твоим методом: просто вызывает прогресс-версию без прогресса.
            return DownloadLatestWithProgressAsync(mod, gameVersion, loader, modsFolder, progress: null, CancellationToken.None);
        }

        public async Task<(bool ok, string message, string filename)> DownloadLatestWithProgressAsync(
            ModItem mod,
            string gameVersion,
            string loader,
            string modsFolder,
            IProgress<double> progress,
            CancellationToken ct = default)
        {
            try
            {
                if (mod == null) return (false, "mod == null", null);

                string url =
                    $"https://api.modrinth.com/v2/project/{mod.project_id}/version?game_versions=[\"{gameVersion}\"]&loaders=[\"{loader}\"]";

                var versions = await _http.GetFromJsonAsync<List<ModVersion>>(url, ct);

                var latest = versions?.OrderByDescending(v => v.date_published).FirstOrDefault();
                if (latest == null) return (false, "Версия не найдена", null);

                var file = latest.files?.FirstOrDefault(f => f.primary) ?? latest.files?.FirstOrDefault();
                if (file == null) return (false, "Файл не найден", null);

                Directory.CreateDirectory(modsFolder);
                string filePath = Path.Combine(modsFolder, file.filename);

                using var req = new HttpRequestMessage(HttpMethod.Get, file.url);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();

                long? contentLength = resp.Content.Headers.ContentLength;

                await using var input = await resp.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(
                    filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

                var buffer = new byte[81920];
                long totalRead = 0;

                progress?.Report(0);

                while (true)
                {
                    int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read <= 0) break;

                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    totalRead += read;

                    if (contentLength is > 0)
                    {
                        double pct = (double)totalRead / contentLength.Value * 100.0;
                        progress?.Report(pct);
                    }
                }

                progress?.Report(100);
                return (true, "Готово", file.filename);
            }
            catch (OperationCanceledException)
            {
                return (false, "Загрузка отменена", null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null);
            }
        }
    }
}