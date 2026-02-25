using McLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace McLauncher
{
    public partial class MainWindow
    {
        private void LoadModVersions()
        {
            var versionsDir = Path.Combine(_baseDir, "versions");
            if (!Directory.Exists(versionsDir)) return;

            var installedFabric = Directory.GetDirectories(versionsDir)
                .Select(Path.GetFileName)
                .Where(name => name != null && name.Contains("fabric-loader"))
                .Select(name =>
                {
                    // "fabric-loader-0.15.3-1.20.1" -> "fabric: 1.20.1"
                    string gameVer = name.Split('-').Last();
                    return new ModVersionDisplay
                    {
                        FullName = name,
                        DisplayName = $"fabric: {gameVer}"
                    };
                })
                .ToList();

            ComboModVersions.ItemsSource = installedFabric;
            ComboModVersions.DisplayMemberPath = "DisplayName";
            ComboModVersions.SelectedValuePath = "FullName";

            if (ComboModVersions.Items.Count > 0 && ComboModVersions.SelectedIndex == -1)
                ComboModVersions.SelectedIndex = 0;
        }

        private async void LoadPopularMods()
        {
            if (ComboModVersions.SelectedValue == null) return;

            string selectedId = ComboModVersions.SelectedValue.ToString();
            string gameVersion = selectedId.Split('-').Last();

            var mods = await _modrinth.GetPopularModsAsync(gameVersion, "fabric");
            RefreshInstalledStatus(mods);
            ModsList.ItemsSource = mods;
        }

        private async void BtnSearchMods_Click(object sender, RoutedEventArgs e)
        {
            if (ComboModVersions.SelectedValue == null) return;

            if (string.IsNullOrWhiteSpace(TxtModSearch.Text))
            {
                LoadPopularMods();
                return;
            }

            string selectedId = ComboModVersions.SelectedValue.ToString();
            string gameVersion = selectedId.Split('-').Last();

            var mods = await _modrinth.SearchModsAsync(TxtModSearch.Text, gameVersion, "fabric");
            RefreshInstalledStatus(mods);
            ModsList.ItemsSource = mods;
        }

        private void RefreshInstalledStatus(IEnumerable<ModItem> mods)
        {
            if (mods == null || ComboModVersions.SelectedValue == null) return;

            string selectedId = ComboModVersions.SelectedValue.ToString();
            string modsFolder = Path.Combine(_baseDir, "instances", selectedId, "mods");

            foreach (var mod in mods)
                mod.IsInstalled = _modManifest.IsInstalled(modsFolder, mod.project_id, out _);
        }

        private async void BtnInstallMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not ModItem mod) return;
            if (ComboModVersions.SelectedValue == null) return;

            string selectedId = ComboModVersions.SelectedValue.ToString();
            string gameVersion = selectedId.Split('-').Last();
            string modsFolder = Path.Combine(_baseDir, "instances", selectedId, "mods");

            Log($"Установка {mod.title}...");

            mod.IsDownloading = true;
            mod.DownloadProgress = 0;

            try
            {
                var progress = new Progress<double>(p => mod.DownloadProgress = p);

                var (ok, message, filename) = await _modrinth.DownloadLatestWithProgressAsync(
                    mod, gameVersion, "fabric", modsFolder, progress, CancellationToken.None);

                if (ok)
                {
                    _modManifest.MarkInstalled(modsFolder, mod.project_id, filename);
                    mod.IsInstalled = true;
                    Log($"Успешно: {mod.title}");
                }
                else
                {
                    Log($"Ошибка: {message}");
                }
            }
            finally
            {
                mod.IsDownloading = false;
                mod.DownloadProgress = 0;
            }
        }

        private void BtnRemoveMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not ModItem mod) return;
            if (ComboModVersions.SelectedValue == null) return;

            string selectedId = ComboModVersions.SelectedValue.ToString();
            string modsFolder = Path.Combine(_baseDir, "instances", selectedId, "mods");

            try
            {
                if (_modManifest.IsInstalled(modsFolder, mod.project_id, out var filename))
                {
                    string filePath = Path.Combine(modsFolder, filename);
                    if (File.Exists(filePath)) File.Delete(filePath);

                    _modManifest.MarkRemoved(modsFolder, mod.project_id);

                    mod.IsInstalled = false;
                    Log($"Удалено: {mod.title}");
                }
                else
                {
                    mod.IsInstalled = false;
                    Log($"Мод не найден в реестре: {mod.title}");
                }
            }
            catch (Exception ex)
            {
                Log("Ошибка удаления: " + ex.Message);
            }
        }

        private void ComboModVersions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && ModsList.ItemsSource is IEnumerable<ModItem> currentMods)
                RefreshInstalledStatus(currentMods);
        }

        private void TxtModSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                BtnSearchMods_Click(null, null);
        }
    }
}