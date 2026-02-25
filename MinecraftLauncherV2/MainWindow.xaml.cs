using McLauncher.Models;
using McLauncher.Services;
using McLauncher.Services.Mods;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace McLauncher
{
    public partial class MainWindow : Window
    {
        private readonly MinecraftService _mcService;
        private readonly LaunchService _launcher;
        private readonly ModrinthService _modrinth;
        private readonly ModManifestService _modManifest;

        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly string _baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".legit");

        private Process _gameProcess;

        private static readonly SolidColorBrush BrushPlay = new SolidColorBrush(Color.FromRgb(56, 142, 60));
        private static readonly SolidColorBrush BrushStop = new SolidColorBrush(Color.FromRgb(183, 28, 28));

        public MainWindow()
        {
            InitializeComponent();

            _mcService = new MinecraftService(_baseDir);
            _launcher = new LaunchService(_baseDir, _mcService);

            _modrinth = new ModrinthService();
            _modManifest = new ModManifestService();

            _timer.Tick += (_, __) => UpdateTelemetry();

            // Инициализация при запуске
            Loaded += async (_, __) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            Log("Загрузка движка...");
            await _mcService.LoadManifestAsync();
            UpdateVersionList();

            // Авто-детект Java 8 по умолчанию
            TxtJavaPath.Text = await JavaService.AutoDetectJavaAsync(8);
            Log("Готов к работе.");
        }

        private void Log(string s)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Log(s));
                return;
            }

            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\n");
            TxtLog.ScrollToEnd();
            LblStatus.Text = s;
        }

        private void FilterChanged(object sender, RoutedEventArgs e) => UpdateVersionList();

        private void BtnBrowseJava_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "java.exe|java.exe" };
            if (dlg.ShowDialog() == true) TxtJavaPath.Text = dlg.FileName;
        }

        private async void AutoDetectJava_Click(object sender, RoutedEventArgs e)
            => TxtJavaPath.Text = await JavaService.AutoDetectJavaAsync(8);
    }
}