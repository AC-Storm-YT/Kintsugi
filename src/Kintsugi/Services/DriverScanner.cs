using System.IO;
using System.Management;
using System.Text.RegularExpressions;

namespace Kintsugi.Services;

/// <param name="Name">Driver file or package name, e.g. "ene.sys" or "wdcsam.inf".</param>
/// <param name="Source">Where it was found, e.g. "kernel driver, running" or "driver store package".</param>
/// <param name="Vendor">The product or vendor the pattern belongs to.</param>
public sealed record DriverFinding(string Name, string Source, string Vendor)
{
    public override string ToString() => $"{Name} [{Vendor}] ({Source})";
}

/// <summary>
/// Looks for drivers that are commonly reported as incompatible with Memory Integrity.
/// This is a heuristic: it can report false positives and cannot find every incompatible driver.
/// </summary>
public static partial class DriverScanner
{
    // Patterns are matched against the whole file name (not the path), so short prefixes
    // such as "ene" do not match unrelated drivers like "netgeneric.inf".
    private static readonly (Regex Pattern, string Vendor)[] KnownDrivers =
    [
        (Pattern(@"wdcsam\w*"),         "Western Digital (WD Drive Utilities / SES)"),
        (Pattern(@"vbox\w*"),           "Oracle VirtualBox (older versions)"),
        (Pattern(@"ssud(bus|mdm)\w*"),  "Samsung USB / modem drivers (older versions)"),
        (Pattern(@"lgcore\w*"),         "Logitech Gaming Software"),
        (Pattern(@"ene(io)?(64)?"),     "ENE Technology (RGB lighting control)"),
        (Pattern(@"enetechio\w*"),      "ENE Technology (RGB lighting control)"),
        (Pattern(@"gio\w*"),            "GIGABYTE utilities"),
        (Pattern(@"gdrv\w*"),           "GIGABYTE utilities"),
        (Pattern(@"ngmsvs\w*"),         "ngmsvs driver"),
        (Pattern(@"winring0\w*"),       "WinRing0 (used by many fan, RGB and monitoring tools)"),
        (Pattern(@"rtcore(32|64)"),     "MSI Afterburner / RivaTuner (older versions)"),
        (Pattern(@"asio(2|3|64)?"),     "ASUS system utilities (AsIO)"),
        (Pattern(@"iqvw(32|64)e?"),     "Intel Network Adapter Diagnostic Driver"),
        (Pattern(@"dbutil_2_3"),        "Dell BIOS utility driver"),
        (Pattern(@"mhyprot\w*"),        "miHoYo anti-cheat"),
        (Pattern(@"amifldrv(32|64)"),   "AMI firmware flash utility"),
    ];

    private static Regex Pattern(string name) =>
        new($@"^({name})\.(sys|inf)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Scans kernel driver services and the driver store. Does I/O, so call it off the UI thread.</summary>
    public static List<DriverFinding> Scan(Action<string>? log)
    {
        var findings = new List<DriverFinding>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Installed kernel driver services (what actually loads at boot).
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT PathName, State FROM Win32_SystemDriver");
            using ManagementObjectCollection results = searcher.Get();
            foreach (ManagementBaseObject driver in results)
            {
                string? file = SafeFileName(driver["PathName"] as string);
                if (Match(file) is { } vendor && seen.Add(file!))
                {
                    string state = (driver["State"] as string ?? "unknown").ToLowerInvariant();
                    findings.Add(new DriverFinding(file!, $"kernel driver, {state}", vendor));
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke("Could not list kernel drivers: " + ex.Message);
        }

        // 2. Third-party driver packages in the driver store (folders are named "<inf>_<arch>_<hash>").
        try
        {
            string repository = Path.Combine(Environment.SystemDirectory, "DriverStore", "FileRepository");
            foreach (string dir in Directory.EnumerateDirectories(repository))
            {
                string folder = Path.GetFileName(dir);
                int cut = folder.IndexOf(".inf_", StringComparison.OrdinalIgnoreCase);
                if (cut <= 0)
                {
                    continue;
                }

                string inf = folder[..(cut + 4)];
                if (Match(inf) is { } vendor && seen.Add(inf))
                {
                    findings.Add(new DriverFinding(inf, "driver store package", vendor));
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke("Could not read the driver store: " + ex.Message);
        }

        return findings;
    }

    private static string? Match(string? fileName) =>
        string.IsNullOrEmpty(fileName)
            ? null
            : KnownDrivers.FirstOrDefault(d => d.Pattern.IsMatch(fileName)).Vendor;

    private static string? SafeFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFileName(path.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
