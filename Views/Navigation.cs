using McLauncher.Services;
using System.Windows;
using System.Windows.Controls;

namespace McLauncher
{
    public partial class MainWindow
    {
        private void BtnNavPlay_Click(object sender, RoutedEventArgs e) => SwitchView(ViewPlay, BtnNavPlay);
        private void BtnNavSettings_Click(object sender, RoutedEventArgs e) => SwitchView(ViewSettings, BtnNavSettings);
        private void BtnNavTelemetry_Click(object sender, RoutedEventArgs e) { SwitchView(ViewTelemetry, BtnNavTelemetry); _timer.Start(); }

        private void BtnNavMods_Click(object sender, RoutedEventArgs e)
        {
            SwitchView(ViewMods, BtnNavMods);
            LoadModVersions();
            LoadPopularMods();
        }

        private void SwitchView(FrameworkElement view, Button btn)
        {
            ViewPlay.Visibility =
                ViewSettings.Visibility =
                ViewTelemetry.Visibility =
                ViewMods.Visibility = Visibility.Collapsed;

            view.Visibility = Visibility.Visible;

            BtnNavPlay.Tag = BtnNavSettings.Tag = BtnNavTelemetry.Tag = BtnNavMods.Tag = "";
            btn.Tag = "Active";

            if (view != ViewTelemetry) _timer.Stop();
        }

        private void UpdateTelemetry()
        {
            long lMem = TelemetryService.GetLauncherMemory();
            long mMem = TelemetryService.GetMinecraftMemory();
            long total = TelemetryService.GetTotalMemory();

            TxtLauncherMem.Text = TelemetryService.FormatBytes(lMem);
            PbLauncher.Value = (double)lMem / total * 100;
            TxtLauncherPercent.Text = $"{(int)PbLauncher.Value}%";

            if (mMem > 0)
            {
                TxtMinecraftStatus.Text = "Активна";
                TxtMinecraftMem.Text = TelemetryService.FormatBytes(mMem);

                double maxAllocated = SliderRam.Value * 1024 * 1024 * 1024;
                PbMinecraft.Value = (double)mMem / maxAllocated * 100;
                TxtMinecraftPercent.Text = $"{(int)PbMinecraft.Value}%";
            }
            else
            {
                TxtMinecraftStatus.Text = "Ожидание";
                TxtMinecraftMem.Text = "—";
                PbMinecraft.Value = 0;
                TxtMinecraftPercent.Text = "0%";
            }
        }
    }
}