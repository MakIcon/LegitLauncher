using McLauncher.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace McLauncher.Services
{
    public class LaunchService
    {
        private readonly string _baseDir;
        private readonly MinecraftService _mcService;

        // ✅ КАК В HelloMineLauncher.cs: генерим один раз на сессию лаунчера
        private static readonly string AccessToken = Guid.NewGuid().ToString("N"); // :contentReference[oaicite:2]{index=2}
        private static readonly string Uuid = Guid.NewGuid().ToString();          // :contentReference[oaicite:3]{index=3}

        public LaunchService(string baseDir, MinecraftService mcService)
        {
            _baseDir = baseDir;
            _mcService = mcService;
        }

        public async Task<Process> LaunchAsync(string versionId, string nickname, string javaPath, int ramGb, Action<string> log)
        {
            string vDir = Path.Combine(_baseDir, "versions", versionId);
            string instanceDir = Path.Combine(_baseDir, "instances", versionId);
            Directory.CreateDirectory(instanceDir);

            string jsonPath = Path.Combine(vDir, $"{versionId}.json");
            var jsonContent = await File.ReadAllTextAsync(jsonPath);
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // inheritsFrom (Fabric)
            string baseId = null;
            if (root.TryGetProperty("inheritsFrom", out var inh))
                baseId = inh.GetString();

            JsonDocument baseDoc = null;
            JsonElement baseRoot = default;
            if (!string.IsNullOrWhiteSpace(baseId))
            {
                var baseJsonPath = Path.Combine(_baseDir, "versions", baseId, $"{baseId}.json");
                if (File.Exists(baseJsonPath))
                {
                    var baseJson = await File.ReadAllTextAsync(baseJsonPath);
                    baseDoc = JsonDocument.Parse(baseJson);
                    baseRoot = baseDoc.RootElement;
                }
            }

            bool is32Bit = javaPath.Contains("x86", StringComparison.OrdinalIgnoreCase);
            int finalRam = (is32Bit && ramGb > 1) ? 1 : ramGb;

            string nativesDir = Path.Combine(vDir, "natives");
            if (Directory.Exists(nativesDir)) try { Directory.Delete(nativesDir, true); } catch { }
            Directory.CreateDirectory(nativesDir);

            // jar: у Fabric берём jar от inheritsFrom
            string jarId = !string.IsNullOrWhiteSpace(baseId) ? baseId : versionId;
            string jarPath = Path.Combine(_baseDir, "versions", jarId, $"{jarId}.jar");

            var cpList = new List<string>();

            if (baseDoc != null) ExtractLibs(baseRoot, cpList, nativesDir);
            ExtractLibs(root, cpList, nativesDir);

            if (File.Exists(jarPath))
                cpList.Add(jarPath);

            // убираем дубликаты библиотек (ASM и т.д.)
            cpList = NormalizeClasspath(cpList);

            string playerName = string.IsNullOrWhiteSpace(nickname) ? "LegitPlayer" : nickname;

            string assetsIndexId =
                (baseDoc != null && baseRoot.TryGetProperty("assetIndex", out var aiBase)) ? aiBase.GetProperty("id").GetString()
                : (root.TryGetProperty("assetIndex", out var aiCur) ? aiCur.GetProperty("id").GetString() : "legacy");

            // ✅ как мы уже правили: version_name для Fabric должен быть ванильным
            string versionNameForGame = !string.IsNullOrWhiteSpace(baseId) ? baseId : versionId;

            var argMap = new Dictionary<string, string> {
                {"${auth_player_name}", playerName},
                {"${version_name}", versionNameForGame},
                {"${game_directory}", instanceDir},
                {"${assets_root}", Path.Combine(_baseDir, "assets")},
                {"${assets_index_name}", assetsIndexId},

                // ✅ ОСНОВНЫЕ ИСПРАВЛЕНИЯ ДЛЯ 1.16.5+:
                {"${auth_uuid}", Uuid},
                {"${auth_access_token}", AccessToken},
                {"${user_type}", "msa"},             // Меняем mojang на msa
                {"${clientid}", AccessToken},        // Добавляем (используем токен как ID клиента)
                {"${xuid}", "1"},                    // Добавляем (любое ненулевое значение)
                {"${user_properties}", "{}"},        // Должно быть пустым JSON-объектом

                {"${version_type}", "release"},
                {"${natives_directory}", nativesDir},
                {"${launcher_name}", "LegitLauncher"},
                {"${launcher_version}", "1.0"},
                {"${classpath}", string.Join(Path.PathSeparator, cpList)}
            };

            var finalArgs = new List<string>
            {
                $"-Xmx{finalRam}G",
                $"-Djava.library.path={nativesDir}",

                // как у HelloMine: можно оставить (не мешает)
                "-Dminecraft.launcher.brand=HelloMine",
                "-Dminecraft.launcher.version=1.0"
            };

            // JVM args: базовые (inheritsFrom), иначе текущие
            bool usedJvm = false;
            if (baseDoc != null && baseRoot.TryGetProperty("arguments", out var bArgs) && bArgs.TryGetProperty("jvm", out var bJvm))
            {
                foreach (var arg in bJvm.EnumerateArray()) AddFormatted(finalArgs, arg, argMap);
                usedJvm = true;
            }
            if (!usedJvm && root.TryGetProperty("arguments", out var argsObj) && argsObj.TryGetProperty("jvm", out var jvmArgs))
                foreach (var arg in jvmArgs.EnumerateArray()) AddFormatted(finalArgs, arg, argMap);

            finalArgs.Add("-cp");
            finalArgs.Add(string.Join(Path.PathSeparator, cpList));

            string mainClass =
                root.TryGetProperty("mainClass", out var mc) ? mc.GetString()
                : (baseDoc != null && baseRoot.TryGetProperty("mainClass", out var bmc) ? bmc.GetString()
                : null);

            finalArgs.Add(mainClass ?? throw new InvalidOperationException("mainClass не найден"));

            // game args: базовые (inheritsFrom), иначе текущие
            bool usedGameArgs = false;
            if (baseDoc != null && baseRoot.TryGetProperty("arguments", out var bA) && bA.TryGetProperty("game", out var bGame))
            {
                foreach (var arg in bGame.EnumerateArray()) AddFormatted(finalArgs, arg, argMap);
                usedGameArgs = true;
            }

            if (!usedGameArgs && root.TryGetProperty("arguments", out var aObj) && aObj.TryGetProperty("game", out var gArgs))
            {
                foreach (var arg in gArgs.EnumerateArray()) AddFormatted(finalArgs, arg, argMap);
            }
            else if (!usedGameArgs)
            {
                JsonElement oldArgsRoot = default;
                bool hasOld = false;

                if (baseDoc != null && baseRoot.TryGetProperty("minecraftArguments", out var oldBase))
                {
                    oldArgsRoot = oldBase;
                    hasOld = true;
                }
                else if (root.TryGetProperty("minecraftArguments", out var oldCur))
                {
                    oldArgsRoot = oldCur;
                    hasOld = true;
                }

                if (hasOld)
                {
                    string raw = oldArgsRoot.GetString();
                    foreach (var kv in argMap) raw = raw.Replace(kv.Key, kv.Value);
                    finalArgs.AddRange(raw.Split(' '));
                }
            }

            var cleanedArgs = finalArgs
                .Where(arg => !arg.StartsWith("--quickPlay") && arg != "--demo" && !string.IsNullOrWhiteSpace(arg))
                .ToList();

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    WorkingDirectory = instanceDir,
                    Arguments = BuildArgsString(cleanedArgs, JavaService.GetRequiredJavaVersion(versionNameForGame) > 8),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            baseDoc?.Dispose();
            return proc;
        }

        private void ExtractLibs(JsonElement root, List<string> cpList, string nativesDir)
        {
            if (!root.TryGetProperty("libraries", out var libs)) return;

            foreach (var lib in libs.EnumerateArray())
            {
                if (!_mcService.CheckRules(lib)) continue;

                // Mojang формат
                if (lib.TryGetProperty("downloads", out var dls))
                {
                    if (dls.TryGetProperty("artifact", out var art))
                    {
                        string p = Path.GetFullPath(Path.Combine(_baseDir, "libraries",
                            art.GetProperty("path").GetString().Replace('/', Path.DirectorySeparatorChar)));
                        if (File.Exists(p)) cpList.Add(p);
                    }

                    string osKey = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "natives-windows" : "natives-linux";
                    if (dls.TryGetProperty("classifiers", out var cls) && cls.TryGetProperty(osKey, out var nArt))
                    {
                        string nPath = Path.Combine(_baseDir, "libraries",
                            nArt.GetProperty("path").GetString().Replace('/', Path.DirectorySeparatorChar));

                        if (File.Exists(nPath))
                        {
                            try
                            {
                                using var zip = ZipFile.OpenRead(nPath);
                                foreach (var e in zip.Entries)
                                {
                                    if (!e.FullName.StartsWith("META-INF"))
                                    {
                                        string op = Path.Combine(nativesDir, e.FullName);
                                        Directory.CreateDirectory(Path.GetDirectoryName(op));
                                        e.ExtractToFile(op, true);
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    continue;
                }

                // Fabric формат: name
                if (lib.TryGetProperty("name", out var nameEl))
                {
                    string name = nameEl.GetString();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var parts = name.Split(':');
                    if (parts.Length < 3) continue;

                    string relPath = $"{parts[0].Replace('.', '/')}/{parts[1]}/{parts[2]}/{parts[1]}-{parts[2]}.jar";
                    string localPath = Path.Combine(_baseDir, "libraries", relPath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(localPath)) cpList.Add(localPath);
                }
            }
        }

        private List<string> NormalizeClasspath(List<string> cpList)
        {
            var jarKeep = new List<string>();
            var libsOnly = new List<string>();

            foreach (var p in cpList.Distinct())
            {
                if (p.Contains(Path.DirectorySeparatorChar + "versions" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    jarKeep.Add(p);
                else
                    libsOnly.Add(p);
            }

            var best = new Dictionary<string, (string path, string version)>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in libsOnly)
            {
                if (!TryParseMavenFromPath(path, out var key, out var version))
                {
                    key = "FILE:" + path;
                    version = "0";
                }

                if (!best.TryGetValue(key, out var cur) || CompareVersions(version, cur.version) > 0)
                    best[key] = (path, version);
            }

            var normalized = best.Values.Select(v => v.path).Where(File.Exists).ToList();
            normalized.AddRange(jarKeep.Where(File.Exists));
            return normalized.Distinct().ToList();
        }

        private bool TryParseMavenFromPath(string fullPath, out string key, out string version)
        {
            key = null;
            version = null;

            var libsRoot = Path.Combine(_baseDir, "libraries") + Path.DirectorySeparatorChar;
            var norm = Path.GetFullPath(fullPath);
            var libsRootNorm = Path.GetFullPath(libsRoot);

            if (!norm.StartsWith(libsRootNorm, StringComparison.OrdinalIgnoreCase))
                return false;

            var rel = norm.Substring(libsRootNorm.Length);
            var parts = rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return false;

            string artifact = parts[^3];
            version = parts[^2];
            string group = string.Join(".", parts.Take(parts.Length - 3));

            string file = parts[^1];
            string classifier = "";

            if (file.StartsWith(artifact + "-" + version, StringComparison.OrdinalIgnoreCase))
            {
                var tail = file.Substring((artifact + "-" + version).Length);
                if (tail.StartsWith("-", StringComparison.Ordinal)) classifier = tail.Substring(1);
                if (classifier.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    classifier = classifier.Substring(0, classifier.Length - 4);
            }
            else classifier = file;

            key = $"{group}:{artifact}:{classifier}";
            return true;
        }

        private int CompareVersions(string a, string b)
        {
            if (a == b) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            var pa = a.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var pb = b.Split('.', StringSplitOptions.RemoveEmptyEntries);
            int n = Math.Max(pa.Length, pb.Length);

            for (int i = 0; i < n; i++)
            {
                int ai = (i < pa.Length && int.TryParse(pa[i], out var x)) ? x : 0;
                int bi = (i < pb.Length && int.TryParse(pb[i], out var y)) ? y : 0;
                if (ai != bi) return ai.CompareTo(bi);
            }
            return string.CompareOrdinal(a, b);
        }

        private void AddFormatted(List<string> list, JsonElement el, Dictionary<string, string> map)
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                string s = el.GetString();
                foreach (var kv in map) s = s.Replace(kv.Key, kv.Value);
                list.Add(s);
            }
            else if (_mcService.CheckRules(el))
            {
                var val = el.GetProperty("value");
                if (val.ValueKind == JsonValueKind.Array)
                    foreach (var v in val.EnumerateArray()) AddFormatted(list, v, map);
                else AddFormatted(list, val, map);
            }
        }

        private string BuildArgsString(List<string> args, bool useArgFile)
        {
            if (useArgFile)
            {
                string f = Path.Combine(Path.GetTempPath(), $"mc_{Guid.NewGuid()}.args");
                File.WriteAllLines(f, args);
                return $"@\"{f}\"";
            }
            return string.Join(" ", args.Select(a => (a.Contains(" ") && !a.StartsWith("\"")) ? $"\"{a}\"" : a));
        }
    }
}