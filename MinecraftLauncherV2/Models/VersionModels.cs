using System.Collections.Generic;
using System.Text.Json;

namespace McLauncher.Models
{
    public class VersionManifest { public List<VersionRef> versions { get; set; } }

    public class VersionRef
    {
        public string id { get; set; }
        public string type { get; set; }
        public string url { get; set; }
        public bool IsInstalled { get; set; }
    }

    // Fabric-версия для UI (id показываем как fabric:1.20.1)
    public class FabricVersionRef : VersionRef
    {
        public string GameVersion { get; set; }
        public string LoaderVersion { get; set; }
        public string LaunchId { get; set; } // fabric-loader-0.16.5-1.21.1
    }

    public class VersionData
    {
        public string id { get; set; }
        public string mainClass { get; set; }
        public string minecraftArguments { get; set; }
        public AssetIndexInfo assetIndex { get; set; }
        public DownloadsData downloads { get; set; }
        public List<Library> libraries { get; set; }
        public ArgumentsData arguments { get; set; }

        // для модлоадеров (Fabric профили обычно наследуются от ваниллы)
        public string inheritsFrom { get; set; }
    }

    public class ArgumentsData
    {
        public List<JsonElement> game { get; set; }
        public List<JsonElement> jvm { get; set; }
    }

    public class AssetIndexInfo { public string id { get; set; } public string url { get; set; } }
    public class DownloadsData { public DownloadItem client { get; set; } }
    public class DownloadItem { public string url { get; set; } }

    public class Library
    {
        public LibDownloads downloads { get; set; }
        public List<JsonElement> rules { get; set; }
        public Dictionary<string, string> natives { get; set; }
    }

    public class LibDownloads
    {
        public Artifact artifact { get; set; }
        public Dictionary<string, Artifact> classifiers { get; set; }
    }

    public class Artifact { public string path { get; set; } public string url { get; set; } }
}