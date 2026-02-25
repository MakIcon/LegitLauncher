using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace McLauncher.Services.Mods
{
    /// <summary>
    /// Хранит реестр установленных модов для конкретной папки mods:
    /// .installed_mods.json : { "project_id": "filename.jar" }
    /// </summary>
    public sealed class ModManifestService
    {
        private const string ManifestFileName = ".installed_mods.json";

        public Dictionary<string, string> Load(string modsFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modsFolder))
                    return new Dictionary<string, string>();

                string path = GetManifestPath(modsFolder);
                if (!File.Exists(path))
                    return new Dictionary<string, string>();

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        public void Save(string modsFolder, Dictionary<string, string> manifest)
        {
            if (string.IsNullOrWhiteSpace(modsFolder))
                return;

            Directory.CreateDirectory(modsFolder);

            string path = GetManifestPath(modsFolder);
            var json = JsonSerializer.Serialize(manifest ?? new Dictionary<string, string>());
            File.WriteAllText(path, json);
        }

        public bool IsInstalled(string modsFolder, string projectId, out string filename)
        {
            filename = null;

            var manifest = Load(modsFolder);
            if (!manifest.TryGetValue(projectId, out filename))
                return false;

            if (string.IsNullOrWhiteSpace(filename))
                return false;

            return File.Exists(Path.Combine(modsFolder, filename));
        }

        public void MarkInstalled(string modsFolder, string projectId, string filename)
        {
            var manifest = Load(modsFolder);
            manifest[projectId] = filename;
            Save(modsFolder, manifest);
        }

        public void MarkRemoved(string modsFolder, string projectId)
        {
            var manifest = Load(modsFolder);
            if (manifest.Remove(projectId))
                Save(modsFolder, manifest);
        }

        private static string GetManifestPath(string modsFolder)
            => Path.Combine(modsFolder, ManifestFileName);
    }
}