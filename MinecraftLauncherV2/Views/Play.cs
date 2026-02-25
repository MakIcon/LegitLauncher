using McLauncher.Models;
using McLauncher.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace McLauncher
{
    public partial class MainWindow
    {
        private void UpdateVersionList()
        {
            VersionsList.ItemsSource = _mcService.GetFilteredVersions(
                CbSnapshots.IsChecked == true,
                CbPreReleases.IsChecked == true,
                CbFabric.IsChecked == true
            );
        }

        private async void VersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionsList.SelectedItem is not VersionRef sel) return;

            if (sel is FabricVersionRef fsel)
            {
                if (string.IsNullOrEmpty(fsel.GameVersion))
                    fsel.GameVersion = _mcService.GetFabricGameVersions().FirstOrDefault() ?? "1.20.1";

                if (string.IsNullOrEmpty(fsel.LoaderVersion))
                {
                    var loaders = await _mcService.GetFabricLoaderVersionsAsync(fsel.GameVersion, CancellationToken.None);
                    fsel.LoaderVersion = loaders.FirstOrDefault() ?? "0.15.3";
                }
            }

            if (_gameProcess == null || _gameProcess.HasExited)
            {
                BtnDownload.Visibility = sel.IsInstalled ? Visibility.Collapsed : Visibility.Visible;
                BtnLaunch.IsEnabled = sel.IsInstalled;
                BtnLaunch.Content = "ИГРАТЬ";
                BtnLaunch.Background = BrushPlay;
            }

            int ver = JavaService.GetRequiredJavaVersion(sel.id);
            string path = await JavaService.AutoDetectJavaAsync(ver);
            if (!string.IsNullOrEmpty(path)) TxtJavaPath.Text = path;
        }

        private async void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (_gameProcess != null && !_gameProcess.HasExited)
            {
                try { _gameProcess.Kill(); Log("Игра остановлена."); } catch { }
                return;
            }

            if (VersionsList.SelectedItem is not VersionRef sel) return;

            try
            {
                string launchId = ResolveLaunchId(sel);

                Log($"Запуск {launchId}...");
                var proc = await _launcher.LaunchAsync(
                    launchId,
                    TxtPlayerName.Text,
                    TxtJavaPath.Text,
                    (int)SliderRam.Value,
                    Log);

                proc.OutputDataReceived += (_, ev) => { if (ev.Data != null) Log("[GAME] " + ev.Data); };
                proc.ErrorDataReceived += (_, ev) => { if (ev.Data != null) Log("[ERR] " + ev.Data); };

                if (!proc.Start())
                    return;

                _gameProcess = proc;
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                BtnLaunch.Content = "ЗАКРЫТЬ";
                BtnLaunch.Background = BrushStop;
                Log("Игра запущена!");

                _ = Task.Run(() =>
                {
                    proc.WaitForExit();
                    TryLogLastCrash(launchId);

                    Dispatcher.Invoke(() =>
                    {
                        _gameProcess = null;
                        BtnLaunch.Content = "ИГРАТЬ";
                        BtnLaunch.Background = BrushPlay;
                        Log("Игра закрыта.");
                    });
                });
            }
            catch (Exception ex)
            {
                Log("Ошибка: " + ex.Message);
            }
        }

        private string ResolveLaunchId(VersionRef sel)
        {
            if (sel is not FabricVersionRef fsel)
                return sel.id;

            string launchId = $"fabric-loader-{fsel.LoaderVersion}-{fsel.GameVersion}";
            string versionPath = Path.Combine(_baseDir, "versions", launchId, launchId + ".json");

            if (!File.Exists(versionPath))
            {
                Log($"Ошибка: Не найден файл {launchId}.json");
                BtnDownload.Visibility = Visibility.Visible;
                BtnLaunch.IsEnabled = false;
                throw new Exception("Профиль Fabric не найден. Скачайте его.");
            }

            return launchId;
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (VersionsList.SelectedItem is not VersionRef sel) return;

            BtnDownload.IsEnabled = false;

            try
            {
                if (sel is FabricVersionRef fsel)
                {
                    if (string.IsNullOrEmpty(fsel.GameVersion))
                        fsel.GameVersion = _mcService.GetFabricGameVersions().FirstOrDefault();

                    if (string.IsNullOrEmpty(fsel.LoaderVersion))
                    {
                        var loaders = await _mcService.GetFabricLoaderVersionsAsync(fsel.GameVersion, CancellationToken.None);
                        fsel.LoaderVersion = loaders.FirstOrDefault();
                    }

                    Log($"Установка Fabric: {fsel.GameVersion}");
                }

                await _mcService.InstallVersionAsync(
                    sel,
                    Log,
                    p => Dispatcher.Invoke(() => Pb.Value = p),
                    CancellationToken.None);

                _mcService.RebuildInstalledFabricIndex();

                UpdateVersionList();
                VersionsList_SelectionChanged(null, null);
                Log("Установка завершена.");
            }
            catch (Exception ex)
            {
                Log("Ошибка: " + ex.Message);
            }
            finally
            {
                BtnDownload.IsEnabled = true;
                Pb.Value = 0;
            }
        }

        private void TryLogLastCrash(string instanceKey)
        {
            try
            {
                string crashDir = Path.Combine(_baseDir, "instances", instanceKey, "crash-reports");
                if (!Directory.Exists(crashDir)) return;

                var last = Directory.GetFiles(crashDir, "crash-*.txt")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (last == null) return;

                Log($"[CRASH] Обнаружен отчет: {last.Name}");

                foreach (var l in File.ReadAllLines(last.FullName).Take(5))
                    Log("[CRASH] " + l);
            }
            catch { }
        }
    }
}