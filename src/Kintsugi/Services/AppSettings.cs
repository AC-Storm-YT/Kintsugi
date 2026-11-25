using Microsoft.Win32;

namespace Kintsugi.Services;

/// <summary>Per-user settings, stored under HKCU\Software\Kintsugi.</summary>
public static class AppSettings
{
    private const string KeyPath = @"Software\Kintsugi";

    public static bool HasAcceptedAgreement
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("AcceptedAgreement") is int value && value == 1;
        }
        set
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue("AcceptedAgreement", value ? 1 : 0, RegistryValueKind.DWord);
        }
    }
}
