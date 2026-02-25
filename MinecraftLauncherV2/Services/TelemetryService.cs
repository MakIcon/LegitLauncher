using System;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace McLauncher.Services
{
    public static class TelemetryService
    {
        public static long GetLauncherMemory() => Process.GetCurrentProcess().PrivateMemorySize64;

        public static long GetMinecraftMemory()
        {
            try {
                // Ищем процесс java, который запущен из нашей папки .legit
                var query = "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE 'java%'";
                using var searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject mo in searcher.Get())
                {
                    if (mo["CommandLine"]?.ToString().Contains(".legit") == true)
                    {
                        var pid = Convert.ToInt32(mo["ProcessId"]);
                        return Process.GetProcessById(pid).PrivateMemorySize64;
                    }
                }
            } catch { }
            return 0;
        }

        public static long GetTotalMemory()
        {
            try {
                using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementObject mo in searcher.Get()) 
                {
                    // ИСПРАВЛЕНО: ToInt64 вместо ToLong
                    return Convert.ToInt64(mo["TotalPhysicalMemory"]); 
                }
            } catch { }
            return 8L * 1024 * 1024 * 1024; // Заглушка 8 ГБ, если WMI не сработал
        }

        public static string FormatBytes(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i = 0; 
            double dblSByte = bytes;
            while (dblSByte >= 1024 && i < Suffix.Length - 1)
            {
                dblSByte /= 1024;
                i++;
            }
            return $"{dblSByte:0.##} {Suffix[i]}";
        }
    }
}