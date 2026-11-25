using System.Management;

namespace Kintsugi.Services;

/// <summary>Creates System Restore points through WMI.</summary>
public static class RestorePoint
{
    private const int ModifySettings = 12;      // RESTOREPOINTINFO.dwRestorePtType
    private const int BeginSystemChange = 100;  // RESTOREPOINTINFO.dwEventType

    /// <summary>
    /// Creates a restore point. Returns null on success, otherwise a readable error.
    /// Windows skips creation (and still reports success) if another restore point
    /// was made in the last 24 hours.
    /// </summary>
    public static string? Create(string description)
    {
        try
        {
            using var restore = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            object result = restore.InvokeMethod("CreateRestorePoint", [description, ModifySettings, BeginSystemChange]);
            return Convert.ToUInt32(result) switch
            {
                0 => null,
                // ERROR_SERVICE_DISABLED: System Restore is turned off for the system drive.
                1058 => "System Restore is turned off. Turn it on in System Properties > System Protection.",
                uint code => $"System Restore returned error {code}."
            };
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
