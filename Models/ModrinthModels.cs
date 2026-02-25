using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace McLauncher.Models
{
    // Класс для отображения версий в ComboBox
    public class ModVersionDisplay
    {
        public string FullName { get; set; }    // fabric-loader-0.18.4-1.21.4
        public string DisplayName { get; set; } // fabric: 1.21.4
    }

    // Результат поиска Modrinth
    public class ModrinthSearchResult
    {
        public List<ModItem> hits { get; set; }
    }

    // Класс мода
    public class ModItem : INotifyPropertyChanged
    {
        public string project_id { get; set; }
        public string slug { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public string icon_url { get; set; }
        public string author { get; set; }

        private bool _isInstalled;
        public bool IsInstalled
        {
            get => _isInstalled;
            set { _isInstalled = value; OnPropertyChanged(); }
        }

        private bool _isDownloading;
        public bool IsDownloading
        {
            get => _isDownloading;
            set { _isDownloading = value; OnPropertyChanged(); }
        }

        private double _downloadProgress; // 0..100
        public double DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ModVersion
    {
        public string id { get; set; }
        public List<ModFile> files { get; set; }

        // добавь:
        public DateTime date_published { get; set; }
    }

    public class ModFile
    {
        public string url { get; set; }
        public string filename { get; set; }
        public bool primary { get; set; }
    }
}