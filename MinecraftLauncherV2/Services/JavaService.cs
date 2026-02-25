using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace McLauncher.Services
{
    public static class JavaService
    {
        private static readonly string JavaExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "java.exe" : "java";

        public static async Task<string?> AutoDetectJavaAsync(int targetVersion)
        {
            return await Task.Run(() =>
            {
                var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 1. Проверяем переменную JAVA_HOME
                var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (!string.IsNullOrEmpty(javaHome))
                {
                    AddIfValid(candidates, Path.Combine(javaHome, "bin", JavaExeName));
                }

                // 2. Проверяем PATH
                var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var p in envPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    AddIfValid(candidates, Path.Combine(p, JavaExeName));
                }

                // 3. Стандартные папки установки (без глубокой рекурсии!)
                foreach (var root in GetSystemJavaRoots())
                {
                    if (!Directory.Exists(root)) continue;

                    try
                    {
                        // Ищем только на 2 уровня вглубь: Root -> VendorFolder -> bin/java.exe
                        foreach (var subDir in Directory.EnumerateDirectories(root))
                        {
                            AddIfValid(candidates, Path.Combine(subDir, "bin", JavaExeName));
                            
                            // Для macOS специфичных путей
                            AddIfValid(candidates, Path.Combine(subDir, "Contents", "Home", "bin", JavaExeName));
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                }

                string targetStr = targetVersion == 8 ? "1.8" : targetVersion.ToString();

                // Сортировка: приоритет тем, где в пути есть нужная версия и слово "jdk"
                return candidates
                    .OrderByDescending(f => f.Contains(targetStr, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(f => f.Contains("jdk", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
            });
        }

        private static void AddIfValid(HashSet<string> candidates, string fullPath)
        {
            if (File.Exists(fullPath))
            {
                candidates.Add(Path.GetFullPath(fullPath));
            }
        }

        private static IEnumerable<string> GetSystemJavaRoots()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java");
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Foundation");
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AdoptOpenJDK");
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                yield return "/Library/Java/JavaVirtualMachines";
            }
            else // Linux
            {
                yield return "/usr/lib/jvm";
                yield return "/usr/java";
            }
        }

        public static int GetRequiredJavaVersion(string versionId)
        {
            if (string.IsNullOrWhiteSpace(versionId)) return 8;

            // Очистка строки от префиксов Fabric/Quilt
            string cleanVersion = versionId;
            if (versionId.Contains(":")) 
                cleanVersion = versionId.Split(':').Last();
            
            if (versionId.StartsWith("fabric-loader-"))
                cleanVersion = versionId.Split('-').Last();

            // Парсинг версии (учитываем форматы 1.20.1, 1.21 и т.д.)
            if (IsVersionAtLeast(cleanVersion, 1, 20, 5)) return 21;
            if (IsVersionAtLeast(cleanVersion, 1, 18)) return 17;
            if (IsVersionAtLeast(cleanVersion, 1, 17)) return 16;

            return 8;
        }

        private static bool IsVersionAtLeast(string version, int major, int minor, int build = 0)
        {
            var parts = version.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();
            if (parts.Length < 2) return false;

            if (parts[0] > major) return true;
            if (parts[0] < major) return false;

            if (parts[1] > minor) return true;
            if (parts[1] < minor) return false;

            return parts.Length <= 2 || parts[2] >= build;
        }
    }
}