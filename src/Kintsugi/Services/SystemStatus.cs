using System.Management;
using Microsoft.Win32;

namespace Kintsugi.Services;

/// <summary>Configured (registry) and live (Device Guard) state of one feature.</summary>
/// <param name="Configured">Registry <c>Enabled</c> value, or null when the value is not set.</param>
/// <param name="Running">Whether Windows reports the feature as running; null when Device Guard could not be queried.</param>
public sealed record FeatureState(SecurityFeature Feature, bool? Configured, bool? Running)
{
    public bool IsConfiguredOn => Configured == true;

    /// <summary>The configuration differs from what is running, so a restart is needed to apply it.</summary>
    public bool PendingRestart => Running.HasValue && Running.Value != IsConfiguredOn;
}

/// <summary>Snapshot of everything the UI shows about the system.</summary>
public sealed class SystemStatus
{
    public required FeatureState MemoryIntegrity { get; init; }
    public required FeatureState KernelStackProtection { get; init; }

    /// <summary>0 = off, 1 = enabled but not running, 2 = running; null when unknown.</summary>
    public int? VbsStatus { get; init; }

    /// <summary>Hardware/firmware virtualization support (Device Guard "hypervisor support"); null when unknown.</summary>
    public bool? VirtualizationAvailable { get; init; }

    public required string CpuName { get; init; }

    /// <summary>CPU and OS support hardware shadow stacks (Intel CET / AMD shadow stack); null when unknown.</summary>
    public bool? ShadowStacksSupported { get; init; }

    /// <summary>Values under the Device Guard policy key; these can grey out the Windows Security toggles.</summary>
    public required IReadOnlyList<string> PolicyOverrides { get; init; }

    public string? DeviceGuardError { get; init; }

    public bool AnyPendingRestart => MemoryIntegrity.PendingRestart || KernelStackProtection.PendingRestart;

    public FeatureState Get(SecurityFeature feature) =>
        feature == SecurityFeature.MemoryIntegrity ? MemoryIntegrity : KernelStackProtection;

    /// <summary>Reads the current state. Does WMI and registry I/O, so call it off the UI thread.</summary>
    public static SystemStatus Read()
    {
        HashSet<int>? running = null;
        bool? virtualization = null;
        int? vbs = null;
        string? error = null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\DeviceGuard",
                "SELECT AvailableSecurityProperties, SecurityServicesRunning, VirtualizationBasedSecurityStatus FROM Win32_DeviceGuard");
            using ManagementObjectCollection results = searcher.Get();
            foreach (ManagementBaseObject item in results)
            {
                running = [.. ToInts(item["SecurityServicesRunning"])];
                virtualization = ToInts(item["AvailableSecurityProperties"]).Contains(1);
                vbs = item["VirtualizationBasedSecurityStatus"] is { } v ? Convert.ToInt32(v) : null;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return new SystemStatus
        {
            MemoryIntegrity = ReadFeature(SecurityFeature.MemoryIntegrity, running),
            KernelStackProtection = ReadFeature(SecurityFeature.KernelStackProtection, running),
            VbsStatus = vbs,
            VirtualizationAvailable = virtualization,
            CpuName = HardwareSupport.GetCpuName(),
            ShadowStacksSupported = HardwareSupport.IsShadowStackSupported(),
            PolicyOverrides = FeatureController.ReadPolicyOverrides(),
            DeviceGuardError = error
        };
    }

    private static FeatureState ReadFeature(SecurityFeature feature, HashSet<int>? running)
    {
        bool? configured = null;
        using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (RegistryKey? key = hklm.OpenSubKey(SecurityFeatureInfo.ScenarioKey(feature)))
        {
            if (key?.GetValue("Enabled") is { } value)
            {
                configured = Convert.ToInt32(value) == 1;
            }
        }

        bool? isRunning = running?.Contains(SecurityFeatureInfo.DeviceGuardServiceId(feature));
        return new FeatureState(feature, configured, isRunning);
    }

    private static IEnumerable<int> ToInts(object? value) =>
        value is Array array ? array.Cast<object>().Select(Convert.ToInt32) : [];
}
