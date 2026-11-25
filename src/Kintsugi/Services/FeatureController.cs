using Microsoft.Win32;

namespace Kintsugi.Services;

/// <summary>Writes feature configuration to the registry.</summary>
public static class FeatureController
{
    private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard";

    /// <summary>
    /// Policy value written by Windows Security Patcher, the predecessor of Kintsugi. It is not a documented Windows
    /// policy, and values under the Policies key can make Windows Security show a setting as managed by an
    /// administrator, so Kintsugi removes it instead of writing it.
    /// </summary>
    public const string LegacyPolicyValue = "HvcicpKernelModeHardwareEnforcedStackProtection";

    /// <summary>
    /// Sets the feature's <c>Enabled</c> value, the same value the Windows Security toggle writes.
    /// Returns true if the legacy policy value was found and removed.
    /// </summary>
    public static bool SetEnabled(SecurityFeature feature, bool enabled)
    {
        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using (RegistryKey key = hklm.CreateSubKey(SecurityFeatureInfo.ScenarioKey(feature)))
        {
            key.SetValue("Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);
        }

        return feature == SecurityFeature.KernelStackProtection && RemoveLegacyPolicy(hklm);
    }

    private static bool RemoveLegacyPolicy(RegistryKey hklm)
    {
        using RegistryKey? policy = hklm.OpenSubKey(PolicyKey, writable: true);
        if (policy?.GetValue(LegacyPolicyValue) is null)
        {
            return false;
        }

        policy.DeleteValue(LegacyPolicyValue, throwOnMissingValue: false);
        return true;
    }

    /// <summary>Lists "name = value" pairs under the Device Guard policy key.</summary>
    public static IReadOnlyList<string> ReadPolicyOverrides()
    {
        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? policy = hklm.OpenSubKey(PolicyKey);
        return policy is null
            ? []
            : policy.GetValueNames().Select(name => $"{name} = {policy.GetValue(name)}").ToList();
    }
}
