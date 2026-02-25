using McLauncher.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace McLauncher.Services
{
    public class MinecraftService
    {
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private readonly string _baseDir;

        private List<VersionRef> _manifest = new List<VersionRef>();

        private readonly FabricService _fabric;
        private List<string> _fabricGameVersions = new List<string>();
        private readonly Dictionary<string, List<string>> _fabricLoadersCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // выбранные значения (из UI)
        public string SelectedFabricGameVersion { get; set; } = null;
        public string SelectedFabricLoaderVersion { get; set; } = null;

        // ДОБАВЛЕНО: быстрый индекс установленных Fabric по gameVersion
        private HashSet<string> _installedFabricGameVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public MinecraftService(string baseDir)
        {
            _baseDir = baseDir;
            _fabric = new FabricService(_http);
        }

        public async Task LoadManifestAsync()
        {
            try
            {
                var json = await _http.GetStringAsync("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json");
                var data = JsonSerializer.Deserialize<VersionManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                _manifest = data?.versions ?? new List<VersionRef>();
            }
            catch { }

            try
            {
                _fabricGameVersions = await _fabric.GetGameVersionsAsync();
                SelectedFabricGameVersion ??= _fabricGameVersions.FirstOrDefault();

                if (!string.IsNullOrEmpty(SelectedFabricGameVersion))
                {
                    var loaders = await GetFabricLoaderVersionsAsync(SelectedFabricGameVersion);
                    SelectedFabricLoaderVersion ??= loaders.FirstOrDefault();
                }
            }
            catch { }

            // индекс установленных Fabric (один раз)
            RebuildInstalledFabricIndex();
        }

        public void RebuildInstalledFabricIndex()
        {
            _installedFabricGameVersions.Clear();
            try
            {
                string versionsDir = Path.Combine(_baseDir, "versions");
                if (!Directory.Exists(versionsDir)) return;

                foreach (var dir in Directory.EnumerateDirectories(versionsDir, "fabric-loader-*"))
                {
                    // fabric-loader-0.16.5-1.21.1
                    var name = Path.GetFileName(dir);
                    var parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        var game = parts[^1];
                        _installedFabricGameVersions.Add(game);
                    }
                }
            }
            catch { }
        }

        public List<string> GetFabricGameVersions() => _fabricGameVersions?.ToList() ?? new List<string>();

        public async Task<List<string>> GetFabricLoaderVersionsAsync(string gameVersion, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(gameVersion)) return new List<string>();

            if (_fabricLoadersCache.TryGetValue(gameVersion, out var cached) && cached?.Count > 0)
                return cached.ToList();

            try
            {
                var loaders = await _fabric.GetLoaderVersionsAsync(gameVersion, ct);
                _fabricLoadersCache[gameVersion] = loaders;
                return loaders.ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        public List<VersionRef> GetFilteredVersions(bool snapshots, bool pre, bool fabric)
        {
            var vanilla = _manifest.Where(v =>
            {
                if (v.type == "release") return true;
                if (snapshots && v.type == "snapshot") return true;
                if (pre && v.type == "pre-release") return true;
                return false;
            })
            .Select(v =>
            {
                v.IsInstalled = File.Exists(Path.Combine(_baseDir, "versions", v.id, v.id + ".json"));
                return v;
            })
            .ToList();

            if (!fabric)
                return vanilla;

            // Fabric версии сверху: fabric:1.20.1
            var fabricList = new List<VersionRef>();
            foreach (var gv in _fabricGameVersions ?? new List<string>())
            {
                var fv = new FabricVersionRef
                {
                    id = $"fabric:{gv}",
                    type = "fabric",
                    GameVersion = gv,
                    // LoaderVersion и LaunchId НЕ фиксируем тут (иначе лаги и неправильные OK)
                    LoaderVersion = null,
                    LaunchId = null,
                    url = null,
                    // OK: если вообще есть установленный fabric-loader-*-<gv>
                    IsInstalled = _installedFabricGameVersions.Contains(gv)
                };
                fabricList.Add(fv);
            }

            return fabricList.Concat(vanilla).ToList();
        }

        public bool CheckRules(JsonElement el)
        {
            if (!el.TryGetProperty("rules", out var rules)) return true;

            bool allow = false;
            string osName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "linux";
            string arch = RuntimeInformation.OSArchitecture == Architecture.X64 ? "x64" : "x86";

            foreach (var rule in rules.EnumerateArray())
            {
                bool matches = true;

                if (rule.TryGetProperty("os", out var os))
                {
                    if (os.TryGetProperty("name", out var name) && name.GetString() != osName) matches = false;
                    if (os.TryGetProperty("arch", out var a) && a.GetString() != arch) matches = false;
                }

                if (rule.TryGetProperty("features", out _)) matches = false;

                string action = rule.GetProperty("action").GetString();
                if (matches) allow = (action == "allow");
            }
            return allow;
        }

        public async Task InstallVersionAsync(VersionRef sel, Action<string> log, Action<double> progress, CancellationToken ct)
        {
            if (sel is FabricVersionRef fsel)
            {
                await InstallFabricAsync(fsel, log, progress, ct);
                // обновим индекс OK после установки
                RebuildInstalledFabricIndex();
                return;
            }

            string vDir = Path.Combine(_baseDir, "versions", sel.id);
            Directory.CreateDirectory(vDir);

            log?.Invoke("Загрузка JSON версии...");
            string vJson = await _http.GetStringAsync(sel.url, ct);
            await File.WriteAllTextAsync(Path.Combine(vDir, $"{sel.id}.json"), vJson);

            using var doc = JsonDocument.Parse(vJson);
            var root = doc.RootElement;
            var jobs = new ConcurrentQueue<(string url, string path)>();

            if (root.GetProperty("downloads").TryGetProperty("client", out var client))
                jobs.Enqueue((client.GetProperty("url").GetString(), Path.Combine(vDir, $"{sel.id}.jar")));

            if (root.TryGetProperty("libraries", out var libs))
            {
                foreach (var lib in libs.EnumerateArray())
                {
                    if (!CheckRules(lib)) continue;
                    if (lib.TryGetProperty("downloads", out var dls))
                    {
                        if (dls.TryGetProperty("artifact", out var art))
                            jobs.Enqueue((art.GetProperty("url").GetString(), Path.Combine(_baseDir, "libraries", art.GetProperty("path").GetString().Replace('/', Path.DirectorySeparatorChar))));

                        string osKey = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "natives-windows" : "natives-linux";
                        if (dls.TryGetProperty("classifiers", out var cls) && cls.TryGetProperty(osKey, out var nArt))
                            jobs.Enqueue((nArt.GetProperty("url").GetString(), Path.Combine(_baseDir, "libraries", nArt.GetProperty("path").GetString().Replace('/', Path.DirectorySeparatorChar))));
                    }
                }
            }

            if (root.TryGetProperty("assetIndex", out var ai))
            {
                log?.Invoke("Проверка ассетов...");
                string indexJson = await _http.GetStringAsync(ai.GetProperty("url").GetString(), ct);
                string indexPath = Path.Combine(_baseDir, "assets", "indexes", $"{ai.GetProperty("id").GetString()}.json");
                Directory.CreateDirectory(Path.GetDirectoryName(indexPath));
                await File.WriteAllTextAsync(indexPath, indexJson);

                using var indexDoc = JsonDocument.Parse(indexJson);
                var objects = indexDoc.RootElement.GetProperty("objects");
                foreach (var prop in objects.EnumerateObject())
                {
                    string hash = prop.Value.GetProperty("hash").GetString();
                    string path = Path.Combine(_baseDir, "assets", "objects", hash.Substring(0, 2), hash);
                    if (!File.Exists(path))
                        jobs.Enqueue(($"https://resources.download.minecraft.net/{hash.Substring(0, 2)}/{hash}", path));
                }
            }

            int total = Math.Max(1, jobs.Count), done = 0;
            var tasks = Enumerable.Range(0, 8).Select(async _ =>
            {
                while (jobs.TryDequeue(out var job))
                {
                    if (ct.IsCancellationRequested) return;
                    try
                    {
                        if (!File.Exists(job.path))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(job.path));
                            var data = await _http.GetByteArrayAsync(job.url, ct);
                            await File.WriteAllBytesAsync(job.path, data, ct);
                        }
                    }
                    catch { }
                    progress?.Invoke((double)Interlocked.Increment(ref done) / total * 100);
                }
            });
            await Task.WhenAll(tasks);
        }

        private async Task InstallFabricAsync(FabricVersionRef fsel, Action<string> log, Action<double> progress, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(fsel.GameVersion))
                throw new InvalidOperationException("Fabric GameVersion пустой.");

            // loader должен прийти из UI (SelectedFabricLoaderVersion)
            if (string.IsNullOrWhiteSpace(fsel.LoaderVersion))
                fsel.LoaderVersion = SelectedFabricLoaderVersion;

            if (string.IsNullOrWhiteSpace(fsel.LoaderVersion))
                throw new InvalidOperationException("Не выбран Fabric Loader.");

            fsel.LaunchId = _fabric.BuildLaunchId(fsel.GameVersion, fsel.LoaderVersion);
            fsel.url = _fabric.GetProfileJsonUrl(fsel.GameVersion, fsel.LoaderVersion);

            // 1) Установим ваниллу для inheritsFrom
            var vanilla = _manifest.FirstOrDefault(v => string.Equals(v.id, fsel.GameVersion, StringComparison.OrdinalIgnoreCase));
            if (vanilla == null)
                throw new InvalidOperationException($"Vanilla версия {fsel.GameVersion} не найдена в Mojang manifest.");

            var vanillaJsonPath = Path.Combine(_baseDir, "versions", vanilla.id, vanilla.id + ".json");
            if (!File.Exists(vanillaJsonPath))
            {
                log?.Invoke($"Установка ваниллы {vanilla.id} (для Fabric)...");
                await InstallVersionAsync(vanilla, log, progress, ct);
            }

            // 2) Скачиваем Fabric profile json
            log?.Invoke($"Загрузка Fabric профиля ({fsel.LoaderVersion})...");
            string profileJson = await _http.GetStringAsync(fsel.url, ct);

            string fDir = Path.Combine(_baseDir, "versions", fsel.LaunchId);
            Directory.CreateDirectory(fDir);
            await File.WriteAllTextAsync(Path.Combine(fDir, $"{fsel.LaunchId}.json"), profileJson, ct);

            // 3) Скачиваем Fabric библиотеки (name/url формат)
            using var doc = JsonDocument.Parse(profileJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("libraries", out var libs))
            {
                log?.Invoke("В Fabric профиле нет libraries.");
                return;
            }

            var jobs = new ConcurrentQueue<(string url, string path)>();
            foreach (var lib in libs.EnumerateArray())
            {
                if (!lib.TryGetProperty("name", out var nameEl)) continue;
                string name = nameEl.GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;

                string baseUrl = lib.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "https://maven.fabricmc.net/";

                var parts = name.Split(':');
                if (parts.Length < 3) continue;

                string relPath = $"{parts[0].Replace('.', '/')}/{parts[1]}/{parts[2]}/{parts[1]}-{parts[2]}.jar";
                string fullUrl = baseUrl.TrimEnd('/') + "/" + relPath;

                string localPath = Path.Combine(_baseDir, "libraries", relPath.Replace('/', Path.DirectorySeparatorChar));
                jobs.Enqueue((fullUrl, localPath));
            }

            int total = Math.Max(1, jobs.Count);
            int done = 0;

            var tasks = Enumerable.Range(0, 8).Select(async _ =>
            {
                while (jobs.TryDequeue(out var job))
                {
                    if (ct.IsCancellationRequested) return;
                    try
                    {
                        if (!File.Exists(job.path))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(job.path));
                            var data = await _http.GetByteArrayAsync(job.url, ct);
                            await File.WriteAllBytesAsync(job.path, data, ct);
                        }
                    }
                    catch { }
                    progress?.Invoke((double)Interlocked.Increment(ref done) / total * 100);
                }
            });

            await Task.WhenAll(tasks);
            log?.Invoke("Fabric установка завершена.");
        }
    }
}