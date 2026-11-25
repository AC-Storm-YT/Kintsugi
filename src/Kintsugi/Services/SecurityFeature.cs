namespace Kintsugi.Services;

/// <summary>The Windows security features Kintsugi can switch on and off.</summary>
public enum SecurityFeature
{
    MemoryIntegrity,
    KernelStackProtection
}

public static class SecurityFeatureInfo
{
    private const string ScenariosRoot = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\";

    public static string DisplayName(SecurityFeature feature) => feature switch
    {
        SecurityFeature.MemoryIntegrity => "Memory Integrity",
        _ => "Kernel Stack Protection"
    };

    /// <summary>HKLM sub-key that holds the feature's <c>Enabled</c> value.</summary>
    public static string ScenarioKey(SecurityFeature feature) => ScenariosRoot + feature switch
    {
        SecurityFeature.MemoryIntegrity => "HypervisorEnforcedCodeIntegrity",
        _ => "KernelShadowStacks"
    };

    /// <summary>
    /// Value reported in <c>Win32_DeviceGuard.SecurityServicesRunning</c> when the feature is active
    /// (2 = hypervisor-enforced code integrity, 5 = kernel-mode hardware-enforced stack protection).
    /// </summary>
    public static int DeviceGuardServiceId(SecurityFeature feature) =>
        feature == SecurityFeature.MemoryIntegrity ? 2 : 5;
}
