using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace Kintsugi.Services;

/// <summary>Operating-system checks used at startup and after changes.</summary>
public static class WindowsEnvironment
{
    public const int MinimumBuild = 22621;  // Windows 11 22H2
    public static readonly Version MinimumSecurityAppVersion = new(1000, 25330, 0, 9000);

    public static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Windows build number, read from the registry.</summary>
    public static int GetBuildNumber()
    {
        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out int build)
            ? build
            : Environment.OSVersion.Version.Build;
    }

    /// <summary>
    /// Highest installed version of the Windows Security app (Microsoft.SecHealthUI), read from the
    /// AppX package repository. Returns null if it cannot be determined.
    /// </summary>
    public static Version? GetSecurityAppVersion()
    {
        const string repository = @"Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\PackageRepository\Packages";
        const string prefix = "Microsoft.SecHealthUI_";

        try
        {
            using RegistryKey? packages = Registry.ClassesRoot.OpenSubKey(repository);
            return packages?.GetSubKeyNames()
                .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(name => Version.TryParse(name[prefix.Length..].Split('_')[0], out Version? v) ? v : null)
                .OfType<Version>()
                .DefaultIfEmpty()
                .Max();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Schedules a restart in a few seconds so the user can see what is happening.</summary>
    public static void RestartSoon()
    {
        using Process? _ = Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"Kintsugi: restarting to apply security changes.\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }
}
