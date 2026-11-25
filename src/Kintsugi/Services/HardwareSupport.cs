using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Kintsugi.Services;

/// <summary>Hardware capability checks.</summary>
public static partial class HardwareSupport
{
    private const uint UserCetEnvironmentWin32Process = 0;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsUserCetAvailableInEnvironment(uint userCetEnvironment);

    /// <summary>
    /// Asks Windows whether this CPU supports hardware shadow stacks (Intel CET / AMD Zen 3+),
    /// which Kernel Stack Protection requires. Returns null when the API is unavailable.
    /// </summary>
    public static bool? IsShadowStackSupported()
    {
        try
        {
            return IsUserCetAvailableInEnvironment(UserCetEnvironmentWin32Process);
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Processor name as reported by the firmware, read from the registry.</summary>
    public static string GetCpuName()
    {
        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? cpu = hklm.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        string? name = (cpu?.GetValue("ProcessorNameString") as string)?.Trim();
        return string.IsNullOrEmpty(name) ? "Unknown CPU" : name;
    }
}
